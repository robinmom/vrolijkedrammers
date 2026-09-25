using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.EBoekhouden;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Members;

/// <summary>
/// Ledensync e-Boekhouden → app (ADR-010): volledig vergelijken op hash, per lid opslaan, dry-run zonder schrijfacties,
/// massadeletie-guard en conflicten voor het bestuur. Schrijft nooit naar e-Boekhouden.
/// </summary>
public sealed class MemberSync(
    DrammersDbContext db, IEBoekhoudenClient eBoekhouden, MemberSyncSettings settings, IOutbox outbox, IAuditLogger audit,
    IClock clock, ILogger<MemberSync> logger)
{
    public const string MessageType = "members.sync";

    /// <summary>Boven dit aandeel ontbrekende actieve leden wordt niemand gedeactiveerd (ADR-010).</summary>
    public const double MassDeletionThreshold = 0.10;

    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

    /// <summary>Zet een run in de wachtrij (outbox); de worker voert hem uit. Maximaal één run tegelijk.</summary>
    public async Task<Guid> RequestAsync(bool dryRun, SyncTrigger trigger, Guid? requestedBy, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Een run die door een herstart is blijven hangen, blokkeert niet voor altijd.
        await db.SyncJobs
            .Where(j => (j.Status == SyncJobStatus.Queued || j.Status == SyncJobStatus.Running) && j.RequestedAt < now - StaleAfter)
            .ExecuteUpdateAsync(s => s
                .SetProperty(j => j.Status, SyncJobStatus.Failed)
                .SetProperty(j => j.CompletedAt, now)
                .SetProperty(j => j.ErrorMessage, "Afgebroken: de run is niet binnen 30 minuten afgerond."), cancellationToken);

        if (await db.SyncJobs.AnyAsync(j => j.Status == SyncJobStatus.Queued || j.Status == SyncJobStatus.Running, cancellationToken))
        {
            throw new DomainException(ErrorCodes.SyncAlreadyRunning, "Er loopt al een ledensync. Wacht tot die klaar is.", DomainErrorKind.Conflict);
        }

        var job = new SyncJob { Id = IdGenerator.NewId(), Status = SyncJobStatus.Queued, DryRun = dryRun, Trigger = trigger, RequestedBy = requestedBy, RequestedAt = now };
        db.SyncJobs.Add(job);
        outbox.Enqueue(MessageType, new SyncMessage(job.Id));
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-sync.requested", "SyncJob", job.Id.ToString(), null,
            JsonSerializer.Serialize(new { dryRun, trigger = trigger.ToString() })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job.Id;
    }

    /// <summary>Voert een run uit die in de wachtrij staat. Idempotent: een run die al gestart is, wordt overgeslagen.</summary>
    public async Task RunAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow.UtcDateTime;
        var claimed = await db.SyncJobs.Where(j => j.Id == jobId && j.Status == SyncJobStatus.Queued)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SyncJobStatus.Running).SetProperty(j => j.StartedAt, now), cancellationToken);
        if (claimed == 0)
        {
            return;
        }

        var job = await db.SyncJobs.SingleAsync(j => j.Id == jobId, cancellationToken);
        try
        {
            var mapping = await settings.GetMappingAsync(cancellationToken);
            await using var session = await eBoekhouden.OpenSessionAsync(cancellationToken);
            var references = await session.ListMembersAsync(cancellationToken);
            job.TotalInSource = references.Count;
            await new Run(db, clock.UtcNow.UtcDateTime, logger, job, mapping, session).ExecuteAsync(references, cancellationToken);
        }
        catch (EBoekhoudenException ex)
        {
            logger.LogError(ex, "Ledensync {JobId} mislukt", jobId);
            job.Status = SyncJobStatus.Failed;
            job.ErrorMessage = ex.Message;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Ledensync {JobId} mislukt door een onverwachte fout", jobId);
            job.Status = SyncJobStatus.Failed;
            job.ErrorMessage = "Onverwachte fout; zie de applicatielogs.";
        }

        job.CompletedAt = clock.UtcNow.UtcDateTime;
        db.ChangeTracker.Clear();
        db.SyncJobs.Update(job);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-sync.completed", "SyncJob", job.Id.ToString(), null, JsonSerializer.Serialize(new
        {
            job.DryRun,
            status = job.Status.ToString(),
            job.TotalInSource,
            job.Created,
            job.Updated,
            job.Unchanged,
            job.Missing,
            job.Deactivated,
            job.Reactivated,
            job.Warnings,
            job.Errors,
            job.Conflicts,
        })), cancellationToken);
        if (job.Status == SyncJobStatus.Conflict && job.Conflicts > 0)
        {
            logger.LogWarning("Ledensync {JobId} heeft {Conflicts} conflict(en) die beoordeeld moeten worden", job.Id, job.Conflicts);
        }
    }

    public sealed record SyncMessage(Guid SyncJobId);

    /// <summary>De verwerking van één run; houdt de lokale leden en tellingen bij.</summary>
    private sealed class Run(DrammersDbContext db, DateTime now, ILogger logger, SyncJob job, MemberFieldMapping mapping, IEBoekhoudenSession session)
    {
        private const int MaxConsecutiveErrors = 5;

        private readonly List<SyncJobItem> pendingItems = [];

        public async Task ExecuteAsync(IReadOnlyList<EbMemberReference> references, CancellationToken cancellationToken)
        {
            // Dry-run: niets volgen, dus niets kan per ongeluk worden opgeslagen.
            var members = job.DryRun
                ? await db.Members.AsNoTracking().ToListAsync(cancellationToken)
                : await db.Members.ToListAsync(cancellationToken);
            var byNumber = members.ToDictionary(m => m.MemberNumber, StringComparer.OrdinalIgnoreCase);
            var byEbId = members.Where(m => m.EbMemberId is not null).ToDictionary(m => m.EbMemberId!.Value);
            var withActiveAccount = (await db.Users.AsNoTracking()
                .Where(u => u.MemberId != null && u.AccountStatus == AccountStatus.Active)
                .Select(u => u.MemberId!.Value).ToListAsync(cancellationToken)).ToHashSet();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var consecutiveErrors = 0;

            foreach (var reference in references)
            {
                EbMember source;
                try
                {
                    source = await session.GetMemberAsync(reference.Id, cancellationToken);
                    consecutiveErrors = 0;
                }
                catch (EBoekhoudenException ex)
                {
                    if (++consecutiveErrors >= MaxConsecutiveErrors)
                    {
                        throw;
                    }

                    await AddItemAsync(reference.MemberNumber ?? $"#{reference.Id}", null, SyncItemAction.Error, null, ex.Message, cancellationToken);
                    continue;
                }

                var number = source.MemberNumber?.Trim();
                if (string.IsNullOrEmpty(number))
                {
                    await AddItemAsync($"#{source.Id}", null, SyncItemAction.Error, null, "Lid zonder lidnummer in e-Boekhouden.", cancellationToken);
                    continue;
                }

                if (!seen.Add(number))
                {
                    await ConflictAsync(SyncConflictType.DuplicateMemberNumber, number, byNumber.GetValueOrDefault(number)?.Id,
                        $"Lidnummer {number} komt meer dan één keer voor in e-Boekhouden; alleen het eerste is verwerkt.", cancellationToken);
                    continue;
                }

                var local = byNumber.GetValueOrDefault(number);
                if (local is null && byEbId.TryGetValue(source.Id, out var renumbered))
                {
                    await ConflictAsync(SyncConflictType.MemberNumberChanged, number, renumbered.Id,
                        $"Het lid met lidnummer {renumbered.MemberNumber} heeft in e-Boekhouden nu lidnummer {number}. Niet automatisch verwerkt.", cancellationToken);
                    seen.Add(renumbered.MemberNumber);
                    continue;
                }

                if (local is null)
                {
                    await CreateAsync(source, number, cancellationToken);
                }
                else
                {
                    await UpdateAsync(local, source, withActiveAccount.Contains(local.Id), cancellationToken);
                }
            }

            await HandleMissingAsync(members.Where(m => !seen.Contains(m.MemberNumber)).ToList(), members, cancellationToken);
            await FlushItemsAsync(cancellationToken);

            job.Status = job.Conflicts > 0 ? SyncJobStatus.Conflict
                : job.Warnings > 0 || job.Errors > 0 ? SyncJobStatus.SucceededWithWarnings
                : SyncJobStatus.Succeeded;
        }

        private async Task CreateAsync(EbMember source, string number, CancellationToken cancellationToken)
        {
            var member = new Member { Id = IdGenerator.NewId(), MemberNumber = number, FullName = string.Empty, MembershipStatus = MembershipStatus.Active };
            var warnings = Apply(member, source, isNew: true);
            member.SyncState = MemberSyncState.InSync;
            member.EbLastSeenAt = now;
            member.MembershipStatus = DeriveStatus(member);
            if (!job.DryRun)
            {
                db.Members.Add(member);
            }

            job.Created++;
            await AddItemAsync(number, member.Id, SyncItemAction.Created, null, null, cancellationToken, warnings);
        }

        private async Task UpdateAsync(Member member, EbMember source, bool hasActiveAccount, CancellationToken cancellationToken)
        {
            var wasMissing = member.SyncState == MemberSyncState.Missing;
            var hash = Hash(source);
            member.EbLastSeenAt = now;
            member.EbMemberId ??= source.Id;

            if (hash.AsSpan().SequenceEqual(member.EbHash) && !wasMissing)
            {
                job.Unchanged++;
                await AddItemAsync(member.MemberNumber, member.Id, SyncItemAction.Unchanged, null, null, cancellationToken);
                return;
            }

            var before = Fields(member);
            var oldEmail = member.Email;
            var warnings = Apply(member, source, isNew: false);
            var changed = before.Where(kv => Fields(member)[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();

            if (hasActiveAccount && !string.Equals(Normalize(oldEmail), Normalize(member.Email), StringComparison.OrdinalIgnoreCase))
            {
                // Het e-mailadres van het lid volgt e-Boekhouden; het inlogadres van het account niet (ADR-010).
                await ConflictAsync(SyncConflictType.EmailChangedForActiveAccount, member.MemberNumber, member.Id,
                    "Het e-mailadres is gewijzigd in e-Boekhouden, terwijl dit lid een actief app-account heeft. Het inlogadres is niet aangepast.",
                    cancellationToken);
            }

            if (wasMissing)
            {
                member.SyncState = MemberSyncState.InSync;
                member.EbMissingSince = null;
                job.Reactivated++;
            }
            else
            {
                job.Updated++;
            }

            member.MembershipStatus = DeriveStatus(member);
            await AddItemAsync(member.MemberNumber, member.Id, wasMissing ? SyncItemAction.Reactivated : SyncItemAction.Updated,
                changed.Count > 0 ? string.Join(',', changed) : null, null, cancellationToken, warnings);
        }

        /// <summary>
        /// Leden die niet meer in e-Boekhouden staan: eerst <c>Missing</c>; bij de volgende run nog steeds weg →
        /// <c>Inactive</c>. Ontbreekt meer dan 10 % van de actieve leden, dan wordt niemand aangeraakt.
        /// </summary>
        private async Task HandleMissingAsync(List<Member> missing, List<Member> all, CancellationToken cancellationToken)
        {
            var candidates = missing.Where(m => m.MembershipStatus == MembershipStatus.Active).ToList();
            var active = all.Count(m => m.MembershipStatus == MembershipStatus.Active);
            if (candidates.Count >= 2 && candidates.Count > active * MassDeletionThreshold)
            {
                await ConflictAsync(SyncConflictType.MassDeletionGuard, null, null,
                    $"{candidates.Count} van de {active} actieve leden ontbreken in e-Boekhouden (meer dan 10 %). Er is niemand gedeactiveerd; controleer e-Boekhouden en de koppeling.",
                    cancellationToken);
                logger.LogError("Massadeletie-guard: {Missing} van {Active} actieve leden ontbreken", candidates.Count, active);
                return;
            }

            foreach (var member in candidates)
            {
                if (member.SyncState == MemberSyncState.Missing)
                {
                    member.MembershipStatus = MembershipStatus.Inactive;
                    job.Deactivated++;
                    await AddItemAsync(member.MemberNumber, member.Id, SyncItemAction.Deactivated, null,
                        "Ook in de vorige run niet gevonden; op inactief gezet.", cancellationToken);
                }
                else
                {
                    member.SyncState = MemberSyncState.Missing;
                    member.EbMissingSince = now;
                    job.Missing++;
                    await AddItemAsync(member.MemberNumber, member.Id, SyncItemAction.Missing, null,
                        "Niet gevonden in e-Boekhouden; bij de volgende run of na bevestiging inactief.", cancellationToken);
                }
            }
        }

        /// <summary>Neemt de e-Boekhouden-velden over; geeft parsewaarschuwingen terug (oude waarde blijft dan staan).</summary>
        private List<string> Apply(Member member, EbMember source, bool isNew)
        {
            var warnings = new List<string>();
            var oldFullName = member.FullName;
            member.EbMemberId = source.Id;
            member.FullName = Clip(source.Name, 100) ?? member.MemberNumber;
            member.Salutation = Clip(source.Salutation, 50);
            member.Gender = source.Gender is "m" or "v" or "a" ? source.Gender : null;
            member.AddressLine = Clip(source.Address, 150);
            member.PostalCode = Clip(source.PostalCode, 50);
            member.City = Clip(source.City, 50);
            member.Country = Clip(source.Country, 50);
            member.Email = Clip(Normalize(source.EmailAddress), 150);
            member.Phone = Clip(source.PhoneNumber, 50);
            member.MobilePhone = Clip(source.MobilePhoneNumber, 50);

            if (mapping.BirthDate is { } birthField)
            {
                var raw = source.FreeText(birthField)?.Trim();
                if (string.IsNullOrEmpty(raw))
                {
                    member.BirthDate = null;
                }
                else if (TryParseDate(raw, out var date))
                {
                    member.BirthDate = date;
                }
                else
                {
                    warnings.Add($"Geboortedatum in {birthField} is geen geldige datum (verwacht JJJJ-MM-DD).");
                }
            }

            if (mapping.JoinYear is { } yearField)
            {
                var raw = source.FreeText(yearField)?.Trim();
                if (string.IsNullOrEmpty(raw))
                {
                    member.JoinYear = null;
                }
                else if (short.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var year) && year >= 1900 && year <= DateTime.UtcNow.Year + 1)
                {
                    member.JoinYear = year;
                }
                else
                {
                    warnings.Add($"Inschrijfjaar in {yearField} is geen geldig jaartal (verwacht JJJJ).");
                }
            }

            if (mapping.Status is { } statusField)
            {
                member.EbStatusRaw = Clip(source.FreeText(statusField)?.Trim(), 100);
            }

            if (mapping.Category is { } categoryField)
            {
                member.MemberCategory = Clip(source.FreeText(categoryField)?.Trim(), 50);
            }

            if ((isNew || oldFullName != member.FullName) && !member.NameCorrectedManually)
            {
                var name = DutchNameParser.Parse(member.FullName);
                member.FirstName = Clip(name.FirstName, 100);
                member.NamePrefix = Clip(name.NamePrefix, 30);
                member.LastName = Clip(name.LastName, 100);
            }

            member.EbHash = Hash(source);
            return warnings;
        }

        private MembershipStatus DeriveStatus(Member member) =>
            member.EbStatusRaw is { } raw && mapping.InactiveStatusValues.Contains(raw.Trim(), StringComparer.OrdinalIgnoreCase)
                ? MembershipStatus.Inactive
                : MembershipStatus.Active;

        /// <summary>SHA-256 over de canonieke weergave van alle overgenomen velden, inclusief de gemapte vrije velden.</summary>
        private byte[] Hash(EbMember source)
        {
            string?[] values =
            [
                source.MemberNumber?.Trim(), source.Name, source.Salutation, source.Gender, source.Address, source.PostalCode,
                source.City, source.Country, source.PhoneNumber, source.MobilePhoneNumber, Normalize(source.EmailAddress),
                mapping.BirthDate is null ? null : source.FreeText(mapping.BirthDate),
                mapping.JoinYear is null ? null : source.FreeText(mapping.JoinYear),
                mapping.Status is null ? null : source.FreeText(mapping.Status),
                mapping.Category is null ? null : source.FreeText(mapping.Category),
                string.Join('|', mapping.BirthDate, mapping.JoinYear, mapping.Status, mapping.Category),
            ];
            return SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(values)));
        }

        private static Dictionary<string, string?> Fields(Member m) => new()
        {
            ["name"] = m.FullName,
            ["salutation"] = m.Salutation,
            ["gender"] = m.Gender,
            ["address"] = m.AddressLine,
            ["postalCode"] = m.PostalCode,
            ["city"] = m.City,
            ["country"] = m.Country,
            ["email"] = m.Email,
            ["phone"] = m.Phone,
            ["mobilePhone"] = m.MobilePhone,
            ["birthDate"] = m.BirthDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["joinYear"] = m.JoinYear?.ToString(CultureInfo.InvariantCulture),
            ["status"] = m.EbStatusRaw,
            ["category"] = m.MemberCategory,
        };

        private async Task ConflictAsync(SyncConflictType type, string? memberNumber, Guid? memberId, string details, CancellationToken cancellationToken)
        {
            job.Conflicts++;
            if (!job.DryRun)
            {
                db.SyncConflicts.Add(new SyncConflict
                {
                    Id = IdGenerator.NewId(),
                    SyncJobId = job.Id,
                    Type = type,
                    MemberNumber = memberNumber,
                    MemberId = memberId,
                    Details = details,
                    Status = SyncConflictStatus.Open,
                    CreatedAt = now,
                });
            }

            await AddItemAsync(memberNumber ?? "-", memberId, SyncItemAction.Conflict, null, details, cancellationToken);
        }

        private async Task AddItemAsync(
            string memberNumber, Guid? memberId, SyncItemAction action, string? changedFields, string? message,
            CancellationToken cancellationToken, List<string>? warnings = null)
        {
            if (action == SyncItemAction.Error)
            {
                job.Errors++;
            }

            pendingItems.Add(new SyncJobItem { SyncJobId = job.Id, MemberNumber = Clip(memberNumber, 20)!, MemberId = memberId, Action = action, ChangedFields = changedFields, Message = message });
            foreach (var warning in warnings ?? [])
            {
                job.Warnings++;
                pendingItems.Add(new SyncJobItem { SyncJobId = job.Id, MemberNumber = Clip(memberNumber, 20)!, MemberId = memberId, Action = SyncItemAction.Warning, Message = warning });
            }

            // Echte run: per lid opslaan (ADR-010), zodat één fout de rest niet tegenhoudt. Dry-run: in blokken.
            if (!job.DryRun || pendingItems.Count >= 100)
            {
                await FlushItemsAsync(cancellationToken);
            }
        }

        private async Task FlushItemsAsync(CancellationToken cancellationToken)
        {
            db.SyncJobItems.AddRange(pendingItems);
            var numbers = pendingItems.Select(i => i.MemberNumber).Distinct().ToList();
            pendingItems.Clear();
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                // Eén lid dat niet opgeslagen kan worden, houdt de rest niet tegen: zijn wijzigingen vervallen, de run gaat door.
                logger.LogError(ex, "Opslaan tijdens ledensync {JobId} mislukt voor lidnummer(s) {Numbers}", job.Id, numbers);
                foreach (var entry in db.ChangeTracker.Entries().Where(e => e.Entity is not SyncJob && e.State != EntityState.Unchanged).ToList())
                {
                    entry.State = EntityState.Detached;
                }

                job.Errors++;
                db.SyncJobItems.AddRange(numbers.Select(n => new SyncJobItem
                {
                    SyncJobId = job.Id,
                    MemberNumber = n,
                    Action = SyncItemAction.Error,
                    Message = "Opslaan mislukt; zie de applicatielogs.",
                }));
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        private static bool TryParseDate(string value, out DateOnly date) =>
            DateOnly.TryParseExact(value, ["yyyy-MM-dd", "dd-MM-yyyy", "d-M-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static string? Normalize(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private static string? Clip(string? value, int max)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}

/// <summary>Voert een ledensync uit die via de outbox is aangevraagd (worker).</summary>
public sealed class MemberSyncHandler(MemberSync sync) : IOutboxMessageHandler
{
    public string Type => MemberSync.MessageType;

    public Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken) =>
        sync.RunAsync(JsonSerializer.Deserialize<MemberSync.SyncMessage>(message.Payload, JsonSerializerOptions.Web)!.SyncJobId, cancellationToken);
}
