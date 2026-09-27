using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Members;

/// <summary>Lokale velden die het bestuur mag wijzigen; e-Boekhouden-velden zijn read-only (docs/04 §4).</summary>
public sealed record MemberLocalUpdate(
    MembershipStatus? LocalStatusOverride,
    DateOnly? MembershipValidFrom,
    DateOnly? MembershipValidTo,
    string? FirstName,
    string? NamePrefix,
    string? LastName,
    DateOnly? BirthDate,
    short? JoinYear);

public sealed record MemberPurgeResult(int Members, int SyncJobs, int UnlinkedAccounts);

public sealed record MemberRemovalResult(string MemberNumber, int Accounts, int Applications);

/// <summary>Instellingen rond testdata: leegmaken mag alleen in Dev en Acc (afgeleid van de omgeving).</summary>
public sealed class MemberDataOptions
{
    public bool AllowPurge { get; set; }
}

/// <summary>Ledenbeheer in het portal (fase 8): lokale velden, bevestigen van verdwenen leden, conflicten en opruimen.</summary>
public sealed class MemberAdministration(
    DrammersDbContext db, IAuditLogger audit, IClock clock, MemberSyncSettings settings, ConfigurationAdministration configuration,
    IOptions<MemberDataOptions> dataOptions, Identity.AccountLifecycle lifecycle, Identity.MyAccount accounts, ICurrentActor actor)
{
    /// <summary>Deze tekst moet letterlijk worden meegestuurd om alle leden te verwijderen.</summary>
    public const string PurgeConfirmation = "LEDEN VERWIJDEREN";

    /// <summary>Deze tekst moet letterlijk worden meegestuurd om één lid volledig te verwijderen.</summary>
    public const string RemoveConfirmation = "VERWIJDEREN";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task UpdateLocalAsync(Guid id, MemberLocalUpdate update, CancellationToken cancellationToken)
    {
        var member = await FindAsync(id, cancellationToken);
        if (update.MembershipValidFrom is { } from && update.MembershipValidTo is { } to && to < from)
        {
            throw new DomainException(ErrorCodes.Validation, "De einddatum ligt vóór de begindatum.");
        }

        if (update.LocalStatusOverride == MembershipStatus.Active)
        {
            throw new DomainException(ErrorCodes.Validation, "Een override is alleen voor Geschorst, Overleden of Inactief; laat hem leeg om de status uit e-Boekhouden te volgen.");
        }

        var mapping = await settings.GetMappingAsync(cancellationToken);
        if (update.BirthDate != member.BirthDate && mapping.BirthDate is not null)
        {
            throw new DomainException(ErrorCodes.MemberFieldFromSource, "De geboortedatum komt uit e-Boekhouden en is daar te wijzigen.");
        }

        if (update.JoinYear != member.JoinYear && mapping.JoinYear is not null)
        {
            throw new DomainException(ErrorCodes.MemberFieldFromSource, "Het inschrijfjaar komt uit e-Boekhouden en is daar te wijzigen.");
        }

        var before = Snapshot(member);
        var nameChanged = update.FirstName != member.FirstName || update.NamePrefix != member.NamePrefix || update.LastName != member.LastName;
        member.LocalStatusOverride = update.LocalStatusOverride;
        member.MembershipValidFrom = update.MembershipValidFrom;
        member.MembershipValidTo = update.MembershipValidTo;
        member.BirthDate = update.BirthDate;
        member.JoinYear = update.JoinYear;
        if (nameChanged)
        {
            member.FirstName = Clean(update.FirstName);
            member.NamePrefix = Clean(update.NamePrefix);
            member.LastName = Clean(update.LastName);
            member.NameCorrectedManually = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.updated", "Member", id.ToString(), before, Snapshot(member)), cancellationToken);
        await lifecycle.ReconcileAsync([id], cancellationToken);
    }

    /// <summary>Een lid dat uit e-Boekhouden verdwenen is, direct op inactief zetten (in plaats van de volgende run af te wachten).</summary>
    public async Task ConfirmInactiveAsync(Guid id, CancellationToken cancellationToken)
    {
        var member = await FindAsync(id, cancellationToken);
        if (member.SyncState != MemberSyncState.Missing)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen een lid dat in e-Boekhouden ontbreekt, kan zo op inactief worden gezet.");
        }

        member.MembershipStatus = MembershipStatus.Inactive;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.confirmed-inactive", "Member", id.ToString()), cancellationToken);
        await lifecycle.ReconcileAsync([id], cancellationToken);
    }

    public async Task ResolveConflictAsync(Guid id, SyncConflictStatus resolution, string? note, Guid? resolvedBy, CancellationToken cancellationToken)
    {
        if (resolution == SyncConflictStatus.Open)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies Geaccepteerd of Genegeerd.");
        }

        var conflict = await db.SyncConflicts.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Conflict niet gevonden.", DomainErrorKind.NotFound);
        if (conflict.Status != SyncConflictStatus.Open)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit conflict is al afgehandeld.", DomainErrorKind.Conflict);
        }

        conflict.Status = resolution;
        conflict.ResolutionNote = Clean(note);
        conflict.ResolvedBy = resolvedBy;
        conflict.ResolvedAt = clock.UtcNow.UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member-sync.conflict-resolved", "SyncConflict", id.ToString(), null,
            JsonSerializer.Serialize(new { type = conflict.Type.ToString(), resolution = resolution.ToString() }, Json)), cancellationToken);
    }

    /// <summary>
    /// Verwijdert alle leden, syncruns en conflicten en ontkoppelt app-accounts; zet de nachtelijke sync uit zodat de
    /// leden niet vanzelf terugkomen. Alleen in Dev en Acc (besluit 2026-09-25: echte leden tijdelijk in Dev).
    /// </summary>
    public async Task<MemberPurgeResult> PurgeAsync(string confirmation, CancellationToken cancellationToken)
    {
        if (!dataOptions.Value.AllowPurge)
        {
            throw new DomainException(ErrorCodes.PurgeNotAllowed, "Leden verwijderen kan alleen in de test- en acceptatieomgeving.", DomainErrorKind.Conflict);
        }

        if (confirmation != PurgeConfirmation)
        {
            throw new DomainException(ErrorCodes.Validation, $"Typ ter bevestiging \"{PurgeConfirmation}\".");
        }

        if (await db.SyncJobs.AnyAsync(j => j.Status == SyncJobStatus.Queued || j.Status == SyncJobStatus.Running, cancellationToken))
        {
            throw new DomainException(ErrorCodes.SyncAlreadyRunning, "Er loopt een ledensync. Wacht tot die klaar is.", DomainErrorKind.Conflict);
        }

        await configuration.SetFeatureFlagAsync(MemberSyncSettings.ScheduleFlag, false, "Uitgezet bij het verwijderen van alle leden", cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var unlinked = await db.Users.Where(u => u.MemberId != null).ExecuteUpdateAsync(s => s.SetProperty(u => u.MemberId, (Guid?)null), cancellationToken);
        await db.SyncConflicts.ExecuteDeleteAsync(cancellationToken);
        await db.SyncJobItems.ExecuteDeleteAsync(cancellationToken);
        var jobs = await db.SyncJobs.ExecuteDeleteAsync(cancellationToken);
        var members = await db.Members.ExecuteDeleteAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("member.purged", "Member", "*", null,
            JsonSerializer.Serialize(new { members, syncJobs = jobs, unlinkedAccounts = unlinked }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new MemberPurgeResult(members, jobs, unlinked);
    }

    /// <summary>
    /// Verwijdert één lid volledig uit de app (fase 9b): accounts en inlogs die aan het lid hangen (AVG-wissen), het lid
    /// zelf met groepen, ouderkoppelingen, aanmeldingen, accountverzoeken, doelgroepen in content en syncregels. Het
    /// lidnummer komt op de uitsluitlijst, zodat de sync het lid niet terugzet zolang het nog in e-Boekhouden staat.
    /// e-Boekhouden zelf blijft ongemoeid (later: status "opgezegd" zetten).
    /// </summary>
    public async Task<MemberRemovalResult> RemoveCompletelyAsync(Guid id, string confirmation, CancellationToken cancellationToken)
    {
        if (confirmation != RemoveConfirmation)
        {
            throw new DomainException(ErrorCodes.Validation, $"Typ ter bevestiging \"{RemoveConfirmation}\".");
        }

        var member = await FindAsync(id, cancellationToken);
        var number = member.MemberNumber;
        var memberRef = id.ToString();

        // Eerst de accounts: dat kan geweigerd worden (laatste beheerder); dan is er nog niets verwijderd.
        var userIds = await db.Users.Where(u => u.MemberId == id && u.AccountStatus != Modules.Identity.Users.AccountStatus.Deleted)
            .Select(u => u.Id).ToListAsync(cancellationToken);
        var erasedApplications = 0;
        foreach (var userId in userIds)
        {
            erasedApplications += (await accounts.EraseAsync(userId, cancellationToken)).Applications;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var applications = await db.MembershipApplications.Where(a => a.ResultingMemberId == id).ExecuteDeleteAsync(cancellationToken);
        await db.AccountRequests.Where(r => r.MemberId == id).ExecuteDeleteAsync(cancellationToken);
        await db.AccountProvisioning.Where(p => p.MemberId == id).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Modules.Content.Events.EventAudience>().Where(a => a.AudienceType == Modules.Content.Shared.AudienceType.Member && a.AudienceRef == memberRef).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Modules.Content.News.NewsAudience>().Where(a => a.AudienceType == Modules.Content.Shared.AudienceType.Member && a.AudienceRef == memberRef).ExecuteDeleteAsync(cancellationToken);
        await db.Set<Modules.Content.Photos.PhotoAlbumAudience>().Where(a => a.AudienceType == Modules.Content.Shared.AudienceType.Member && a.AudienceRef == memberRef).ExecuteDeleteAsync(cancellationToken);
        await db.SyncConflicts.Where(c => c.MemberId == id || c.MemberNumber == number).ExecuteDeleteAsync(cancellationToken);
        await db.SyncJobItems.Where(i => i.MemberId == id || i.MemberNumber == number).ExecuteDeleteAsync(cancellationToken);
        // Groepslidmaatschappen en ouderkoppelingen gaan mee via cascade.
        await db.Members.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (!await db.ExcludedMembers.AnyAsync(e => e.MemberNumber == number, cancellationToken))
        {
            db.ExcludedMembers.Add(new ExcludedMember { MemberNumber = number, ExcludedAt = clock.UtcNow.UtcDateTime, ExcludedBy = actor.UserId });
            await db.SaveChangesAsync(cancellationToken);
        }

        var result = new MemberRemovalResult(number, userIds.Count, erasedApplications + applications);
        await audit.WriteAsync(new AuditEntry("member.removed", "Member", id.ToString(), null, JsonSerializer.Serialize(result, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    /// <summary>Uitsluiting opheffen: bij de volgende sync komt het lid (als het nog in e-Boekhouden staat) terug.</summary>
    public async Task IncludeAgainAsync(string memberNumber, CancellationToken cancellationToken)
    {
        var removed = await db.ExcludedMembers.Where(e => e.MemberNumber == memberNumber).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            throw new DomainException(ErrorCodes.MemberNotFound, "Dit lidnummer staat niet op de uitsluitlijst.", DomainErrorKind.NotFound);
        }

        await audit.WriteAsync(new AuditEntry("member.included-again", "ExcludedMember", memberNumber), cancellationToken);
    }

    private async Task<Member> FindAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Members.SingleOrDefaultAsync(m => m.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.MemberNotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);

    /// <summary>Alleen lokale velden in de auditlog (geen NAW uit e-Boekhouden).</summary>
    private static string Snapshot(Member m) => JsonSerializer.Serialize(new
    {
        localStatusOverride = m.LocalStatusOverride?.ToString(),
        m.MembershipValidFrom,
        m.MembershipValidTo,
        m.NameCorrectedManually,
        birthDateSet = m.BirthDate is not null,
        m.JoinYear,
    }, Json);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
