using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record CategoryTotals(string Name, int Registrations, int Participants, decimal LengthMeters);

/// <summary>Totalen voor het overzicht: aantallen per status en per categorie, deelnemers en lengte van de optocht.</summary>
public sealed record LineupSummary(
    int Active, int Approved, int WithStartNumber, int Published, int Participants, decimal LineupLengthMeters,
    IReadOnlyDictionary<RegistrationStatus, int> PerStatus, IReadOnlyList<CategoryTotals> Categories);

public sealed record PublishResult(int Published, int WithoutStartNumber);

/// <summary>
/// Optochtbeheer (fase 12a): startnummers (ADR-012) handmatig toekennen en wisselen, de gemeten lengte vastleggen,
/// startnummers publiceren (→ <c>StartNumberAssigned</c>, push en e-mail) en de totalen van de optocht.
/// Een gepubliceerd startnummer dat wijzigt, meldt de app direct aan de groep.
/// </summary>
public sealed class ParadeLineup(
    DrammersDbContext db, INotificationService notifications, IOutbox outbox, IAuditLogger audit, ICurrentActor actor,
    ParadeChangeContext changeContext, IClock clock)
{
    /// <summary>Statussen waarin een groep in de optocht zit en dus een startnummer kan krijgen.</summary>
    public static readonly RegistrationStatus[] InLineup = [RegistrationStatus.Approved, RegistrationStatus.StartNumberAssigned, RegistrationStatus.Final];

    private DateTime Now => clock.UtcNow.UtcDateTime;

    private async Task<Parade> CurrentParadeAsync(CancellationToken cancellationToken) =>
        await db.CurrentParades().AsNoTracking().FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht voor het actieve carnavalsjaar.", DomainErrorKind.NotFound);

    public async Task<LineupSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var rows = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.ParadeId == parade.Id && r.Status != RegistrationStatus.Draft)
            .GroupJoin(db.ParadeCategories, r => r.CategoryId, c => c.Id, (r, cs) => new { r, cs })
            .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new
            {
                x.r.Status,
                Category = c == null ? null : c.Name,
                Participants = x.r.ChildrenCount + x.r.AdultCount,
                Length = x.r.MeasuredLengthMeters ?? x.r.EstimatedLengthMeters ?? 0m,
                Spacing = x.r.SpacingAfterMeters,
                x.r.StartNumber,
            })
            .ToListAsync(cancellationToken);
        var active = rows.Where(r => r.Status is not (RegistrationStatus.Rejected or RegistrationStatus.Withdrawn)).ToList();
        var lineup = active.Where(r => InLineup.Contains(r.Status)).ToList();
        return new LineupSummary(
            active.Count,
            lineup.Count,
            lineup.Count(r => r.StartNumber != null),
            lineup.Count(r => r.Status != RegistrationStatus.Approved),
            active.Sum(r => r.Participants),
            lineup.Sum(r => r.Length + (r.Spacing ?? parade.DefaultSpacingMeters)),
            rows.GroupBy(r => r.Status).ToDictionary(g => g.Key, g => g.Count()),
            [.. active.GroupBy(r => r.Category ?? "Zonder categorie").OrderBy(g => g.Key, StringComparer.CurrentCulture)
                .Select(g => new CategoryTotals(g.Key, g.Count(), g.Sum(r => r.Participants), g.Sum(r => r.Length)))]);
    }

    /// <summary>
    /// Startnummer toekennen (of leegmaken). Is het nummer al van een andere groep, dan 409 met diens naam, tenzij
    /// <paramref name="swap"/>: dan wisselen beide groepen van nummer in één databaseopdracht.
    /// </summary>
    public async Task SetStartNumberAsync(Guid id, int? startNumber, bool swap, CancellationToken cancellationToken)
    {
        var registration = await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.Id == id && r.Status != RegistrationStatus.Draft, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        if (registration.Status is not (RegistrationStatus.Approved or RegistrationStatus.StartNumberAssigned))
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition,
                registration.Status == RegistrationStatus.Final
                    ? "De optocht is vastgesteld; startnummers wijzigen kan alleen met het recht voor de definitieve optocht."
                    : "Alleen een goedgekeurde inschrijving krijgt een startnummer.",
                DomainErrorKind.Conflict);
        }

        if (startNumber is <= 0 or > 9999)
        {
            throw new DomainException(ErrorCodes.Validation, "Een startnummer is een getal van 1 tot en met 9999.");
        }

        if (registration.StartNumber == startNumber)
        {
            return;
        }

        await EnsureNotReservedAsync(registration.ParadeId, startNumber, cancellationToken);

        var other = startNumber is null
            ? null
            : await db.ParadeRegistrations.AsNoTracking().SingleOrDefaultAsync(r => r.ParadeId == registration.ParadeId && r.StartNumber == startNumber && r.Id != id, cancellationToken);
        if (other is not null && !swap)
        {
            throw new DomainException(ErrorCodes.StartNumberTaken,
                $"Startnummer {startNumber} is al toegekend aan {other.GroupName ?? "een andere groep"} (opgavenummer {other.RegistrationNumber}). Kies Wisselen om de nummers om te ruilen.",
                DomainErrorKind.Conflict);
        }

        List<(ParadeRegistration Registration, int? New)> changes = [(registration, startNumber)];
        if (other is not null)
        {
            changes.Add((other, registration.StartNumber));
        }

        await ApplyStartNumbersAsync(changes, cancellationToken);
        await audit.WriteAsync(new AuditEntry(other is null ? "parade-registration.start-number" : "parade-registration.start-number-swapped", "ParadeRegistration", id.ToString(),
            JsonSerializer.Serialize(new { startNumber = registration.StartNumber }),
            JsonSerializer.Serialize(new { startNumber, swappedWith = other?.Id })), cancellationToken);
    }

    /// <summary>Startnummers 1 … n zijn van de vaste plekken vooraan (fase 12c).</summary>
    internal async Task EnsureNotReservedAsync(Guid paradeId, int? startNumber, CancellationToken cancellationToken)
    {
        if (startNumber is not { } number)
        {
            return;
        }

        var parade = await db.Parades.AsNoTracking().SingleAsync(p => p.Id == paradeId, cancellationToken);
        if (number < parade.FirstGroupStartNumber)
        {
            throw new DomainException(ErrorCodes.Validation,
                $"Startnummer {number} is van de vaste plek \"{parade.FixedEntries[number - 1].Name}\". Groepen beginnen bij {parade.FirstGroupStartNumber}.");
        }
    }

    /// <summary>
    /// Zet startnummers in één UPDATE (zodat wisselen de unieke index niet raakt) en legt de veldhistorie vast.
    /// Groepen met een gepubliceerd nummer krijgen direct een melding van het nieuwe nummer.
    /// </summary>
    internal async Task ApplyStartNumbersAsync(IReadOnlyList<(ParadeRegistration Registration, int? New)> changes, CancellationToken cancellationToken)
    {
        var now = Now;
        var correlation = Guid.NewGuid();
        var values = string.Join(", ", changes.Select((_, i) => $"({{{i * 2}}}, {{{(i * 2) + 1}}})"));
        var parameters = changes.SelectMany(c => new object[] { c.Registration.Id, c.New is { } n ? n : DBNull.Value }).ToArray();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
#pragma warning disable EF1002 // Alleen parameterplaatsen worden samengesteld; de waarden gaan als parameters mee.
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE r SET r.start_number = v.n FROM parade.ParadeRegistration r JOIN (VALUES {values}) AS v(id, n) ON r.id = v.id", parameters, cancellationToken);
#pragma warning restore EF1002
        foreach (var (registration, next) in changes)
        {
            db.ParadeRegistrationHistory.Add(new ParadeRegistrationHistory
            {
                RegistrationId = registration.Id,
                FieldName = nameof(ParadeRegistration.StartNumber),
                FieldLabel = ParadeHistoryInterceptor.Label(nameof(ParadeRegistration.StartNumber)),
                OldValue = registration.StartNumber?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                NewValue = next?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ChangedByUserId = actor.UserId,
                ChangedAt = now,
                ChangeSource = RegistrationSource.Portal,
                CorrelationId = correlation,
            });
            if (registration.Status == RegistrationStatus.StartNumberAssigned && next is not null)
            {
                await NotifyStartNumberAsync(registration, next.Value, changed: true, cancellationToken);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SetMeasuredLengthAsync(Guid id, decimal? meters, CancellationToken cancellationToken)
    {
        if (meters is { } m && (m <= 0 || m > RegistrationRules.MaxLengthMeters || decimal.Round(m, 1) != m))
        {
            throw new DomainException(ErrorCodes.Validation, $"De gemeten lengte is groter dan 0 en hoogstens {RegistrationRules.MaxLengthMeters} meter, met maximaal 1 decimaal.");
        }

        var registration = await db.ParadeRegistrations.SingleOrDefaultAsync(r => r.Id == id && r.Status != RegistrationStatus.Draft, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Inschrijving niet gevonden.", DomainErrorKind.NotFound);
        var before = registration.MeasuredLengthMeters;
        registration.MeasuredLengthMeters = meters;
        changeContext.Source = RegistrationSource.Portal;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade-registration.measured-length", "ParadeRegistration", id.ToString(),
            JsonSerializer.Serialize(new { measuredLengthMeters = before }), JsonSerializer.Serialize(new { measuredLengthMeters = meters })), cancellationToken);
    }

    /// <summary>
    /// Publiceert alle toegekende startnummers: goedgekeurde inschrijvingen mét startnummer worden
    /// <c>StartNumberAssigned</c> en de groep krijgt een push en e-mail "Jullie startnummer is …".
    /// </summary>
    public async Task<PublishResult> PublishStartNumbersAsync(Guid actorId, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var approved = await db.ParadeRegistrations.Where(r => r.ParadeId == parade.Id && r.Status == RegistrationStatus.Approved).ToListAsync(cancellationToken);
        var publish = approved.Where(r => r.StartNumber != null).ToList();
        foreach (var registration in publish)
        {
            registration.Status = RegistrationStatus.StartNumberAssigned;
            db.ParadeStatusHistory.Add(new ParadeStatusHistory
            {
                RegistrationId = registration.Id,
                FromStatus = RegistrationStatus.Approved,
                ToStatus = RegistrationStatus.StartNumberAssigned,
                ActorUserId = actorId,
                OccurredAt = Now,
            });
            await NotifyStartNumberAsync(registration, registration.StartNumber!.Value, changed: false, cancellationToken);
        }

        changeContext.Source = RegistrationSource.Portal;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.start-numbers-published", "Parade", parade.Id.ToString(), null,
            JsonSerializer.Serialize(new { published = publish.Count, withoutStartNumber = approved.Count - publish.Count })), cancellationToken);
        return new PublishResult(publish.Count, approved.Count - publish.Count);
    }

    private async Task NotifyStartNumberAsync(ParadeRegistration registration, int startNumber, bool changed, CancellationToken cancellationToken)
    {
        var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == registration.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
        if (managers.Count > 0)
        {
            await notifications.EnqueueAsync(new SystemNotification(
                changed ? "Startnummer optocht gewijzigd" : "Startnummer optocht bekend",
                changed ? $"{registration.GroupName} heeft nu startnummer {startNumber}." : $"Jullie startnummer is {startNumber} ({registration.GroupName}).",
                NotificationCategory.Parade, new NotificationAudience(UserIds: managers), "drammers://optocht"), cancellationToken);
        }

        outbox.Enqueue(ParadeReview.StatusMailMessageType, new ParadeReview.StatusMail(registration.Id, RegistrationStatus.StartNumberAssigned, changed ? "gewijzigd" : null));
    }
}
