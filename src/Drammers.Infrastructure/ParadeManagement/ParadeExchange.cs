using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.ParadeManagement;

/// <summary>
/// Eén regel van de optochtexport (fase 12c) in de kolommen van het deelnemersbestand van de optochtcommissie:
/// Opgave, Startnummer, Naam groep, contactpersoon, adres, tel nr, mail adres, Categorie, Soort, onderwerp, kinderen,
/// volwassenen, Muziek, Bouw adres, Stalling voor jury, Lengte, Extra info, Tekst.
/// </summary>
public sealed record ParadeExportRow(
    int? RegistrationNumber, int? StartNumber, string? GroupName, string? ContactName, string? ContactAddress, string? Phone, string? Email,
    string? Category, string? Kind, string? Subject, int? Children, int? Adults, bool? HasMusic, string? BuildAddress, string? JuryAddress,
    decimal? LengthMeters, string? ExtraInformation, string? Text, bool Fixed);

public sealed record ParadeExport(string ParadeName, int Year, IReadOnlyList<ParadeExportRow> Rows);

/// <summary>Een regel uit een geüpload bestand: de ruwe celwaarden van Opgave, Startnummer en (ter controle) Naam groep.</summary>
public sealed record StartNumberImportRow(int Row, string? RegistrationNumber, string? StartNumber, string? GroupName = null);

/// <summary>
/// Een fout in een importbestand: <see cref="Message"/> = wat er mis is en waarom, <see cref="Advice"/> = wat je eraan
/// doet; met de gegevens uit het bestand (opgave, startnummer, naam) zodat de regel terug te vinden is.
/// </summary>
public sealed record ImportIssue(
    int Row, string Message, string? Advice = null, string? RegistrationNumber = null, string? StartNumber = null, string? GroupName = null);

public sealed record ImportChange(int RegistrationNumber, string? GroupName, int? OldStartNumber, int? NewStartNumber, bool Published);

/// <summary>Voorbeeld vóór bevestigen: alle wijzigingen en alle fouten (bij één fout wordt niets ingelezen).</summary>
public sealed record StartNumberImportPreview(int Version, int Rows, IReadOnlyList<ImportChange> Changes, IReadOnlyList<ImportIssue> Errors);

/// <summary>
/// Export en import van de optocht (fase 12c): het deelnemersbestand als Excel en startnummers terug inlezen uit
/// datzelfde bestand. Koppelen gebeurt op het opgavenummer (nooit op groepsnaam); inlezen is alles of niets en pas na
/// een voorbeeld. Startnummers 1 … n zijn van de vaste plekken vooraan.
/// </summary>
public sealed class ParadeExchange(DrammersDbContext db, ParadeLineup lineup, IAuditLogger audit)
{
    /// <summary>Inschrijvingen die in het deelnemersbestand horen (geen concepten, afgewezen of ingetrokken).</summary>
    private static readonly RegistrationStatus[] Exported =
    [
        RegistrationStatus.Submitted, RegistrationStatus.UnderReview, RegistrationStatus.AdditionalInformationRequired,
        RegistrationStatus.Approved, RegistrationStatus.StartNumberAssigned, RegistrationStatus.Final,
    ];

    private async Task<Parade> CurrentParadeAsync(CancellationToken cancellationToken) =>
        await db.Parades.AsNoTracking().Where(p => db.CarnivalYears.Any(y => y.Id == p.CarnivalYearId && y.Active)).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht voor het actieve carnavalsjaar.", DomainErrorKind.NotFound);

