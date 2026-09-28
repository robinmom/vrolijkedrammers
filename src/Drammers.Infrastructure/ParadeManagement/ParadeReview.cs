using System.Net;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public enum ReviewAction
{
    StartReview,
    Approve,
    Reject,
    RequestInformation,
    Reopen,
}

public sealed record ReviewSummary(
    Guid Id, int? RegistrationNumber, int? StartNumber, string? GroupName, string? CategoryName, bool Youth, bool HasVehicle,
    RegistrationStatus Status, RegistrationSource Source, string? ContactName, string? ContactPhone, string? ContactEmail, string? Subject,
    int ChildrenCount, int AdultCount, decimal? EstimatedLengthMeters, decimal? MeasuredLengthMeters, bool JuryElsewhere,
    string? AdditionalInformation, DateTime? SubmittedAt, DateTime? UpdatedAt, bool HasWarnings, bool SupplementReceived);

/// <summary>Ontbrekende of afwijkende gegevens (filter in het overzicht, docs/13 §7.1).</summary>
public enum MissingData
{
    StartNumber,
    MeasuredLength,
    Warnings,
    JuryElsewhere,
    Documents,
}

public enum RegistrationSort
{
    RegistrationNumber,
    StartNumber,
    GroupName,
    Category,
    Participants,
    Length,
    Status,
    SubmittedAt,
}

/// <summary>Filters en sortering van het overzicht voor de commissie.</summary>
public sealed record RegistrationFilter(
    RegistrationStatus? Status = null, string? Search = null, int? CategoryId = null, AgeGroup? AgeGroup = null, bool? HasVehicle = null,
    MissingData? Missing = null, RegistrationSort Sort = RegistrationSort.RegistrationNumber, bool Descending = false);

