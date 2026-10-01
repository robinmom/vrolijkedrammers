using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

public sealed record ArrivalRow(Guid Id, int? StartNumber, int? RegistrationNumber, string? Category, string? GroupName, TimeOnly? ArrivalTime);

public sealed record ArrivalList(
    string? Location, DateTime? PublishedAt, DateOnly ParadeDate, TimeOnly ParadeStartTime, int Version, IReadOnlyList<ArrivalRow> Rows);

/// <summary>De openbare aanrijtijdenlijst (zoals op de website): alleen na publiceren.</summary>
public sealed record PublicArrivalRow(int? StartNumber, string? Category, string? GroupName, string? ArrivalTime);

public sealed record PublicArrivals(bool Published, string? ParadeName, DateOnly? ParadeDate, string? Location, IReadOnlyList<PublicArrivalRow> Rows);

/// <summary>Een regel uit een geüpload bestand: Stnr. en de tijd (ruwe celwaarden).</summary>
public sealed record ArrivalImportRow(int Row, string? StartNumber, string? Time, string? GroupName = null);

public sealed record ArrivalChange(Guid Id, int StartNumber, string? GroupName, TimeOnly? OldTime, TimeOnly? NewTime);

public sealed record ArrivalImportPreview(int Version, string? Location, IReadOnlyList<ArrivalChange> Changes, IReadOnlyList<ImportIssue> Errors);