    public async Task<ParadeExport> ExportAsync(CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var rows = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.ParadeId == parade.Id && Exported.Contains(r.Status))
            .GroupJoin(db.ParadeCategories, r => r.CategoryId, c => c.Id, (r, cs) => new { r, cs })
            .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new { x.r, Category = c })
            .ToListAsync(cancellationToken);

        // Het adres van de contactpersoon: de persoon die inschreef (zijn lidgegevens), als dat een lid is.
        var owners = rows.Where(x => x.r.OwnerUserId != null).Select(x => x.r.OwnerUserId!.Value).Distinct().ToList();
        var addresses = await db.Users.AsNoTracking().Where(u => owners.Contains(u.Id) && u.MemberId != null)
            .Join(db.Members, u => u.MemberId, m => m.Id, (u, m) => new { u.Id, m.AddressLine, m.PostalCode, m.City })
            .ToDictionaryAsync(x => x.Id, x => Join(", ", x.AddressLine, Join(" ", x.PostalCode, x.City)), cancellationToken);

        var result = new List<ParadeExportRow>();
        result.AddRange(parade.FixedEntries.Select((e, i) => new ParadeExportRow(
            null, i + 1, e.Name, null, null, null, null, null, null, null, e.ChildrenCount, e.AdultCount, e.HasMusic, null, null, null, null, null, true)));
        result.AddRange(rows.OrderBy(x => x.r.RegistrationNumber).Select(x => new ParadeExportRow(
            x.r.RegistrationNumber, x.r.StartNumber, x.r.GroupName, x.r.ContactName,
            x.r.OwnerUserId is { } owner ? addresses.GetValueOrDefault(owner) : null,
            x.r.ContactPhone is null ? null : PhoneNormalizer.Display(x.r.ContactPhone), x.r.ContactEmail,
            x.Category?.Name, x.Category is null ? null : x.Category.AgeGroup == AgeGroup.Youth ? "Jeugd" : "Volwassenen",
            x.r.Subject, x.r.ChildrenCount, x.r.AdultCount, x.r.HasMusic, Short(x.r.BuildAddress),
            x.r.JuryInspectionSameAsBuildAddress ? "nvt" : Short(x.r.JuryInspectionAddress),
            x.r.MeasuredLengthMeters ?? x.r.EstimatedLengthMeters, x.r.AdditionalInformation, x.r.SubjectDescription, false)));
        return new ParadeExport(parade.Name, parade.ParadeDate.Year, result);
    }

    /// <summary>Straat en huisnummer; de plaats alleen als die niet Loil is (zoals in het deelnemersbestand).</summary>
    private static string? Short(Address a)
    {
        var street = Join(" ", a.Street, Join("", a.HouseNumber, a.Addition));
        var city = a.City is { } c && !string.Equals(c.Trim(), "Loil", StringComparison.OrdinalIgnoreCase) ? c.Trim() : null;
        return Join(", ", street, city);
    }

    private static string? Join(string separator, params string?[] parts)
    {
        var text = string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
        return text.Length == 0 ? null : text;
    }

    // ----- Import van startnummers ------------------------------------------------------------------------------

    public async Task<StartNumberImportPreview> PreviewAsync(IReadOnlyList<StartNumberImportRow> rows, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        var (changes, errors, _) = await PlanAsync(parade, rows, cancellationToken);
        return new StartNumberImportPreview(parade.CompositionVersion, rows.Count, [.. changes.Select(c => c.Change)], errors);
    }

    /// <summary>Leest in wat het voorbeeld liet zien; alles of niets. Is de optocht intussen gewijzigd, dan 412.</summary>
    public async Task<int> ApplyAsync(int version, IReadOnlyList<StartNumberImportRow> rows, CancellationToken cancellationToken)
    {
        var parade = await CurrentParadeAsync(cancellationToken);
        if (parade.CompositionVersion != version)
        {
            throw new DomainException(ErrorCodes.RegistrationChanged,
                "De optocht is intussen gewijzigd. Laad het bestand opnieuw voor een nieuw voorbeeld.", DomainErrorKind.PreconditionFailed);
        }

        var (changes, errors, _) = await PlanAsync(parade, rows, cancellationToken);
        if (errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Het bestand bevat {errors.Count} fout(en); er is niets ingelezen.");
        }

        if (changes.Count > 0)
        {
            await lineup.ApplyStartNumbersAsync([.. changes.Select(c => (c.Registration, c.Change.NewStartNumber))], cancellationToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE parade.Parade SET composition_version = composition_version + 1 WHERE id = {parade.Id}", cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("parade.start-numbers-imported", "Parade", parade.Id.ToString(), null,
            JsonSerializer.Serialize(new { changes = changes.Select(c => new { c.Change.RegistrationNumber, from = c.Change.OldStartNumber, to = c.Change.NewStartNumber }) })),
            cancellationToken);
        return changes.Count;
    }

    private async Task<(List<(ParadeRegistration Registration, ImportChange Change)> Changes, List<ImportIssue> Errors, int Rows)> PlanAsync(
        Parade parade, IReadOnlyList<StartNumberImportRow> rows, CancellationToken cancellationToken)
    {
        var registrations = await db.ParadeRegistrations.AsNoTracking()
            .Where(r => r.ParadeId == parade.Id && r.RegistrationNumber != null && r.Status != RegistrationStatus.Draft)
            .ToListAsync(cancellationToken);
        var byNumber = registrations.ToDictionary(r => r.RegistrationNumber!.Value);
        var errors = new List<ImportIssue>();
        var seen = new Dictionary<int, int>();
        var target = new Dictionary<Guid, int?>();

        foreach (var row in rows)
        {
            ImportIssue Issue(string message, string advice) =>
                new(row.Row, message, advice, row.RegistrationNumber, row.StartNumber, row.GroupName);

            if (string.IsNullOrWhiteSpace(row.RegistrationNumber))
            {
                // Vaste plekken en lege regels hebben geen opgavenummer.
                continue;
            }

            if (!TryNumber(row.RegistrationNumber, out var number))
            {
                errors.Add(Issue($"In de kolom Opgave staat \"{row.RegistrationNumber}\"; dat is geen opgavenummer.",
                    "Zet in de kolom Opgave alleen het opgavenummer uit de app, of maak de cel leeg."));
                continue;
            }

            // Een vaste plek vooraan (bijv. de geluidswagen) staat in het bronbestand soms mét een opgavenummer: die regel
            // hoort niet bij een inschrijving en wordt overgeslagen.
            if (TryNumber(row.StartNumber ?? string.Empty, out var fixedNumber) && fixedNumber >= 1 && fixedNumber < parade.FirstGroupStartNumber
                && SameName(row.GroupName, parade.FixedEntries[fixedNumber - 1].Name))
            {
                continue;
            }

            if (seen.TryGetValue(number, out var earlier))
            {
                errors.Add(Issue($"Opgave {number} staat twee keer in het bestand (ook op regel {earlier}).", "Laat elke opgave maar één keer voorkomen."));
                continue;
            }

            seen[number] = row.Row;
            if (!byNumber.TryGetValue(number, out var registration))
            {
                errors.Add(Issue(
                    $"Er is in deze optocht geen inschrijving met opgavenummer {number}"
                    + (string.IsNullOrWhiteSpace(row.GroupName) ? " (en de regel heeft geen groepsnaam)." : $" (in het bestand: {row.GroupName})."),
                    "Het opgavenummer komt uit een ander bestand of een ander jaar, of de groep heeft (nog) niet ingeschreven in de app. Haal het startnummer bij deze regel weg of verwijder de regel; gebruik bij voorkeur de export als basis."));
                continue;
            }

            int? startNumber = null;
            if (!string.IsNullOrWhiteSpace(row.StartNumber))
            {
                if (!TryNumber(row.StartNumber, out var n) || n is < 1 or > 9999)
                {
                    errors.Add(Issue($"\"{row.StartNumber}\" is geen geldig startnummer: alleen hele getallen van 1 tot en met 9999.",
                        "Vul een heel getal in, of maak de cel leeg."));
                    continue;
                }

                if (n < parade.FirstGroupStartNumber)
                {
                    errors.Add(Issue(
                        $"Startnummer {n} is gereserveerd voor de vaste plek \"{parade.FixedEntries[n - 1].Name}\"; groepen beginnen bij {parade.FirstGroupStartNumber}.",
                        $"Kies voor {registration.GroupName ?? $"opgave {number}"} een startnummer vanaf {parade.FirstGroupStartNumber}, of pas de vaste plekken aan onder Optocht."));
                    continue;
                }

                startNumber = n;
            }

            if (registration.StartNumber == startNumber)
            {
                continue;
            }

            if (startNumber is not null && !ParadeLineup.InLineup.Contains(registration.Status))
            {
                errors.Add(Issue(
                    $"Opgave {number} ({registration.GroupName ?? "zonder naam"}) heeft de status \"{StatusLabel(registration.Status)}\"; alleen goedgekeurde inschrijvingen krijgen een startnummer.",
                    registration.Status is RegistrationStatus.Rejected or RegistrationStatus.Withdrawn
                        ? "Deze groep doet niet mee: haal het startnummer bij deze regel weg."
                        : "Keur de inschrijving eerst goed (Optocht → Inschrijvingen) en importeer daarna opnieuw, of haal het startnummer weg."));
                continue;
            }

            if (registration.Status == RegistrationStatus.Final)
            {
                errors.Add(Issue(
                    $"Opgave {number} ({registration.GroupName}) is definitief vastgesteld; het startnummer ({registration.StartNumber}) wijzigt niet via een import.",
                    "Wijzig dit startnummer bij de inschrijving zelf (met het recht voor de definitieve optocht), of zet het oude nummer terug in het bestand."));
                continue;
            }

            target[registration.Id] = startNumber;
        }

        // Elk startnummer maar één keer, na alle wijzigingen samen (groepen die niet in het bestand staan houden hun nummer).
        var final = registrations.ToDictionary(r => r.Id, r => target.TryGetValue(r.Id, out var t) ? t : r.StartNumber);
        foreach (var duplicate in final.Where(f => f.Value != null).GroupBy(f => f.Value!.Value).Where(g => g.Count() > 1))
        {
            var names = duplicate.Select(d => registrations.First(r => r.Id == d.Key)).Select(r => $"opgave {r.RegistrationNumber} ({r.GroupName})");
            errors.Add(new ImportIssue(0, $"Startnummer {duplicate.Key} komt meer dan eens voor: {string.Join(", ", names)}.",
                "Geef elke groep een eigen startnummer. Een groep die niet in het bestand staat, houdt haar huidige nummer.",
                StartNumber: duplicate.Key.ToString(CultureInfo.InvariantCulture)));
        }

        var changes = target
            .Select(t => registrations.First(r => r.Id == t.Key))
            .Select(r => (r, new ImportChange(r.RegistrationNumber!.Value, r.GroupName, r.StartNumber, target[r.Id], r.Status == RegistrationStatus.StartNumberAssigned)))
            .OrderBy(c => c.Item2.NewStartNumber ?? int.MaxValue).ThenBy(c => c.Item2.RegistrationNumber)
            .ToList();
        return (changes, errors, rows.Count);
    }

    private static bool SameName(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b)
        && string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => string.Join(' ', value.Replace('"', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string StatusLabel(RegistrationStatus status) => status switch
    {
        RegistrationStatus.Draft => "concept",
        RegistrationStatus.Submitted => "ingediend",
        RegistrationStatus.UnderReview => "in behandeling",
        RegistrationStatus.AdditionalInformationRequired => "aanvulling gevraagd",
        RegistrationStatus.Approved => "goedgekeurd",
        RegistrationStatus.Rejected => "afgewezen",
        RegistrationStatus.Withdrawn => "ingetrokken",
        RegistrationStatus.StartNumberAssigned => "startnummer bekend",
        RegistrationStatus.Final => "definitief",
        _ => status.ToString(),
    };

    /// <summary>Hele getallen, ook zoals Excel ze als tekst of als "12.0" kan opslaan.</summary>
    private static bool TryNumber(string value, out int number)
    {
        number = 0;
        var text = value.Trim();
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) && d == decimal.Truncate(d) && d is >= int.MinValue and <= int.MaxValue)
        {
            number = (int)d;
            return true;
        }

        return false;
    }
}