/// <summary>
/// Beoordeling door de Optochtcommissie (fase 11, <c>parade.manage</c>): elke inschrijving wordt in behandeling genomen en
/// pas na goedkeuring definitief. Afwijzen en om aanvulling vragen gaan met een reden; de groep krijgt bij elke stap
/// een melding (push aan de beheerders, e-mail aan het contactadres).
/// </summary>
public sealed class ParadeReview(
    DrammersDbContext db, IFileStore files, INotificationService notifications, IOutbox outbox, IAuditLogger audit, ParadeChangeContext changeContext, IClock clock)
{
    public const string StatusMailMessageType = "parade.status-mail";

    /// <summary>Reden in de statushistorie wanneer de groep de gevraagde aanvulling indient (weer "In behandeling").</summary>
    public const string SupplementReason = "Aanvulling ingediend door de groep – opnieuw beoordelen";

    public sealed record StatusMail(Guid RegistrationId, RegistrationStatus Status, string? Reason);

    private static readonly Dictionary<ReviewAction, (RegistrationStatus[] From, RegistrationStatus To, bool ReasonRequired)> Transitions = new()
    {
        [ReviewAction.StartReview] = ([RegistrationStatus.Submitted, RegistrationStatus.AdditionalInformationRequired], RegistrationStatus.UnderReview, false),
        [ReviewAction.Approve] = ([RegistrationStatus.Submitted, RegistrationStatus.UnderReview], RegistrationStatus.Approved, false),
        [ReviewAction.Reject] = ([RegistrationStatus.Submitted, RegistrationStatus.UnderReview, RegistrationStatus.AdditionalInformationRequired], RegistrationStatus.Rejected, true),
        [ReviewAction.RequestInformation] = ([RegistrationStatus.Submitted, RegistrationStatus.UnderReview], RegistrationStatus.AdditionalInformationRequired, true),
        [ReviewAction.Reopen] = ([RegistrationStatus.Rejected, RegistrationStatus.Withdrawn, RegistrationStatus.Approved], RegistrationStatus.UnderReview, true),
    };

    public static IReadOnlyList<ReviewAction> AllowedActions(RegistrationStatus status) =>
        [.. Transitions.Where(t => t.Value.From.Contains(status)).Select(t => t.Key)];

    public async Task<(IReadOnlyList<ReviewSummary> Items, int Total)> SearchAsync(
        RegistrationFilter filter, int page, int pageSize, CancellationToken cancellationToken)
    {
        var parade = await db.Parades.AsNoTracking().Where(p => db.CarnivalYears.Any(y => y.Id == p.CarnivalYearId && y.Active)).Select(p => (Guid?)p.Id).SingleOrDefaultAsync(cancellationToken);
        var query =
            from r in db.ParadeRegistrations.AsNoTracking()
            where r.ParadeId == parade && r.Status != RegistrationStatus.Draft
            join c in db.ParadeCategories on r.CategoryId equals c.Id into cs
            from c in cs.DefaultIfEmpty()
            select new { r, c };
        if (filter.Status is { } s)
        {
            query = query.Where(x => x.r.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var term = filter.Search.Trim();
            query = int.TryParse(term, out var number)
                ? query.Where(x => x.r.RegistrationNumber == number || x.r.StartNumber == number)
                : query.Where(x => x.r.GroupName!.Contains(term) || x.r.ContactName!.Contains(term) || x.r.Subject!.Contains(term) || x.r.ContactEmail!.Contains(term));
        }

        if (filter.CategoryId is { } category)
        {
            query = query.Where(x => x.r.CategoryId == category);
        }

        if (filter.AgeGroup is { } age)
        {
            query = query.Where(x => x.c != null && x.c.AgeGroup == age);
        }

        if (filter.HasVehicle is { } vehicle)
        {
            query = query.Where(x => x.c != null && x.c.HasVehicle == vehicle);
        }

        query = filter.Missing switch
        {
            MissingData.StartNumber => query.Where(x => x.r.StartNumber == null),
            MissingData.MeasuredLength => query.Where(x => x.r.MeasuredLengthMeters == null),
            MissingData.Warnings => query.Where(x => x.r.ValidationWarnings != null),
            MissingData.JuryElsewhere => query.Where(x => !x.r.JuryInspectionSameAsBuildAddress),
            MissingData.Documents => query.Where(x => !db.ParadeDocuments.Any(d => d.RegistrationId == x.r.Id)),
            _ => query,
        };

        var total = await query.CountAsync(cancellationToken);
        var sorted = (filter.Sort, filter.Descending) switch
        {
            (RegistrationSort.StartNumber, false) => query.OrderBy(x => x.r.StartNumber == null).ThenBy(x => x.r.StartNumber),
            (RegistrationSort.StartNumber, true) => query.OrderBy(x => x.r.StartNumber == null).ThenByDescending(x => x.r.StartNumber),
            (RegistrationSort.GroupName, false) => query.OrderBy(x => x.r.GroupName),
            (RegistrationSort.GroupName, true) => query.OrderByDescending(x => x.r.GroupName),
            (RegistrationSort.Category, false) => query.OrderBy(x => x.c!.SortOrder).ThenBy(x => x.r.RegistrationNumber),
            (RegistrationSort.Category, true) => query.OrderByDescending(x => x.c!.SortOrder).ThenBy(x => x.r.RegistrationNumber),
            (RegistrationSort.Participants, false) => query.OrderBy(x => x.r.ChildrenCount + x.r.AdultCount),
            (RegistrationSort.Participants, true) => query.OrderByDescending(x => x.r.ChildrenCount + x.r.AdultCount),
            (RegistrationSort.Length, false) => query.OrderBy(x => x.r.MeasuredLengthMeters ?? x.r.EstimatedLengthMeters),
            (RegistrationSort.Length, true) => query.OrderByDescending(x => x.r.MeasuredLengthMeters ?? x.r.EstimatedLengthMeters),
            (RegistrationSort.Status, false) => query.OrderBy(x => x.r.Status).ThenBy(x => x.r.RegistrationNumber),
            (RegistrationSort.Status, true) => query.OrderByDescending(x => x.r.Status).ThenBy(x => x.r.RegistrationNumber),
            (RegistrationSort.SubmittedAt, false) => query.OrderBy(x => x.r.SubmittedAt),
            (RegistrationSort.SubmittedAt, true) => query.OrderByDescending(x => x.r.SubmittedAt),
            (_, true) => query.OrderByDescending(x => x.r.RegistrationNumber),
            _ => query.OrderBy(x => x.r.RegistrationNumber),
        };
        var rows = await sorted.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new
            {
                x.r,
                Category = x.c == null ? null : x.c.Name,
                Youth = x.c != null && x.c.AgeGroup == AgeGroup.Youth,
                HasVehicle = x.c != null && x.c.HasVehicle,
                Supplement = x.r.Status == RegistrationStatus.UnderReview && db.ParadeStatusHistory.Where(h => h.RegistrationId == x.r.Id)
                    .OrderByDescending(h => h.OccurredAt).ThenByDescending(h => h.Id).Select(h => h.Reason).FirstOrDefault() == SupplementReason,
            })
            .ToListAsync(cancellationToken);
        var items = rows.Select(x => new ReviewSummary(
            x.r.Id, x.r.RegistrationNumber, x.r.StartNumber, x.r.GroupName, x.Category, x.Youth, x.HasVehicle, x.r.Status, x.r.Source,
            x.r.ContactName, x.r.ContactPhone is null ? null : PhoneNormalizer.Display(x.r.ContactPhone), x.r.ContactEmail, x.r.Subject,
            x.r.ChildrenCount, x.r.AdultCount, x.r.EstimatedLengthMeters, x.r.MeasuredLengthMeters, !x.r.JuryInspectionSameAsBuildAddress,
            x.r.AdditionalInformation, x.r.SubmittedAt, x.r.UpdatedAt, x.r.ValidationWarnings != null, x.Supplement)).ToList();
        return (items, total);
    }

    public async Task<ParadeRegistration> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.Status != RegistrationStatus.Draft, cancellationToken)
        ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);

    public async Task<IReadOnlyList<ParadeStatusHistory>> StatusHistoryAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ParadeStatusHistory.AsNoTracking().Where(h => h.RegistrationId == id).OrderBy(h => h.OccurredAt).ThenBy(h => h.Id).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ParadeRegistrationHistory>> FieldHistoryAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ParadeRegistrationHistory.AsNoTracking().Where(h => h.RegistrationId == id).OrderByDescending(h => h.ChangedAt).Take(200).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ParadeDocument>> DocumentsAsync(Guid id, CancellationToken cancellationToken) =>
        await db.ParadeDocuments.AsNoTracking().Where(d => d.RegistrationId == id).OrderBy(d => d.UploadedAt).ToListAsync(cancellationToken);

    public async Task<Uri> DocumentUrlAsync(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        var path = await db.ParadeDocuments.Where(d => d.Id == documentId && d.RegistrationId == id).Select(d => d.BlobPath).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Document niet gevonden.", DomainErrorKind.NotFound);
        await audit.WriteAsync(new AuditEntry("parade-registration.document-downloaded", "ParadeRegistration", id.ToString(), null, $"{{\"documentId\":\"{documentId}\"}}"), cancellationToken);
        return await files.GetReadUriAsync(FileContainers.ParadeDocuments, path, cancellationToken);
    }

    public async Task<ParadeRegistration> TransitionAsync(Guid id, ReviewAction action, string? reason, Guid actorId, CancellationToken cancellationToken)
    {
        var registration = await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == id && r.Status != RegistrationStatus.Draft, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        var (from, to, reasonRequired) = Transitions[action];
        if (!from.Contains(registration.Status))
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Deze stap kan niet vanuit de huidige status.", DomainErrorKind.Conflict);
        }

        var cleanReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (reasonRequired && cleanReason is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een reden in; de groep krijgt die te zien.");
        }

        var now = clock.UtcNow.UtcDateTime;
        var previous = registration.Status;
        registration.Status = to;
        if (to == RegistrationStatus.Withdrawn || to == RegistrationStatus.Rejected)
        {
            registration.StartNumber = null;
        }

        // Wie de optocht (goedgekeurd) verlaat, gaat ook uit de volgorde van het samenstellen.
        if (!ParadeLineup.InLineup.Contains(to))
        {
            registration.ParadeOrder = null;
        }

        db.ParadeStatusHistory.Add(new ParadeStatusHistory
        {
            RegistrationId = id,
            FromStatus = previous,
            ToStatus = to,
            Reason = cleanReason is { Length: > 1000 } ? cleanReason[..1000] : cleanReason,
            ActorUserId = actorId,
            OccurredAt = now,
        });

        // De groep informeren; "in behandeling nemen" alleen per e-mail aan niet-leden niet nodig: wel een korte push.
        var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == id).Select(m => m.UserId).ToListAsync(cancellationToken);
        if (managers.Count > 0)
        {
            var (title, body) = PushText(registration, to, cleanReason);
            await notifications.EnqueueAsync(new SystemNotification(title, body, NotificationCategory.Parade, new NotificationAudience(UserIds: managers), "drammers://optocht"), cancellationToken);
        }

        outbox.Enqueue(StatusMailMessageType, new StatusMail(id, to, cleanReason));
        changeContext.Source = RegistrationSource.Portal;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry($"parade-registration.{action.ToString().ToLowerInvariant()}", "ParadeRegistration", id.ToString(),
            JsonSerializer.Serialize(new { status = previous.ToString() }), JsonSerializer.Serialize(new { status = to.ToString(), reason = cleanReason })), cancellationToken);
        return registration;
    }

    public static (string Title, string Body) PushText(ParadeRegistration r, RegistrationStatus status, string? reason) => status switch
    {
        RegistrationStatus.UnderReview => ("Inschrijving optocht in behandeling", $"De optochtcommissie bekijkt de inschrijving van {r.GroupName}."),
        RegistrationStatus.Approved => ("Inschrijving optocht goedgekeurd", $"{r.GroupName} doet mee! Het startnummer volgt na de indeling."),
        RegistrationStatus.Rejected => ("Inschrijving optocht afgewezen", $"De inschrijving van {r.GroupName} is afgewezen. Bekijk de reden in de app."),
        RegistrationStatus.AdditionalInformationRequired => ("Aanvulling nodig voor de optocht", $"De optochtcommissie vraagt om een aanvulling op de inschrijving van {r.GroupName}."),
        _ => ("Inschrijving optocht gewijzigd", $"De status van de inschrijving van {r.GroupName} is gewijzigd."),
    };
}