/// <summary>
/// Aanrijtijden (fase 16): alleen wagens (categorieën met een voertuig) krijgen een tijd bij de meldplek van de optocht.
/// Genereren op startnummervolgorde (eerste tijd + minuten per wagen), per groep aanpassen of een Excel in het formaat
/// van de websitetabel inlezen (Stnr. + tijd). Na publiceren zien groepen hun tijd in de app (met een melding) en is de
/// lijst openbaar; een latere wijziging wordt de groep direct gemeld.
/// </summary>
public sealed class ParadeArrivals(
    DrammersDbContext db, INotificationService notifications, IAuditLogger audit, ParadeChangeContext changeContext, IClock clock)
{
    private async Task<Parade> CurrentParadeAsync(CancellationToken cancellationToken) =>
        await db.CurrentParades().FirstOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht voor het actieve carnavalsjaar.", DomainErrorKind.NotFound);

    /// <summary>Goedgekeurde wagens in de optocht, op startnummer (zonder nummer achteraan).</summary>
    private async Task<List<(ParadeRegistration Registration, string? Category)>> VehiclesAsync(Guid paradeId, bool tracked, CancellationToken cancellationToken)
    {
        var query = db.ParadeRegistrations.Where(r => r.ParadeId == paradeId && ParadeLineup.InLineup.Contains(r.Status));
        if (!tracked)
        {
            query = query.AsNoTracking();
        }

        var rows = await query
            .Join(db.ParadeCategories.Where(c => c.HasVehicle), r => r.CategoryId, c => (int?)c.Id, (r, c) => new { r, c.Name })
            .ToListAsync(cancellationToken);
        return [.. rows.OrderBy(x => x.r.StartNumber ?? int.MaxValue).ThenBy(x => x.r.RegistrationNumber).Select(x => (x.r, (string?)x.Name))];
    }

    public async Task<ArrivalList> ListAsync(CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var rows = await VehiclesAsync(parade.Id, tracked: false, cancellationToken);
        return new ArrivalList(parade.ArrivalLocation, parade.ArrivalTimesPublishedAt, parade.ParadeDate, parade.StartTime, parade.CompositionVersion,
            [.. rows.Select(x => new ArrivalRow(x.Registration.Id, x.Registration.StartNumber, x.Registration.RegistrationNumber, x.Category,
                x.Registration.GroupName, x.Registration.ArrivalTime))]);
    }

    public async Task SetLocationAsync(string? location, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var clean = location?.Trim();
        parade.ArrivalLocation = string.IsNullOrEmpty(clean) ? null : clean.Length <= 100 ? clean : throw Invalid("De meldplek is hooguit 100 tekens.");
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.arrival-location", "Parade", parade.Id.ToString(), null, JsonSerializer.Serialize(new { location = clean })), cancellationToken);
    }

    /// <summary>
    /// Vult de aanrijtijden van alle wagens met een startnummer: <paramref name="first"/>, daarna steeds
    /// <paramref name="intervalMinutes"/> later, op startnummervolgorde. Met <paramref name="onlyEmpty"/> blijven ingevulde tijden staan.
    /// </summary>
    public async Task<int> GenerateAsync(TimeOnly first, int intervalMinutes, bool onlyEmpty, CancellationToken cancellationToken)
    {
        if (intervalMinutes is < 1 or > 60)
        {
            throw Invalid("Kies 1 tot en met 60 minuten per wagen.");
        }

        var parade = await CurrentParadeAsync(cancellationToken);
        var vehicles = (await VehiclesAsync(parade.Id, tracked: true, cancellationToken)).Where(x => x.Registration.StartNumber != null).ToList();
        if (vehicles.Count == 0)
        {
            throw Invalid("Er zijn nog geen wagens met een startnummer. Ken eerst startnummers toe.");
        }

        var changes = new List<(ParadeRegistration Registration, TimeOnly? Old)>();
        for (var i = 0; i < vehicles.Count; i++)
        {
            var registration = vehicles[i].Registration;
            var time = first.AddMinutes(i * intervalMinutes, out var wrapped);
            if (wrapped > 0)
            {
                throw Invalid("De tijden lopen over middernacht; kies een eerdere begintijd of minder minuten per wagen.");
            }

            if ((onlyEmpty && registration.ArrivalTime != null) || registration.ArrivalTime == time)
            {
                continue;
            }

            changes.Add((registration, registration.ArrivalTime));
            registration.ArrivalTime = time;
        }

        await SaveAsync(parade, changes, "parade.arrival-times-generated",
            new { first = first.ToString("HH:mm", CultureInfo.InvariantCulture), intervalMinutes, onlyEmpty }, cancellationToken);
        return changes.Count;
    }

    public async Task SetAsync(Guid registrationId, TimeOnly? time, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var vehicle = (await VehiclesAsync(parade.Id, tracked: true, cancellationToken)).FirstOrDefault(x => x.Registration.Id == registrationId).Registration
            ?? throw new DomainException(ErrorCodes.RegistrationNotFound, "Alleen goedgekeurde wagens in de optocht krijgen een aanrijtijd.", DomainErrorKind.NotFound);
        if (vehicle.ArrivalTime == time)
        {
            return;
        }

        var old = vehicle.ArrivalTime;
        vehicle.ArrivalTime = time;
        await SaveAsync(parade, [(vehicle, old)], "parade-registration.arrival-time", new { registrationId, time }, cancellationToken);
    }

    /// <summary>Publiceert de aanrijtijden: elke wagen met een tijd krijgt een melding; daarna is de lijst openbaar.</summary>
    public async Task<int> PublishAsync(CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var vehicles = (await VehiclesAsync(parade.Id, tracked: false, cancellationToken)).Where(x => x.Registration.ArrivalTime != null).ToList();
        if (vehicles.Count == 0)
        {
            throw Invalid("Er zijn nog geen aanrijtijden om te publiceren.");
        }

        parade.ArrivalTimesPublishedAt = clock.UtcNow.UtcDateTime;
        foreach (var (registration, _) in vehicles)
        {
            await NotifyAsync(parade, registration, changed: false, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("parade.arrival-times-published", "Parade", parade.Id.ToString(), null,
            JsonSerializer.Serialize(new { published = vehicles.Count })), cancellationToken);
        return vehicles.Count;
    }

    // ----- Import (tabel van de website: Stnr., Categorie, Naam, <meldplek>) ------------------------------------

    public async Task<ArrivalImportPreview> PreviewImportAsync(IReadOnlyList<ArrivalImportRow> rows, string? location, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var (changes, errors) = await PlanImportAsync(parade, rows, cancellationToken);
        return new ArrivalImportPreview(parade.CompositionVersion, location ?? parade.ArrivalLocation,
            [.. changes.Select(c => new ArrivalChange(c.Registration.Id, c.Registration.StartNumber!.Value, c.Registration.GroupName, c.Registration.ArrivalTime, c.Time))],
            errors);
    }

    public async Task<int> ImportAsync(IReadOnlyList<ArrivalImportRow> rows, string? location, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var (changes, errors) = await PlanImportAsync(parade, rows, cancellationToken);
        if (errors.Count > 0)
        {
            throw Invalid($"Het bestand bevat {errors.Count} fout(en); er is niets ingelezen.");
        }

        var tracked = (await VehiclesAsync(parade.Id, tracked: true, cancellationToken)).ToDictionary(x => x.Registration.Id, x => x.Registration);
        var applied = changes.Select(c => (tracked[c.Registration.Id], c.Registration.ArrivalTime, c.Time)).ToList();
        foreach (var (registration, _, time) in applied)
        {
            registration.ArrivalTime = time;
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            parade.ArrivalLocation = location.Trim().Length <= 100 ? location.Trim() : parade.ArrivalLocation;
        }

        await SaveAsync(parade, [.. applied.Select(a => (a.Item1, a.Item2))], "parade.arrival-times-imported", new { changes = applied.Count, location }, cancellationToken);
        return applied.Count;
    }

    private async Task<(List<(ParadeRegistration Registration, TimeOnly? Time)> Changes, List<ImportIssue> Errors)> PlanImportAsync(
        Parade parade, IReadOnlyList<ArrivalImportRow> rows, CancellationToken cancellationToken)
    {
        var vehicles = (await VehiclesAsync(parade.Id, tracked: false, cancellationToken)).Where(x => x.Registration.StartNumber != null)
            .ToDictionary(x => x.Registration.StartNumber!.Value, x => x.Registration);
        var errors = new List<ImportIssue>();
        var changes = new List<(ParadeRegistration, TimeOnly?)>();
        var seen = new HashSet<int>();
        var byNumber = (await db.ParadeRegistrations.AsNoTracking()
                .Where(r => r.ParadeId == parade.Id && r.StartNumber != null && r.Status != RegistrationStatus.Draft)
                .GroupJoin(db.ParadeCategories, r => r.CategoryId, c => (int?)c.Id, (r, cs) => new { r, cs })
                .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new { x.r.StartNumber, x.r.GroupName, x.r.Status, HasVehicle = c != null && c.HasVehicle })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.StartNumber!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var row in rows)
        {
            ImportIssue Issue(string message, string advice) => new(row.Row, message, advice, null, row.StartNumber, row.GroupName);

            if (string.IsNullOrWhiteSpace(row.StartNumber))
            {
                continue;
            }

            if (WholeNumber(row.StartNumber) is not { } number)
            {
                errors.Add(Issue($"In de kolom Stnr. staat \"{row.StartNumber}\"; dat is geen startnummer.", "Zet in Stnr. alleen het startnummer, of maak de cel leeg."));
                continue;
            }

            if (number < parade.FirstGroupStartNumber)
            {
                // Vaste plekken vooraan (geluidswagen e.d.) krijgen geen aanrijtijd via de groepen.
                continue;
            }

            if (!seen.Add(number))
            {
                errors.Add(Issue($"Startnummer {number} staat twee keer in het bestand.", "Laat elk startnummer maar één keer voorkomen."));
                continue;
            }

            if (!vehicles.TryGetValue(number, out var registration))
            {
                var found = byNumber.GetValueOrDefault(number);
                errors.Add(found is null
                    ? Issue($"Geen groep in de optocht heeft startnummer {number}" + (string.IsNullOrWhiteSpace(row.GroupName) ? "." : $" (in het bestand: {row.GroupName})."),
                        "Ken eerst startnummers toe (Samenstellen of de startnummerimport), of haal deze regel weg.")
                    : !found.HasVehicle
                        ? Issue($"Startnummer {number} is {found.GroupName}; dat is geen wagen (alleen wagens krijgen een aanrijtijd).",
                            "Haal de tijd bij deze regel weg, of kies bij de inschrijving een categorie met een voertuig.")
                        : Issue($"Startnummer {number} is {found.GroupName}, maar die inschrijving is (nog) niet goedgekeurd.",
                            "Keur de inschrijving eerst goed en importeer daarna opnieuw, of haal deze regel weg."));
                continue;
            }

            TimeOnly? time = null;
            if (!string.IsNullOrWhiteSpace(row.Time))
            {
                if (ParseTime(row.Time) is not { } parsed)
                {
                    errors.Add(Issue($"\"{row.Time}\" is geen tijd.", "Schrijf de tijd als 10:30 of 10:30 uur."));
                    continue;
                }

                time = parsed;
            }

            if (registration.ArrivalTime != time)
            {
                changes.Add((registration, time));
            }
        }

        return (changes, errors);
    }

    private static int? WholeNumber(string value)
    {
        var text = value.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
        {
            return number;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && d is >= 1 and <= 9999 ? (int)d : null;
    }

    /// <summary>"10:30", "10:30 uur", "10.30", "1030" of een Excel-tijd (fractie van een dag, als tekst "0.4375").</summary>
    public static TimeOnly? ParseTime(string value)
    {
        var text = value.Trim().ToLowerInvariant().Replace("uur", string.Empty, StringComparison.Ordinal).Trim().Replace('.', ':');
        if (TimeOnly.TryParseExact(text, ["H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            return time;
        }

        if (text.Length is 3 or 4 && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var hhmm) && hhmm / 100 < 24 && hhmm % 100 < 60)
        {
            return new TimeOnly(hhmm / 100, hhmm % 100);
        }

        var fraction = value.Trim().Replace(',', '.');
        return double.TryParse(fraction, NumberStyles.Float, CultureInfo.InvariantCulture, out var day) && day is > 0 and < 1
            ? TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(Math.Round(day * 24 * 60)))
            : null;
    }

    // ----- Openbaar en voor de groep -----------------------------------------------------------------------------

    public async Task<PublicArrivals> PublicAsync(CancellationToken cancellationToken)
    {
        var parade = await db.CurrentParades().AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (parade?.ArrivalTimesPublishedAt is null)
        {
            return new PublicArrivals(false, parade?.Name, parade?.ParadeDate, parade?.ArrivalLocation, []);
        }

        var rows = await VehiclesAsync(parade.Id, tracked: false, cancellationToken);
        return new PublicArrivals(true, parade.Name, parade.ParadeDate, parade.ArrivalLocation,
            [.. rows.Where(x => x.Registration.ArrivalTime != null && x.Registration.StartNumber != null)
                .OrderBy(x => x.Registration.ArrivalTime).ThenBy(x => x.Registration.StartNumber)
                .Select(x => new PublicArrivalRow(x.Registration.StartNumber, x.Category, x.Registration.GroupName, Format(x.Registration.ArrivalTime!.Value)))]);
    }

    public static string Format(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    // ----- Intern ------------------------------------------------------------------------------------------------

    private async Task SaveAsync(Parade parade, IReadOnlyList<(ParadeRegistration Registration, TimeOnly? Old)> changes, string action, object values, CancellationToken cancellationToken)
    {
        if (parade.ArrivalTimesPublishedAt is not null)
        {
            // Al gepubliceerd: de groep hoort het direct.
            foreach (var (registration, _) in changes.Where(c => c.Registration.ArrivalTime != null))
            {
                await NotifyAsync(parade, registration, changed: true, cancellationToken);
            }
        }

        changeContext.Source = RegistrationSource.Portal;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, "Parade", parade.Id.ToString(), null, JsonSerializer.Serialize(values)), cancellationToken);
    }

    private async Task NotifyAsync(Parade parade, ParadeRegistration registration, bool changed, CancellationToken cancellationToken)
    {
        var managers = await db.ParadeRegistrationManagers.Where(m => m.RegistrationId == registration.Id).Select(m => m.UserId).ToListAsync(cancellationToken);
        if (managers.Count == 0 || registration.ArrivalTime is not { } time)
        {
            return;
        }

        var where = parade.ArrivalLocation is { } location ? $" bij {location}" : string.Empty;
        await notifications.EnqueueAsync(new SystemNotification(
            changed ? "Aanrijtijd optocht gewijzigd" : "Aanrijtijd optocht bekend",
            changed
                ? $"De aanrijtijd van {registration.GroupName} is nu {Format(time)} uur{where}."
                : $"De aanrijtijd voor jullie groep is bekend: {Format(time)} uur{where} ({registration.GroupName}).",
            NotificationCategory.Parade, new NotificationAudience(UserIds: managers), "drammers://optocht"), cancellationToken);
    }

    private static DomainException Invalid(string message) => new(ErrorCodes.Validation, message);
}
