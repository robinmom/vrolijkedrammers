using System.Text.Json;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record AddressInput(string? Street, string? HouseNumber, string? Addition, string? PostalCode, string? City, string? Country);

/// <summary>Alle velden die de groep invult (wizard); het opgavenummer bestaat hier bewust niet (ADR-011).</summary>
public sealed record RegistrationInput(
    string? GroupName,
    string? ContactName,
    string? ContactPhone,
    string? ContactEmail,
    int? CategoryId,
    string? Subject,
    string? SubjectDescription,
    int ChildrenCount,
    int AdultCount,
    AddressInput? BuildAddress,
    bool JuryInspectionSameAsBuildAddress,
    AddressInput? JuryInspectionAddress,
    decimal? EstimatedLengthMeters,
    string? AdditionalInformation,
    bool? HasMusic = null);

public sealed record UploadedDocument(string FileName, long Length, DocumentType Type, Func<Stream> Open);

/// <summary>
/// Optochtinschrijvingen van leden (fase 11a, docs/13): concept, autosave met versiecontrole (412), valideren, definitief
/// indienen met opgavenummer (ADR-011), intrekken, mede-beheerders en documenten. Alleen beheerders van de inschrijving
/// zien haar (anders 404, BOLA). Wat na indienen nog mag, volgt uit het statusbeleid (docs/14 §9).
/// </summary>
public sealed class ParadeRegistrations(
    DrammersDbContext db,
    ParadeAdministration parades,
    ContentFiles contentFiles,
    IFileStore files,
    INotificationService notifications,
    IOutbox outbox,
    IAuditLogger audit,
    ParadeChangeContext changeContext,
    IClock clock)
{
    public const string SubmittedMailMessageType = "parade.submitted-mail";

    public sealed record SubmittedMail(Guid RegistrationId);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private DateTime Now => clock.UtcNow.UtcDateTime;

    // ----- Lezen ------------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ParadeRegistration>> MineAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ParadeRegistrations.AsNoTracking()
            .Where(r => db.ParadeRegistrationManagers.Any(m => m.RegistrationId == r.Id && m.UserId == userId))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<ParadeRegistration> GetAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.ParadeRegistrations.AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == id && db.ParadeRegistrationManagers.Any(m => m.RegistrationId == r.Id && m.UserId == userId), cancellationToken)
        ?? throw NotFound();

    private async Task<ParadeRegistration> TrackedAsync(Guid userId, Guid id, CancellationToken cancellationToken) =>
        await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == id && db.ParadeRegistrationManagers.Any(m => m.RegistrationId == r.Id && m.UserId == userId), cancellationToken)
        ?? throw NotFound();

    /// <summary>Velden die de groep nu mag wijzigen (<c>*</c> = alles), en of intrekken mag.</summary>
    public async Task<(IReadOnlySet<string> Fields, bool CanWithdraw)> OwnerPolicyAsync(ParadeRegistration registration, CancellationToken cancellationToken)
    {
        var policies = await db.ParadeStatusEditPolicies.AsNoTracking()
            .Where(p => p.Status == registration.Status && p.ActorScope == ActorScope.Owner && (p.ParadeId == null || p.ParadeId == registration.ParadeId))
            .ToListAsync(cancellationToken);
        var policy = policies.OrderByDescending(p => p.ParadeId.HasValue).FirstOrDefault();
        if (policy is null)
        {
            return (new HashSet<string>(), false);
        }

        var fields = policy.EditableFields == "*"
            ? RegistrationFields.All.ToHashSet()
            : policy.EditableFields.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();

        // Na de wijzigingsdeadline (standaard: sluiting van de inschrijving) alleen nog contactgegevens.
        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        // Uitzondering: om een aanvulling gevraagd door de commissie mag ook na de deadline.
        if (registration.Status is not (RegistrationStatus.Draft or RegistrationStatus.AdditionalInformationRequired)
            && Now >= (parade.EditDeadlineAt ?? parade.RegistrationClosesAt))
        {
            fields.IntersectWith([RegistrationFields.ContactName, RegistrationFields.ContactPhone, RegistrationFields.ContactEmail]);
        }

        return (fields, policy.CanWithdraw);
    }

    /// <summary>Toelichting van de commissie bij "Aanvulling gevraagd" of "Afgewezen" (voor de app); anders <c>null</c>.</summary>
    public async Task<string?> ReviewReasonAsync(ParadeRegistration registration, CancellationToken cancellationToken) =>
        registration.Status is RegistrationStatus.AdditionalInformationRequired or RegistrationStatus.Rejected
            ? await db.ParadeStatusHistory.AsNoTracking()
                .Where(h => h.RegistrationId == registration.Id && h.ToStatus == registration.Status)
                .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Select(h => h.Reason).FirstOrDefaultAsync(cancellationToken)
            : null;

    // ----- Concept ----------------------------------------------------------------------------------------------------

    /// <summary>
    /// Nieuw concept voor de huidige optocht, vooringevuld met de eigen contactgegevens, de groepsnaam uit e-Boekhouden
    /// en de laatst gebruikte bouwlocatie. Alleen voor groepsverantwoordelijken (<c>parade.register</c>, per gebruiker
    /// aangevinkt in het portal); de maker wordt eigenaar.
    /// </summary>
    public async Task<ParadeRegistration> CreateDraftAsync(UserAccess user, CancellationToken cancellationToken)
    {
        var parade = await parades.CurrentAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht om voor in te schrijven.", DomainErrorKind.NotFound);
        if (Now >= parade.RegistrationClosesAt || parade.Status is not (ParadeStatus.Planned or ParadeStatus.RegistrationOpen))
        {
            throw new DomainException(ErrorCodes.RegistrationClosed, "De inschrijving voor de optocht is gesloten.", DomainErrorKind.Conflict);
        }

        var member = user.MemberId is { } memberId
            ? await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => new { m.FullName, m.MobilePhone, m.Phone, m.Email, m.ParadeGroupName }).SingleOrDefaultAsync(cancellationToken)
            : null;
        // Eén inschrijving per persoon per optocht (besluit product owner 2026-09-28); na intrekken mag het opnieuw.
        if (await db.ParadeRegistrations.AnyAsync(r => r.ParadeId == parade.Id && r.OwnerUserId == user.UserId && r.Status != RegistrationStatus.Withdrawn, cancellationToken))
        {
            throw new DomainException(ErrorCodes.RegistrationExists,
                "Je hebt al een inschrijving voor deze optocht. Open die via Mijn inschrijving.", DomainErrorKind.Conflict);
        }

        var location = await db.ParadeBuildLocations.AsNoTracking().Where(l => l.UserId == user.UserId)
            .OrderByDescending(l => l.LastUsedAt).FirstOrDefaultAsync(cancellationToken);
        var registration = new ParadeRegistration
        {
            Id = IdGenerator.NewId(),
            ParadeId = parade.Id,
            CarnivalYearId = parade.CarnivalYearId,
            Status = RegistrationStatus.Draft,
            Source = RegistrationSource.App,
            OwnerUserId = user.UserId,
            ContactName = member?.FullName ?? user.DisplayName,
            ContactPhone = PhoneNormalizer.Normalize(member?.MobilePhone ?? member?.Phone),
            ContactEmail = member?.Email ?? user.Email,
            GroupName = member?.ParadeGroupName,
            BuildAddress = location is null ? new Address() : CloneAddress(location.Address),
        };
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.ParadeRegistrations.Add(registration);
        db.ParadeRegistrationManagers.Add(new ParadeRegistrationManager { RegistrationId = registration.Id, UserId = user.UserId, Role = ManagerRole.Owner, AddedAt = Now });
        db.ParadeStatusHistory.Add(new ParadeStatusHistory { RegistrationId = registration.Id, ToStatus = RegistrationStatus.Draft, ActorUserId = user.UserId, OccurredAt = Now });
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.created", "ParadeRegistration", registration.Id.ToString()), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return registration;
    }

    public async Task DeleteDraftAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var registration = await TrackedAsync(userId, id, cancellationToken);
        if (registration.Status != RegistrationStatus.Draft)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Een ingediende inschrijving kun je niet verwijderen; je kunt haar wel intrekken.", DomainErrorKind.Conflict);
        }

        var documents = await db.ParadeDocuments.Where(d => d.RegistrationId == id).ToListAsync(cancellationToken);
        db.ParadeRegistrations.Remove(registration);
        await db.SaveChangesAsync(cancellationToken);
        foreach (var document in documents)
        {
            await files.DeleteAsync(FileContainers.ParadeDocuments, document.BlobPath, cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("parade-registration.draft-deleted", "ParadeRegistration", id.ToString()), cancellationToken);
    }

    // ----- Bijwerken (autosave) ---------------------------------------------------------------------------------------

    /// <summary>
    /// Slaat de wizard op. <paramref name="version"/> is de versie die de app had; is de inschrijving intussen elders
    /// gewijzigd, dan 412. Na indienen: alleen velden uit het statusbeleid, en de regels gelden hard (geen blokkade).
    /// </summary>
    public async Task<(ParadeRegistration Registration, IReadOnlyList<ValidationIssue> Issues)> UpdateAsync(
        Guid userId, Guid id, RegistrationInput input, string? version, CancellationToken cancellationToken)
    {
        var registration = await TrackedAsync(userId, id, cancellationToken);
        if (version is not null && !string.Equals(version, Convert.ToBase64String(registration.RowVersion), StringComparison.Ordinal))
        {
            throw Changed();
        }

        var (allowed, _) = await OwnerPolicyAsync(registration, cancellationToken);
        var next = Apply(Clone(registration), input);
        var changed = ChangedFields(registration, next);
        var forbidden = changed.Where(f => !allowed.Contains(f)).ToList();
        if (forbidden.Count > 0)
        {
            throw new DomainException(ErrorCodes.FieldNotEditable,
                $"In deze fase kun je niet meer wijzigen: {string.Join(", ", forbidden.Select(f => RegistrationFields.Labels[f]))}. Neem contact op met de optochtcommissie.");
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        var category = await CategoryAsync(next.CategoryId, parade.Id, cancellationToken);
        var issues = RegistrationRules.Validate(next, category, parade.SubjectRequired, forSubmit: registration.Status != RegistrationStatus.Draft);
        PhoneIssue(input.ContactPhone, next.ContactPhone, issues);
        if (registration.Status != RegistrationStatus.Draft && issues.Any(i => i.Severity == IssueSeverity.Block))
        {
            throw Invalid("De wijziging voldoet niet aan de regels.", issues);
        }

        Apply(registration, input);
        registration.ValidationWarnings = Warnings(issues);
        changeContext.Source = RegistrationSource.App;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        return (registration, issues);
    }

    public async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var registration = await GetAsync(userId, id, cancellationToken);
        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        var issues = RegistrationRules.Validate(registration, await CategoryAsync(registration.CategoryId, parade.Id, cancellationToken), parade.SubjectRequired, forSubmit: true);
        if (!string.IsNullOrWhiteSpace(registration.ContactPhone) && PhoneNormalizer.Normalize(registration.ContactPhone) is null)
        {
            issues.Add(new ValidationIssue(RegistrationFields.ContactPhone, "Vul een geldig telefoonnummer in.", IssueSeverity.Block));
        }

        return issues;
    }

    // ----- Indienen (ADR-011) -----------------------------------------------------------------------------------------

    /// <summary>
    /// Definitief indienen: in één transactie het nummer reserveren (atomaire UPDATE op de teller, die gelijktijdige
    /// inzendingen per optocht serialiseert) en de inschrijving bijwerken. Opnieuw indienen van een al ingediende
    /// inschrijving geeft hetzelfde nummer terug (idempotent).
    /// </summary>
    public async Task<ParadeRegistration> SubmitAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var registration = await TrackedAsync(userId, id, cancellationToken);
        if (registration.Status == RegistrationStatus.AdditionalInformationRequired)
        {
            await SubmitSupplementAsync(userId, registration, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return registration;
        }

        if (registration.Status != RegistrationStatus.Draft)
        {
            return registration;
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        if (!parade.IsRegistrationOpen(Now))
        {
            throw new DomainException(ErrorCodes.RegistrationClosed,
                Now < parade.RegistrationOpensAt ? "De inschrijving is nog niet geopend." : "De inschrijving voor de optocht is gesloten.",
                DomainErrorKind.Conflict);
        }

        var issues = await ValidateAsync(userId, id, cancellationToken);
        if (issues.Any(i => i.Severity == IssueSeverity.Block))
        {
            throw Invalid("Nog niet alles is goed ingevuld.", issues);
        }

        var number = await NextNumberAsync(db, parade.Id, cancellationToken);

        registration.RegistrationNumber = number;
        registration.Status = RegistrationStatus.Submitted;
        registration.SubmittedAt = Now;
        registration.ValidationWarnings = Warnings(issues);
        db.ParadeStatusHistory.Add(new ParadeStatusHistory
        {
            RegistrationId = id,
            FromStatus = RegistrationStatus.Draft,
            ToStatus = RegistrationStatus.Submitted,
            ActorUserId = userId,
            OccurredAt = Now,
        });
        var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == id).Select(m => m.UserId).ToListAsync(cancellationToken);
        await notifications.EnqueueAsync(new SystemNotification(
            $"Inschrijving optocht ontvangen: nr. {number}",
            $"{registration.GroupName} heeft opgavenummer {number} (volgorde van binnenkomst). De optochtcommissie beoordeelt de inschrijving.",
            NotificationCategory.Parade, new NotificationAudience(UserIds: managers), "drammers://optocht"), cancellationToken);
        outbox.Enqueue(SubmittedMailMessageType, new SubmittedMail(id));
        await RememberLocationAsync(userId, registration.BuildAddress, cancellationToken);
        changeContext.Source = RegistrationSource.App;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        await audit.WriteAsync(new AuditEntry("parade-registration.submitted", "ParadeRegistration", id.ToString(), null,
            JsonSerializer.Serialize(new { registrationNumber = number }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return registration;
    }

    /// <summary>
    /// De groep dient de gevraagde aanvulling in: weer <c>UnderReview</c> (zelfde opgavenummer), met in de statushistorie
    /// <see cref="ParadeReview.SupplementReason"/>, zodat de commissie ziet dat er opnieuw beoordeeld moet worden.
    /// De commissie en de beheerders krijgen een melding.
    /// </summary>
    private async Task SubmitSupplementAsync(Guid userId, ParadeRegistration registration, CancellationToken cancellationToken)
    {
        var issues = await ValidateAsync(userId, registration.Id, cancellationToken);
        if (issues.Any(i => i.Severity == IssueSeverity.Block))
        {
            throw Invalid("Nog niet alles is goed ingevuld.", issues);
        }

        registration.Status = RegistrationStatus.UnderReview;
        registration.ValidationWarnings = Warnings(issues);
        db.ParadeStatusHistory.Add(new ParadeStatusHistory
        {
            RegistrationId = registration.Id,
            FromStatus = RegistrationStatus.AdditionalInformationRequired,
            ToStatus = RegistrationStatus.UnderReview,
            ActorUserId = userId,
            Reason = ParadeReview.SupplementReason,
            OccurredAt = Now,
        });
        var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == registration.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
        await notifications.EnqueueAsync(new SystemNotification(
            "Aanvulling optocht ontvangen",
            $"{registration.GroupName} (nr. {registration.RegistrationNumber}) heeft de gevraagde aanvulling ingediend en wacht op een nieuwe beoordeling.",
            NotificationCategory.Parade, new NotificationAudience(Roles: [DefaultRoles.Optochtcommissie], UserIds: managers), "drammers://optocht"), cancellationToken);
        changeContext.Source = RegistrationSource.App;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        await audit.WriteAsync(new AuditEntry("parade-registration.supplemented", "ParadeRegistration", registration.Id.ToString(), null, null), cancellationToken);
    }

    /// <summary>
    /// Reserveert het volgende opgavenummer (ADR-011): atomaire UPDATE op de teller, binnen de transactie van de aanroeper;
    /// gelijktijdige inzendingen voor dezelfde optocht wachten op elkaar. Nummers worden nooit hergebruikt.
    /// </summary>
    public static async Task<int> NextNumberAsync(DrammersDbContext db, Guid paradeId, CancellationToken cancellationToken) =>
        (await db.Database.SqlQuery<int>($"""
            UPDATE parade.ParadeNumberSequence
               SET last_registration_number = last_registration_number + 1
            OUTPUT inserted.last_registration_number AS [Value]
             WHERE parade_id = {paradeId}
            """).ToListAsync(cancellationToken)).Single();

    public async Task<ParadeRegistration> WithdrawAsync(Guid userId, Guid id, string? reason, CancellationToken cancellationToken)
    {
        var registration = await TrackedAsync(userId, id, cancellationToken);
        var (_, canWithdraw) = await OwnerPolicyAsync(registration, cancellationToken);
        if (!canWithdraw)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition,
                registration.Status == RegistrationStatus.Draft ? "Een concept kun je verwijderen; intrekken kan na indienen." : "Intrekken kan in deze fase niet meer; neem contact op met de optochtcommissie.",
                DomainErrorKind.Conflict);
        }

        var from = registration.Status;
        registration.Status = RegistrationStatus.Withdrawn;
        registration.WithdrawnAt = Now;
        registration.StartNumber = null; // het opgavenummer blijft; het startnummer komt vrij (docs/13 §3)
        registration.ParadeOrder = null;
        db.ParadeStatusHistory.Add(new ParadeStatusHistory
        {
            RegistrationId = id,
            FromStatus = from,
            ToStatus = RegistrationStatus.Withdrawn,
            Reason = Clip(reason, 1000),
            ActorUserId = userId,
            OccurredAt = Now,
        });
        changeContext.Source = RegistrationSource.App;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.withdrawn", "ParadeRegistration", id.ToString()), cancellationToken);
        return registration;
    }

    // ----- Onthouden bouwlocaties ------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ParadeBuildLocation>> LocationsAsync(Guid userId, CancellationToken cancellationToken) =>
        await db.ParadeBuildLocations.AsNoTracking().Where(l => l.UserId == userId).OrderByDescending(l => l.LastUsedAt).ToListAsync(cancellationToken);

    public async Task DeleteLocationAsync(Guid userId, Guid locationId, CancellationToken cancellationToken)
    {
        if (await db.ParadeBuildLocations.Where(l => l.Id == locationId && l.UserId == userId).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            throw new DomainException(ErrorCodes.NotFound, "Locatie niet gevonden.", DomainErrorKind.NotFound);
        }
    }

    /// <summary>Het bouwadres onthouden voor volgend jaar; hetzelfde adres wordt niet dubbel opgeslagen.</summary>
    private async Task RememberLocationAsync(Guid userId, Address address, CancellationToken cancellationToken)
    {
        if (address.IsEmpty)
        {
            return;
        }

        var existing = (await db.ParadeBuildLocations.Where(l => l.UserId == userId).ToListAsync(cancellationToken))
            .FirstOrDefault(l => SameAddress(l.Address, address));
        if (existing is null)
        {
            db.ParadeBuildLocations.Add(new ParadeBuildLocation { Id = IdGenerator.NewId(), UserId = userId, Address = CloneAddress(address), CreatedAt = Now, LastUsedAt = Now });
        }
        else
        {
            existing.LastUsedAt = Now;
        }
    }

    // ----- Beheerders ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<(Guid UserId, string DisplayName, ManagerRole Role)>> ManagersAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await GetAsync(userId, id, cancellationToken);
        return [.. (await db.ParadeRegistrationManagers.AsNoTracking().Where(m => m.RegistrationId == id)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => new { m.UserId, u.DisplayName, m.Role })
            .OrderBy(m => m.Role).ThenBy(m => m.DisplayName)
            .ToListAsync(cancellationToken)).Select(m => (m.UserId, m.DisplayName, m.Role))];
    }

    /// <summary>Mede-beheerder toevoegen op e-mailadres; alleen een actief account (lid) en alleen door de eigenaar.</summary>
    public async Task AddManagerAsync(Guid userId, Guid id, string email, CancellationToken cancellationToken)
    {
        await EnsureOwnerAsync(userId, id, cancellationToken);
        var normalized = email.Trim().ToLowerInvariant();
        var other = await db.Users.Where(u => u.Email == normalized && u.AccountStatus == AccountStatus.Active).Select(u => u.Id).SingleOrDefaultAsync(cancellationToken);
        if (other == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.UserNotFound, "Er is geen app-account met dit e-mailadres. Een mede-beheerder moet zelf lid zijn met een account.", DomainErrorKind.NotFound);
        }

        var today = DateOnly.FromDateTime(Now);
        var leader = await db.Users.Where(u => u.Id == other)
            .SelectMany(u => u.Roles).Where(r => (r.ValidFrom == null || r.ValidFrom <= today) && (r.ValidTo == null || r.ValidTo >= today))
            .Join(db.Roles, r => r.RoleId, role => role.Id, (r, role) => role.Code)
            .AnyAsync(code => code == DefaultRoles.Groepsverantwoordelijke, cancellationToken);
        if (!leader)
        {
            throw new DomainException(ErrorCodes.Forbidden,
                "Deze persoon is (nog) geen groepsverantwoordelijke. Vraag het bestuur om dat in het portal aan te vinken.", DomainErrorKind.Forbidden);
        }

        if (!await db.ParadeRegistrationManagers.AnyAsync(m => m.RegistrationId == id && m.UserId == other, cancellationToken))
        {
            db.ParadeRegistrationManagers.Add(new ParadeRegistrationManager { RegistrationId = id, UserId = other, Role = ManagerRole.CoManager, AddedAt = Now });
            await db.SaveChangesAsync(cancellationToken);

            await audit.WriteAsync(new AuditEntry("parade-registration.manager-added", "ParadeRegistration", id.ToString(), null, $"{{\"userId\":\"{other}\"}}"), cancellationToken);
        }
    }

    public async Task RemoveManagerAsync(Guid userId, Guid id, Guid managerUserId, CancellationToken cancellationToken)
    {
        await EnsureOwnerAsync(userId, id, cancellationToken);
        var removed = await db.ParadeRegistrationManagers
            .Where(m => m.RegistrationId == id && m.UserId == managerUserId && m.Role == ManagerRole.CoManager)
            .ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
        {
            throw new DomainException(ErrorCodes.UserNotFound, "Deze mede-beheerder is niet gevonden (de eigenaar kun je niet verwijderen).", DomainErrorKind.NotFound);
        }

        await audit.WriteAsync(new AuditEntry("parade-registration.manager-removed", "ParadeRegistration", id.ToString(), null, $"{{\"userId\":\"{managerUserId}\"}}"), cancellationToken);
    }

    private async Task EnsureOwnerAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var role = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == id && m.UserId == userId).Select(m => (ManagerRole?)m.Role).SingleOrDefaultAsync(cancellationToken);
        if (role is null)
        {
            throw NotFound();
        }

        if (role != ManagerRole.Owner)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Alleen de eigenaar van de inschrijving beheert de mede-beheerders.", DomainErrorKind.Forbidden);
        }
    }

    // ----- Documenten ------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<ParadeDocument>> DocumentsAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        await GetAsync(userId, id, cancellationToken);
        return await db.ParadeDocuments.AsNoTracking().Where(d => d.RegistrationId == id).OrderBy(d => d.UploadedAt).ToListAsync(cancellationToken);
    }

    /// <summary>Upload via de pijplijn uit fase 5: type op inhoud, quarantaine, virusscan; daarna in de private container.</summary>
    public async Task<ParadeDocument> AddDocumentAsync(Guid userId, Guid id, UploadedDocument upload, CancellationToken cancellationToken)
    {
        var registration = await GetAsync(userId, id, cancellationToken);
        var (allowed, _) = await OwnerPolicyAsync(registration, cancellationToken);
        if (!allowed.Contains(RegistrationFields.Documents))
        {
            throw new DomainException(ErrorCodes.FieldNotEditable, "In deze fase kun je geen documenten meer toevoegen.");
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == registration.ParadeId, cancellationToken);
        if (await db.ParadeDocuments.CountAsync(d => d.RegistrationId == id, cancellationToken) >= parade.MaxDocumentsPerRegistration)
        {
            throw new DomainException(ErrorCodes.TooManyDocuments, $"Er kunnen maximaal {parade.MaxDocumentsPerRegistration} documenten bij een inschrijving.");
        }

        await using var stream = upload.Open();
        var (quarantinePath, kind) = await contentFiles.QuarantineAsync(
            stream, upload.Length, [FileKind.Pdf, FileKind.Jpeg, FileKind.Png], parade.MaxDocumentSizeMb * 1024L * 1024L, cancellationToken);
        await contentFiles.EnsureCleanAsync(quarantinePath, cancellationToken);
        var document = new ParadeDocument
        {
            Id = IdGenerator.NewId(),
            RegistrationId = id,
            DocumentType = upload.Type,
            FileName = SafeFileName(upload.FileName, kind),
            ContentType = FileTypeInspector.ContentType(kind),
            SizeBytes = upload.Length,
            BlobPath = $"{id:N}/{IdGenerator.NewId():N}{FileTypeInspector.Extension(kind)}",
            UploadedBy = userId,
            UploadedAt = Now,
        };
        await files.CopyAsync(FileContainers.Quarantine, quarantinePath, FileContainers.ParadeDocuments, document.BlobPath, cancellationToken);
        await files.DeleteAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);
        db.ParadeDocuments.Add(document);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.document-added", "ParadeRegistration", id.ToString(), null,
            JsonSerializer.Serialize(new { document.Id, type = document.DocumentType.ToString(), document.SizeBytes }, Json)), cancellationToken);
        return document;
    }

    public async Task DeleteDocumentAsync(Guid userId, Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var registration = await GetAsync(userId, id, cancellationToken);
        var (allowed, _) = await OwnerPolicyAsync(registration, cancellationToken);
        if (!allowed.Contains(RegistrationFields.Documents))
        {
            throw new DomainException(ErrorCodes.FieldNotEditable, "In deze fase kun je geen documenten meer verwijderen.");
        }

        var document = await db.ParadeDocuments.SingleOrDefaultAsync(d => d.Id == documentId && d.RegistrationId == id, cancellationToken) ?? throw NotFound();
        db.ParadeDocuments.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await files.DeleteAsync(FileContainers.ParadeDocuments, document.BlobPath, cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.document-deleted", "ParadeRegistration", id.ToString()), cancellationToken);
    }

    /// <summary>Korte leeslink (ADR-008: ≤ 15 minuten).</summary>
    public async Task<Uri> DocumentUrlAsync(Guid userId, Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        await GetAsync(userId, id, cancellationToken);
        var path = await db.ParadeDocuments.Where(d => d.Id == documentId && d.RegistrationId == id).Select(d => d.BlobPath).SingleOrDefaultAsync(cancellationToken)
            ?? throw NotFound();
        return await files.GetReadUriAsync(FileContainers.ParadeDocuments, path, cancellationToken);
    }

    // ----- Hulpfuncties ----------------------------------------------------------------------------------------------

    private async Task<ParadeCategory?> CategoryAsync(int? categoryId, Guid paradeId, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return null;
        }

        var category = (await parades.CategoriesForAsync(paradeId, activeOnly: false, cancellationToken)).SingleOrDefault(c => c.Id == id);
        return category is { Active: true }
            ? category
            : throw Invalid("Kies een categorie.", [new ValidationIssue(RegistrationFields.Category, "Deze categorie bestaat niet (meer).", IssueSeverity.Block)]);
    }

    private static void PhoneIssue(string? input, string? normalized, List<ValidationIssue> issues)
    {
        if (!string.IsNullOrWhiteSpace(input) && normalized is null)
        {
            issues.Add(new ValidationIssue(RegistrationFields.ContactPhone, "Vul een geldig telefoonnummer in, bijvoorbeeld 06 12345678.", IssueSeverity.Block));
        }
    }

    /// <summary>Invoer overnemen (genormaliseerd: telefoon E.164, postcode, e-mail in kleine letters).</summary>
    public static ParadeRegistration ApplyInput(ParadeRegistration r, RegistrationInput input) => Apply(r, input);

    private static ParadeRegistration Apply(ParadeRegistration r, RegistrationInput input)
    {
        r.GroupName = Clean(input.GroupName);
        r.ContactName = Clean(input.ContactName);
        r.ContactPhone = PhoneNormalizer.Normalize(input.ContactPhone);
        r.ContactEmail = Clean(input.ContactEmail)?.ToLowerInvariant();
        r.CategoryId = input.CategoryId;
        r.Subject = Clean(input.Subject);
        r.SubjectDescription = Clean(input.SubjectDescription);
        r.ChildrenCount = input.ChildrenCount;
        r.AdultCount = input.AdultCount;
        r.HasMusic = input.HasMusic;
        Copy(input.BuildAddress, r.BuildAddress);
        r.JuryInspectionSameAsBuildAddress = input.JuryInspectionSameAsBuildAddress;
        Copy(input.JuryInspectionSameAsBuildAddress ? null : input.JuryInspectionAddress, r.JuryInspectionAddress);
        r.EstimatedLengthMeters = input.EstimatedLengthMeters;
        r.AdditionalInformation = Clean(input.AdditionalInformation);
        return r;
    }

    private static void Copy(AddressInput? from, Address to)
    {
        to.Street = Clean(from?.Street);
        to.HouseNumber = Clean(from?.HouseNumber);
        to.Addition = Clean(from?.Addition);
        to.PostalCode = RegistrationRules.NormalizePostalCode(Clean(from?.PostalCode));
        to.City = Clean(from?.City);
        to.Country = string.IsNullOrWhiteSpace(from?.Country) ? "NL" : from.Country.Trim().ToUpperInvariant();
    }

    private static ParadeRegistration Clone(ParadeRegistration r) => new()
    {
        GroupName = r.GroupName,
        ContactName = r.ContactName,
        ContactPhone = r.ContactPhone,
        ContactEmail = r.ContactEmail,
        CategoryId = r.CategoryId,
        Subject = r.Subject,
        SubjectDescription = r.SubjectDescription,
        ChildrenCount = r.ChildrenCount,
        AdultCount = r.AdultCount,
        HasMusic = r.HasMusic,
        BuildAddress = CloneAddress(r.BuildAddress),
        JuryInspectionSameAsBuildAddress = r.JuryInspectionSameAsBuildAddress,
        JuryInspectionAddress = CloneAddress(r.JuryInspectionAddress),
        EstimatedLengthMeters = r.EstimatedLengthMeters,
        AdditionalInformation = r.AdditionalInformation,
        Status = r.Status,
    };

    private static Address CloneAddress(Address a) =>
        new() { Street = a.Street, HouseNumber = a.HouseNumber, Addition = a.Addition, PostalCode = a.PostalCode, City = a.City, Country = a.Country };

    private static List<string> ChangedFields(ParadeRegistration before, ParadeRegistration after)
    {
        var changed = new List<string>();
        void Check(bool different, string field)
        {
            if (different)
            {
                changed.Add(field);
            }
        }

        Check(before.GroupName != after.GroupName, RegistrationFields.GroupName);
        Check(before.ContactName != after.ContactName, RegistrationFields.ContactName);
        Check(before.ContactPhone != after.ContactPhone, RegistrationFields.ContactPhone);
        Check(before.ContactEmail != after.ContactEmail, RegistrationFields.ContactEmail);
        Check(before.CategoryId != after.CategoryId, RegistrationFields.Category);
        Check(before.Subject != after.Subject, RegistrationFields.Subject);
        Check(before.SubjectDescription != after.SubjectDescription, RegistrationFields.SubjectDescription);
        Check(before.ChildrenCount != after.ChildrenCount, RegistrationFields.ChildrenCount);
        Check(before.AdultCount != after.AdultCount, RegistrationFields.AdultCount);
        Check(before.HasMusic != after.HasMusic, RegistrationFields.HasMusic);
        Check(!SameAddress(before.BuildAddress, after.BuildAddress), RegistrationFields.BuildAddress);
        Check(before.JuryInspectionSameAsBuildAddress != after.JuryInspectionSameAsBuildAddress || !SameAddress(before.JuryInspectionAddress, after.JuryInspectionAddress),
            RegistrationFields.JuryInspection);
        Check(before.EstimatedLengthMeters != after.EstimatedLengthMeters, RegistrationFields.EstimatedLength);
        Check(before.AdditionalInformation != after.AdditionalInformation, RegistrationFields.AdditionalInformation);
        return changed;
    }

    private static bool SameAddress(Address a, Address b) =>
        a.Street == b.Street && a.HouseNumber == b.HouseNumber && a.Addition == b.Addition && a.PostalCode == b.PostalCode && a.City == b.City && a.Country == b.Country;

    private static string? Warnings(IReadOnlyList<ValidationIssue> issues)
    {
        var warnings = issues.Where(i => i.Severity == IssueSeverity.Warn).Select(i => new { i.Field, i.Message }).ToList();
        return warnings.Count == 0 ? null : JsonSerializer.Serialize(warnings, Json);
    }

    private static DomainException Invalid(string message, IEnumerable<ValidationIssue> issues) => new(ErrorCodes.RegistrationInvalid, message)
    {
        Issues = [.. issues.Select(i => new FieldIssue(i.Field, i.Message, i.Severity.ToString()))],
    };

    private static DomainException Changed() =>
        new(ErrorCodes.RegistrationChanged, "De inschrijving is intussen gewijzigd (bijvoorbeeld door een mede-beheerder). Laad haar opnieuw.", DomainErrorKind.PreconditionFailed);

    private static DomainException NotFound() => new(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);

    private static string SafeFileName(string fileName, FileKind kind)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var safe = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(100)]).Trim();
        return $"{(safe.Length == 0 ? "document" : safe)}{FileTypeInspector.Extension(kind)}";
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Clip(string? value, int max) => Clean(value) is { } v ? (v.Length <= max ? v : v[..max]) : null;
}