/// <summary>E-mail aan het contactadres bij elke statuswijziging door de commissie (ook voor niet-leden zonder app).</summary>
public sealed class ParadeStatusMailHandler(DrammersDbContext db, IEmailSender email) : IOutboxMessageHandler
{
    public string Type => ParadeReview.StatusMailMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var mail = JsonSerializer.Deserialize<ParadeReview.StatusMail>(message.Payload, JsonSerializerOptions.Web)!;
        var r = await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == mail.RegistrationId, cancellationToken);
        if (r?.ContactEmail is null)
        {
            return;
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == r.ParadeId, cancellationToken);
        var (subject, text) = mail.Status switch
        {
            RegistrationStatus.UnderReview => ($"Inschrijving {parade.Name} in behandeling", $"De optochtcommissie bekijkt jullie inschrijving (opgavenummer {r.RegistrationNumber}). Je hoort van ons zodra ze is beoordeeld."),
            RegistrationStatus.Approved => ($"Inschrijving {parade.Name} goedgekeurd", $"Goed nieuws: {r.GroupName} is goedgekeurd voor de {parade.Name}. De inschrijving is nu definitief. Het startnummer en de aanrijtijd volgen na de indeling."),
            RegistrationStatus.Rejected => ($"Inschrijving {parade.Name} afgewezen", $"Helaas is de inschrijving van {r.GroupName} afgewezen.\n\nReden: {mail.Reason}\n\nVragen? Neem contact op met de optochtcommissie."),
            RegistrationStatus.StartNumberAssigned => (
                mail.Reason == "gewijzigd" ? $"Startnummer {parade.Name} gewijzigd: {r.StartNumber}" : $"Startnummer {parade.Name}: {r.StartNumber}",
                $"Het startnummer van {r.GroupName} voor de {parade.Name} is {r.StartNumber}.{(mail.Reason == "gewijzigd" ? " Dit vervangt het eerder doorgegeven nummer." : "")}\n\nDe aanrijtijd en de opstelplaats volgen nog."),
            RegistrationStatus.AdditionalInformationRequired => ($"Aanvulling nodig: inschrijving {parade.Name}", $"De optochtcommissie heeft een aanvulling nodig op de inschrijving van {r.GroupName}:\n\n{mail.Reason}\n\n{(r.Source == RegistrationSource.WebForm ? "Neem hiervoor contact op met de optochtcommissie." : "Vul de aanvulling in de app in (Optocht → jullie inschrijving) en dien hem opnieuw in; daarna beoordeelt de commissie de inschrijving opnieuw.")}"),
            _ => ($"Inschrijving {parade.Name} gewijzigd", $"De status van de inschrijving van {r.GroupName} is gewijzigd."),
        };
        var plain = $"Beste {r.ContactName},\n\n{text}\n\nGroeten,\nDe Vrolijke Drammers";
        var html = $"<p>Beste {WebUtility.HtmlEncode(r.ContactName)},</p><p>{WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal)}</p><p>Groeten,<br>De Vrolijke Drammers</p>";
        await email.SendAsync(new EmailMessage(r.ContactEmail, subject, plain, html), cancellationToken);
    }
}
