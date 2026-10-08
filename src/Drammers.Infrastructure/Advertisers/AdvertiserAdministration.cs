using System.Globalization;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Advertisers;

/// <summary>Een regel uit het Excel-overzicht, zoals de API hem inleest (alle cellen als tekst).</summary>
public sealed record AdvertiserImportRow(
    int RowNumber, string? Collector, string? Number, string? Page, string? CompanyName, string? ContactName, string? Phone, string? Mobile,
    string? Email, string? AddressLine, string? PostalCode, string? City, string? Kind, string? Iban, string? Payment, string? Particulars,
    string? Website, string? MandateReference, IReadOnlyDictionary<int, string?> Contributions, string? Remark);

public sealed record AdvertiserImportIssue(int Row, string Message);

public sealed record AdvertiserImportPreview(
    int Rows, int New, int Updated, IReadOnlyList<AdvertiserImportIssue> Errors, IReadOnlyList<AdvertiserImportIssue> Warnings,
    IReadOnlyList<string> UnknownCollectors, IReadOnlyList<int> Years);

public sealed record AdvertiserInput(
    int Number, string CompanyName, string? ContactName, string? Phone, string? Mobile, string? Email, string? AddressLine, string? PostalCode,
    string? City, string? Website, string? Page, AdvertiserKind Kind, AdvertiserPayment Payment, string? Iban, string? MandateReference,
    Guid? CollectorMemberId, string? Notes, bool Active);

public sealed record AdvertiserCollector(Guid MemberId, string Name);

public sealed record AdvertiserStatusRow(
    Guid Id, int Number, string CompanyName, string? City, AdvertiserKind Kind, AdvertiserPayment Payment, Guid? CollectorMemberId, string? CollectorName,
    AdvertiserYearStatus Status, decimal? Amount, bool IsFree, decimal? PreviousAmount, DateTime? StatusChangedAt, string? Note, DateTime? PaidAt = null,
    int? Round = null);

/// <summary>Tellers; <c>CashReceived</c>/<c>CashOutstanding</c>: van de opgehaalde contante bijdragen wat binnen is en wat nog niet (fase 27d).</summary>
public sealed record AdvertiserStatusTotals(
    int Total, int Collected, int Stopped, int Open, decimal CollectedAmount, decimal ExpectedAmount, decimal CashReceived = 0, decimal CashOutstanding = 0);

public sealed record AdvertiserCollectorTotals(Guid? CollectorMemberId, string Name, AdvertiserStatusTotals Totals);

public sealed record AdvertiserStatusReport(
    int Year, AdvertiserStatusTotals Totals, IReadOnlyList<AdvertiserCollectorTotals> PerCollector, IReadOnlyList<AdvertiserStatusRow> Rows);

/// <summary>De bijdrage van één jaar in de historie (fase 27f); jaar Y = carnavalsjaar (Y-1)/Y.</summary>
public sealed record AdvertiserHistoryItem(int Year, decimal? Amount, bool IsFree, AdvertiserYearStatus Status);

/// <summary>Een adverteerder zoals de collectant hem in de app ziet.</summary>
public sealed record MyAdvertiser(
    Guid Id, int Number, string CompanyName, string? ContactName, string? Phone, string? Mobile, string? Email, string? AddressLine, string? PostalCode,
    string? City, AdvertiserKind Kind, AdvertiserPayment Payment, AdvertiserYearStatus Status, decimal? Amount, decimal? PreviousAmount, string? Note,
    bool CashReceived = false, IReadOnlyList<AdvertiserHistoryItem>? History = null, string? Page = null);

public sealed record MyAdvertisers(bool IsCollector, int Year, IReadOnlyList<MyAdvertiser> Items, string? Info = null);

/// <summary>Informatie voor de collectanten van een campagnejaar (fase 27h).</summary>
public sealed record AdvertiserCampaignInfo(int Year, string? Text);

/// <summary>Een nieuwe adverteerder vanuit de app; bij een machtiging zijn IBAN en toestemming nodig.</summary>
public sealed record NewAdvertiserInput(
    string CompanyName, string? ContactName, string? Phone, string? Email, string? AddressLine, string? PostalCode, string? City, AdvertiserKind Kind,
    AdvertiserPayment Payment, decimal Amount, string? Iban, bool MandateConsent, string? Note, bool CashReceived = false);

/// <summary>IBAN's van adverteerders, versleuteld met een eigen sleutel (los van die van de leden).</summary>
public sealed class AdvertiserIbanProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Drammers.AdvertiserIban.v1");

    public string Protect(string iban) => _protector.Protect(iban);

    public string Unprotect(string protectedIban) => _protector.Unprotect(protectedIban);
}

/// <summary>
/// Adverteerders (fase 27b): het Excel-overzicht inlezen (opnieuw inlezen werkt bij), beheren, collectanten uit het kader
/// koppelen en per jaar bijhouden wie is opgehaald. Betaling M (machtiging), C (contant) of R (rekening); andere waarden meldt
/// de import per regel, zodat het Excel-bestand eerst wordt aangepast.
/// </summary>
public sealed class AdvertiserAdministration(
    DrammersDbContext db, AdvertiserIbanProtector ibans, IAuditLogger audit, IClock clock, ICurrentActor actor)
{
    // ----- Collectanten en campagnejaar ----------------------------------------------------------------------------

    /// <summary>
    /// Wie collectant kan zijn (fase 27g): kaderleden (kaderlijst) en leden met het recht <c>advertiser.collect</c> via een
    /// rol (Kaderlid of Collectant), met een actief account en een geldige roltoewijzing.
    /// </summary>
    private IQueryable<Guid> CollectorMemberIds()
    {
        var permission = DefaultRoles.PermissionId(SharedKernel.Authorization.Permissions.AdvertiserCollect);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var viaRole =
            from u in db.Users
            where u.MemberId != null && u.AccountStatus == AccountStatus.Active
            join ur in db.UserRoles on u.Id equals ur.UserId
            where (ur.ValidFrom == null || ur.ValidFrom <= today) && (ur.ValidTo == null || ur.ValidTo >= today)
            join rp in db.RolePermissions on ur.RoleId equals rp.RoleId
            where rp.PermissionId == permission
            select u.MemberId!.Value;
        return db.CommitteeMembers.Where(c => c.MemberId != null).Select(c => c.MemberId!.Value).Union(viaRole);
    }

    /// <summary>Kaderleden en collectanten (zie <see cref="CollectorMemberIds"/>), op naam.</summary>
    public async Task<IReadOnlyList<AdvertiserCollector>> CollectorsAsync(CancellationToken cancellationToken)
    {
        var ids = await CollectorMemberIds().Distinct().ToListAsync(cancellationToken);
        return await db.Members.AsNoTracking().Where(m => ids.Contains(m.Id)).OrderBy(m => m.FullName)
            .Select(m => new AdvertiserCollector(m.Id, m.FullName)).ToListAsync(cancellationToken);
    }

    /// <summary>Het jaar van de lopende campagne: ingesteld in het portal, anders het komende carnavalsjaar.</summary>
    public async Task<int> CampaignYearAsync(CancellationToken cancellationToken)
    {
        var value = await db.AppConfiguration.AsNoTracking().Where(s => s.Key == AppConfigurationKeys.AdvertiserCampaignYear)
            .Select(s => s.Value).SingleOrDefaultAsync(cancellationToken);
        if (int.TryParse(value, CultureInfo.InvariantCulture, out var year))
        {
            return year;
        }

        // Na carnaval (vanaf maart) begint de campagne voor de krant van het volgende carnaval.
        var now = clock.UtcNow.UtcDateTime;
        return now.Month >= 3 ? now.Year + 1 : now.Year;
    }

    public async Task SetCampaignYearAsync(int year, CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een jaar tussen 2000 en 2100.");
        }

        var setting = await db.AppConfiguration.SingleOrDefaultAsync(s => s.Key == AppConfigurationKeys.AdvertiserCampaignYear, cancellationToken);
        var before = setting?.Value;
        if (setting is null)
        {
            db.AppConfiguration.Add(new AppConfigurationSetting { Key = AppConfigurationKeys.AdvertiserCampaignYear, Value = year.ToString(CultureInfo.InvariantCulture) });
        }
        else
        {
            setting.Value = year.ToString(CultureInfo.InvariantCulture);
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.campaign-year-changed", "AppConfiguration", AppConfigurationKeys.AdvertiserCampaignYear, before,
            year.ToString(CultureInfo.InvariantCulture)), cancellationToken);
    }

    /// <summary>De informatie voor de collectanten van dit jaar (tarieven, inleverdatum, contactpersoon).</summary>
    public async Task<AdvertiserCampaignInfo> InfoAsync(int year, CancellationToken cancellationToken) =>
        new(year, await db.AppConfiguration.AsNoTracking().Where(s => s.Key == AppConfigurationKeys.AdvertiserInfo(year))
            .Select(s => s.Value).SingleOrDefaultAsync(cancellationToken));

    public async Task SetInfoAsync(int year, string? text, CancellationToken cancellationToken)
    {
        if (year is < 2000 or > 2100)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een jaar tussen 2000 en 2100.");
        }

        var value = text?.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (value?.Length > 1000)
        {
            throw new DomainException(ErrorCodes.Validation, "De informatie mag hooguit 1000 tekens zijn.");
        }

        var key = AppConfigurationKeys.AdvertiserInfo(year);
        var setting = await db.AppConfiguration.SingleOrDefaultAsync(s => s.Key == key, cancellationToken);
        var before = setting?.Value;
        if (string.IsNullOrEmpty(value))
        {
            if (setting is not null)
            {
                db.AppConfiguration.Remove(setting);
            }
        }
        else if (setting is null)
        {
            db.AppConfiguration.Add(new AppConfigurationSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.info-changed", "AppConfiguration", key, before, value), cancellationToken);
    }

    // ----- Import --------------------------------------------------------------------------------------------------

    public async Task<AdvertiserImportPreview> PreviewAsync(IReadOnlyList<AdvertiserImportRow> rows, CancellationToken cancellationToken) =>
        (await PlanAsync(rows, cancellationToken)).Preview;

    /// <summary>Leest het overzicht in; alleen als er geen fouten zijn. Bestaande adverteerders (op nummer) worden bijgewerkt.</summary>
    public async Task<AdvertiserImportPreview> ApplyAsync(IReadOnlyList<AdvertiserImportRow> rows, CancellationToken cancellationToken)
    {
        var plan = await PlanAsync(rows, cancellationToken);
        if (plan.Preview.Errors.Count > 0)
        {
            throw new DomainException(ErrorCodes.Validation, $"Het bestand heeft {plan.Preview.Errors.Count} fouten; pas het Excel-bestand aan en lees het opnieuw in.");
        }

        var numbers = plan.Items.Select(i => i.Number).ToList();
        var existing = await db.Advertisers.Include(a => a.Years).Where(a => numbers.Contains(a.Number)).ToDictionaryAsync(a => a.Number, cancellationToken);
        foreach (var item in plan.Items)
        {
            if (!existing.TryGetValue(item.Number, out var advertiser))
            {
                advertiser = new Advertiser { Id = IdGenerator.NewId(), Number = item.Number, CompanyName = item.CompanyName };
                db.Advertisers.Add(advertiser);
            }

            (advertiser.CompanyName, advertiser.ContactName, advertiser.Phone, advertiser.Mobile, advertiser.Email, advertiser.AddressLine,
                advertiser.PostalCode, advertiser.City, advertiser.Website, advertiser.Page, advertiser.Kind, advertiser.Payment, advertiser.Notes) =
                (item.CompanyName, item.ContactName, item.Phone, item.Mobile, item.Email, item.AddressLine, item.PostalCode, item.City, item.Website,
                    item.Page, item.Kind, item.Payment, item.Notes);
            advertiser.MandateReference = item.MandateReference ?? advertiser.MandateReference;
            if (item.Iban is { } iban)
            {
                (advertiser.IbanProtected, advertiser.IbanLast4) = (ibans.Protect(iban), iban[^4..]);
            }

            advertiser.ImportedCollectorName = item.CollectorId is null ? item.CollectorName : null;
            advertiser.CollectorMemberId = item.CollectorId ?? (item.CollectorName is null ? null : advertiser.CollectorMemberId);

            foreach (var (year, amount, free) in item.Years)
            {
                var row = advertiser.Years.SingleOrDefault(y => y.Year == year);
                if (row is null)
                {
                    row = new AdvertiserYear { AdvertiserId = advertiser.Id, Year = year };
                    advertiser.Years.Add(row);
                }

                (row.Amount, row.IsFree) = (amount, free);
                // De stand uit het portal of de app gaat voor; anders volgt hij uit het bedrag.
                if (row.StatusChangedBy is null)
                {
                    row.Status = free || amount > 0 ? AdvertiserYearStatus.Collected : AdvertiserYearStatus.Stopped;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.imported", "Advertiser", "excel", null,
            JsonSerializer.Serialize(new { plan.Preview.Rows, plan.Preview.New, plan.Preview.Updated }, JsonSerializerOptions.Web)), cancellationToken);
        return plan.Preview;
    }

    private sealed record PlannedAdvertiser(
        int Number, string CompanyName, string? ContactName, string? Phone, string? Mobile, string? Email, string? AddressLine, string? PostalCode,
        string? City, string? Website, string? Page, AdvertiserKind Kind, AdvertiserPayment Payment, string? Iban, string? MandateReference,
        Guid? CollectorId, string? CollectorName, string? Notes, IReadOnlyList<(int Year, decimal? Amount, bool Free)> Years);

    private sealed record Plan(AdvertiserImportPreview Preview, IReadOnlyList<PlannedAdvertiser> Items);

    private async Task<Plan> PlanAsync(IReadOnlyList<AdvertiserImportRow> rows, CancellationToken cancellationToken)
    {
        var errors = new List<AdvertiserImportIssue>();
        var warnings = new List<AdvertiserImportIssue>();
        var unknown = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        var collectors = (await CollectorsAsync(cancellationToken)).GroupBy(c => NameKey(c.Name)).ToDictionary(g => g.Key, g => g.ToList());
        var items = new List<PlannedAdvertiser>();
        var seen = new Dictionary<int, int>();
        var years = new SortedSet<int>();

        foreach (var row in rows)
        {
            void Error(string message) => errors.Add(new AdvertiserImportIssue(row.RowNumber, message));
            var company = Clean(row.CompanyName, 200);
            if (company is null)
            {
                Error("Naam bedrijf ontbreekt.");
                continue;
            }

            if (!int.TryParse(row.Number?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number <= 0)
            {
                Error($"{company}: het nummer (NR.) ontbreekt of is geen getal.");
                continue;
            }

            if (seen.TryGetValue(number, out var firstRow))
            {
                Error($"{company}: nummer {number} staat ook op regel {firstRow}.");
                continue;
            }

            seen[number] = row.RowNumber;
            var kind = row.Kind?.Trim().ToUpperInvariant() switch
            {
                "A" => AdvertiserKind.Advertisement,
                "V" => AdvertiserKind.FreeGift,
                "G" => AdvertiserKind.Gift,
                _ => (AdvertiserKind?)null,
            };
            if (kind is null)
            {
                Error($"{company}: kolom A/V/G moet A, V of G zijn (nu \"{row.Kind}\").");
            }

            var payment = row.Payment?.Trim().ToUpperInvariant() switch
            {
                "M" => AdvertiserPayment.Mandate,
                "C" => AdvertiserPayment.Cash,
                "R" => AdvertiserPayment.Invoice,
                _ => (AdvertiserPayment?)null,
            };
            if (payment is null)
            {
                Error($"{company}: kolom M/C/R/B moet M (machtiging), C (contant) of R (rekening) zijn (nu \"{row.Payment}\").");
            }

            string? iban = null;
            if (!string.IsNullOrWhiteSpace(row.Iban))
            {
                try
                {
                    iban = MembershipApplications.NormalizeIban(row.Iban);
                }
                catch (DomainException)
                {
                    Error($"{company}: de IBAN klopt niet.");
                }
            }

            var email = Clean(row.Email, 254);
            if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _))
            {
                Error($"{company}: het e-mailadres \"{email}\" klopt niet.");
            }

            var mandate = Clean(row.MandateReference, 35);
            if (payment == AdvertiserPayment.Mandate && (iban is null || mandate is null))
            {
                warnings.Add(new AdvertiserImportIssue(row.RowNumber, $"{company}: machtiging zonder {(iban is null ? "IBAN" : "machtigingsnummer")}; incasso is pas mogelijk als die is ingevuld."));
            }

            var amounts = new List<(int, decimal?, bool)>();
            foreach (var (year, value) in row.Contributions)
            {
                var text = value?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                years.Add(year);
                if (text.Equals("GRATIS", StringComparison.OrdinalIgnoreCase))
                {
                    amounts.Add((year, 0m, true));
                }
                else if (decimal.TryParse(text.Replace("€", "", StringComparison.Ordinal).Replace(',', '.').Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) && amount >= 0)
                {
                    amounts.Add((year, Math.Round(amount, 2), false));
                }
                else
                {
                    // Oude kolommen bevatten soms een opmerking in plaats van een bedrag: overslaan, niet tegenhouden.
                    warnings.Add(new AdvertiserImportIssue(row.RowNumber, $"{company}: bijdrage {year} \"{text}\" is geen bedrag en wordt overgeslagen."));
                }
            }

            Guid? collectorId = null;
            var collectorName = Clean(row.Collector, 150);
            if (collectorName is not null)
            {
                if (collectors.TryGetValue(NameKey(collectorName), out var matches) && matches.Count == 1)
                {
                    collectorId = matches[0].MemberId;
                }
                else
                {
                    unknown.Add(collectorName);
                }
            }

            var notes = string.Join("\n", new[] { Clean(row.Particulars, 1000), Clean(row.Remark, 1000) }.OfType<string>());
            items.Add(new PlannedAdvertiser(number, company, Clean(row.ContactName, 150), Clean(row.Phone, 30), Clean(row.Mobile, 30), email,
                Clean(row.AddressLine, 200), Clean(row.PostalCode, 10), Clean(row.City, 100), Clean(row.Website, 200), Clean(row.Page, 50),
                kind ?? default, payment ?? default, iban, mandate, collectorId, collectorName, notes.Length == 0 ? null : notes, amounts));
        }

        foreach (var name in unknown)
        {
            warnings.Add(new AdvertiserImportIssue(0, $"Collectant \"{name}\" is geen (gekoppeld) kaderlid of collectant; koppel de adverteerders daarna in het portal."));
        }

        var known = await db.Advertisers.AsNoTracking().Where(a => seen.Keys.Contains(a.Number)).Select(a => a.Number).ToListAsync(cancellationToken);
        var preview = new AdvertiserImportPreview(rows.Count, items.Count(i => !known.Contains(i.Number)), items.Count(i => known.Contains(i.Number)),
            errors, warnings, [.. unknown], [.. years]);
        return new Plan(preview, items);
    }

    // ----- Beheer --------------------------------------------------------------------------------------------------

    public async Task<Guid> CreateAsync(AdvertiserInput input, CancellationToken cancellationToken)
    {
        var advertiser = new Advertiser { Id = IdGenerator.NewId(), CompanyName = "" };
        await ApplyAsync(advertiser, input, cancellationToken);
        db.Advertisers.Add(advertiser);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.created", "Advertiser", advertiser.Id.ToString(), null, advertiser.CompanyName), cancellationToken);
        return advertiser.Id;
    }

    public async Task UpdateAsync(Guid id, AdvertiserInput input, CancellationToken cancellationToken)
    {
        var advertiser = await db.Advertisers.SingleOrDefaultAsync(a => a.Id == id, cancellationToken) ?? throw NotFound();
        await ApplyAsync(advertiser, input, cancellationToken);
        advertiser.AddedViaApp = false;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.updated", "Advertiser", id.ToString(), null, advertiser.CompanyName), cancellationToken);
    }

    private async Task ApplyAsync(Advertiser advertiser, AdvertiserInput input, CancellationToken cancellationToken)
    {
        if (Clean(input.CompanyName, 200) is not { } company)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul de naam van het bedrijf in.");
        }

        if (input.Number <= 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Het nummer moet groter dan 0 zijn.");
        }

        if (await db.Advertisers.AnyAsync(a => a.Number == input.Number && a.Id != advertiser.Id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, $"Nummer {input.Number} is al in gebruik.");
        }

        var email = Clean(input.Email, 254);
        if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            throw new DomainException(ErrorCodes.Validation, "Het e-mailadres klopt niet.");
        }

        if (input.CollectorMemberId is { } collector && !(await CollectorsAsync(cancellationToken)).Any(c => c.MemberId == collector))
        {
            throw new DomainException(ErrorCodes.Validation, "De collectant moet een kaderlid zijn of de rol Collectant hebben.");
        }

        (advertiser.Number, advertiser.CompanyName, advertiser.ContactName, advertiser.Phone, advertiser.Mobile, advertiser.Email, advertiser.AddressLine,
            advertiser.PostalCode, advertiser.City, advertiser.Website, advertiser.Page, advertiser.Kind, advertiser.Payment, advertiser.MandateReference,
            advertiser.Notes, advertiser.Active) =
            (input.Number, company, Clean(input.ContactName, 150), Clean(input.Phone, 30), Clean(input.Mobile, 30), email, Clean(input.AddressLine, 200),
                Clean(input.PostalCode, 10), Clean(input.City, 100), Clean(input.Website, 200), Clean(input.Page, 50), input.Kind, input.Payment,
                Clean(input.MandateReference, 35), Clean(input.Notes, 2000), input.Active);
        if (input.CollectorMemberId != advertiser.CollectorMemberId)
        {
            (advertiser.CollectorMemberId, advertiser.ImportedCollectorName) = (input.CollectorMemberId, null);
        }

        // Lege IBAN = ongewijzigd; "-" = weghalen.
        if (input.Iban?.Trim() == "-")
        {
            (advertiser.IbanProtected, advertiser.IbanLast4) = (null, null);
        }
        else if (!string.IsNullOrWhiteSpace(input.Iban))
        {
            var iban = MembershipApplications.NormalizeIban(input.Iban);
            (advertiser.IbanProtected, advertiser.IbanLast4) = (ibans.Protect(iban), iban[^4..]);
        }
    }

    // ----- Campagne ------------------------------------------------------------------------------------------------

    /// <summary>De stand van de campagne van een jaar: per adverteerder, per collectant en in totaal.</summary>
    public async Task<AdvertiserStatusReport> StatusAsync(int year, Guid? collectorMemberId, CancellationToken cancellationToken)
    {
        var query = db.Advertisers.AsNoTracking().Include(a => a.Years.Where(y => y.Year <= year)).Where(a => a.Active);
        var advertisers = await query.ToListAsync(cancellationToken);
        var names = await CollectorNamesAsync(advertisers.Select(a => a.CollectorMemberId), cancellationToken);
        var rows = advertisers.Select(a =>
        {
            var current = a.Years.SingleOrDefault(y => y.Year == year);
            var previous = a.Years.Where(y => y.Year < year && (y.Amount is > 0 || y.IsFree)).OrderByDescending(y => y.Year).FirstOrDefault();
            return new AdvertiserStatusRow(a.Id, a.Number, a.CompanyName, a.City, a.Kind, a.Payment, a.CollectorMemberId,
                a.CollectorMemberId is { } c ? names.GetValueOrDefault(c) : a.ImportedCollectorName,
                current?.Status ?? AdvertiserYearStatus.Open, current?.Amount, current?.IsFree ?? false, previous?.Amount, current?.StatusChangedAt, current?.Note,
                current?.PaidAt, current?.Round);
        }).OrderBy(r => r.CollectorName ?? "~").ThenBy(r => r.CompanyName).ToList();

        var perCollector = rows.GroupBy(r => (r.CollectorMemberId, Name: r.CollectorName ?? "Zonder collectant"))
            .Select(g => new AdvertiserCollectorTotals(g.Key.CollectorMemberId, g.Key.Name, Totals(g))).OrderBy(c => c.Name).ToList();
        var shown = collectorMemberId is null ? rows : rows.Where(r => r.CollectorMemberId == collectorMemberId).ToList();
        return new AdvertiserStatusReport(year, Totals(shown), perCollector, shown);
    }

    private static AdvertiserStatusTotals Totals(IEnumerable<AdvertiserStatusRow> rows)
    {
        var list = rows.ToList();
        var cash = list.Where(r => r.Status == AdvertiserYearStatus.Collected && r.Payment == AdvertiserPayment.Cash && !r.IsFree).ToList();
        return new AdvertiserStatusTotals(list.Count, list.Count(r => r.Status == AdvertiserYearStatus.Collected), list.Count(r => r.Status == AdvertiserYearStatus.Stopped),
            list.Count(r => r.Status == AdvertiserYearStatus.Open),
            list.Where(r => r.Status == AdvertiserYearStatus.Collected).Sum(r => r.Amount ?? 0),
            list.Where(r => r.Status != AdvertiserYearStatus.Stopped).Sum(r => r.Amount ?? r.PreviousAmount ?? 0),
            cash.Where(r => r.PaidAt is not null).Sum(r => r.Amount ?? 0), cash.Where(r => r.PaidAt is null).Sum(r => r.Amount ?? 0));
    }

    /// <summary>Zet de stand van een adverteerder voor een jaar (portal of app); opgehaald zonder bedrag neemt dat van vorig jaar.</summary>
    public async Task SetStatusAsync(
        Guid advertiserId, int year, AdvertiserYearStatus status, decimal? amount, string? note, CancellationToken cancellationToken, bool? cashReceived = null)
    {
        var advertiser = await db.Advertisers.Include(a => a.Years).SingleOrDefaultAsync(a => a.Id == advertiserId, cancellationToken) ?? throw NotFound();
        if (amount is < 0 or > 100_000)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een bedrag in tussen 0 en 100.000 euro.");
        }

        var row = advertiser.Years.SingleOrDefault(y => y.Year == year);
        if (row is null)
        {
            row = new AdvertiserYear { AdvertiserId = advertiserId, Year = year };
            advertiser.Years.Add(row);
        }

        var before = $"{row.Status} {row.Amount}";
        row.Status = status;
        if (status == AdvertiserYearStatus.Collected)
        {
            row.Amount = amount ?? row.Amount ?? advertiser.Years.Where(y => y.Year < year && y.Amount > 0).OrderByDescending(y => y.Year).Select(y => y.Amount).FirstOrDefault();
        }
        else if (amount is not null)
        {
            row.Amount = amount;
        }

        (row.StatusChangedAt, row.StatusChangedBy, row.Note) = (clock.UtcNow.UtcDateTime, actor.UserId, Clean(note, 500));
        ApplyCash(advertiser, row, cashReceived);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.status-changed", "Advertiser", advertiserId.ToString(), before,
            $"{year}: {status} {row.Amount}"), cancellationToken);
    }

    // ----- De collectant in de app (fase 27b-2) -------------------------------------------------------------------

    /// <summary>Of het lid collectant kan zijn: kaderlid of het recht <c>advertiser.collect</c> via een rol.</summary>
    public Task<bool> IsCollectorAsync(Guid memberId, CancellationToken cancellationToken) =>
        CollectorMemberIds().AnyAsync(id => id == memberId, cancellationToken);

    /// <summary>De adverteerders van deze collectant in het lopende campagnejaar: eerst open, dan op naam.</summary>
    public async Task<MyAdvertisers> MineAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var year = await CampaignYearAsync(cancellationToken);
        if (!await IsCollectorAsync(memberId, cancellationToken))
        {
            return new MyAdvertisers(false, year, []);
        }

        var info = (await InfoAsync(year, cancellationToken)).Text;
        var report = await StatusAsync(year, memberId, cancellationToken);
        var ids = report.Rows.Select(r => r.Id).ToList();
        var details = await db.Advertisers.AsNoTracking().Include(a => a.Years.Where(y => y.Year < year && y.Year >= year - 5))
            .Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, cancellationToken);
        return new MyAdvertisers(true, year, [.. report.Rows
            .OrderBy(r => r.Status == AdvertiserYearStatus.Open ? 0 : 1).ThenBy(r => r.CompanyName)
            .Select(r =>
            {
                var a = details[r.Id];
                return new MyAdvertiser(a.Id, a.Number, a.CompanyName, a.ContactName, a.Phone, a.Mobile, a.Email, a.AddressLine, a.PostalCode, a.City, a.Kind,
                    a.Payment, r.Status, r.Amount, r.PreviousAmount, r.Note, r.PaidAt is not null,
                    [.. a.Years.OrderByDescending(y => y.Year).Select(y => new AdvertiserHistoryItem(y.Year, y.Amount, y.IsFree, y.Status))], a.Page);
            })], info);
    }

    /// <summary>De collectant zet de stand van een van zijn eigen adverteerders, in het lopende campagnejaar.</summary>
    public async Task SetMyStatusAsync(
        Guid memberId, Guid advertiserId, AdvertiserYearStatus status, decimal? amount, string? note, CancellationToken cancellationToken, bool? cashReceived = null)
    {
        if (!await db.Advertisers.AnyAsync(a => a.Id == advertiserId && a.CollectorMemberId == memberId && a.Active, cancellationToken))
        {
            throw NotFound();
        }

        await SetStatusAsync(advertiserId, await CampaignYearAsync(cancellationToken), status, amount, note, cancellationToken, cashReceived);
    }

    /// <summary>De collectant zet alleen de opmerking van dit jaar (fase 27g), zonder de stand te wijzigen.</summary>
    public async Task SetMyNoteAsync(Guid memberId, Guid advertiserId, string? note, CancellationToken cancellationToken)
    {
        var advertiser = await db.Advertisers.Include(a => a.Years)
            .SingleOrDefaultAsync(a => a.Id == advertiserId && a.CollectorMemberId == memberId && a.Active, cancellationToken) ?? throw NotFound();
        var year = await CampaignYearAsync(cancellationToken);
        var row = advertiser.Years.SingleOrDefault(y => y.Year == year);
        if (row is null)
        {
            row = new AdvertiserYear { AdvertiserId = advertiserId, Year = year };
            advertiser.Years.Add(row);
        }

        var before = row.Note;
        row.Note = Clean(note, 500);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.note-changed", "Advertiser", advertiserId.ToString(), before, row.Note), cancellationToken);
    }

    /// <summary>
    /// Een nieuwe adverteerder die de collectant zelf heeft gevonden: meteen opgehaald voor dit jaar, met het volgende
    /// vrije nummer. Het bestuur ziet hem in het portal als "nieuw via app" en kijkt de gegevens na.
    /// </summary>
    public async Task<Guid> AddFromAppAsync(Guid memberId, NewAdvertiserInput input, CancellationToken cancellationToken)
    {
        if (!await IsCollectorAsync(memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Alleen kaderleden en collectanten kunnen adverteerders toevoegen.", DomainErrorKind.Forbidden);
        }

        if (Clean(input.CompanyName, 200) is not { } company)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul de naam van het bedrijf in.");
        }

        if (input.Amount is < 0 or > 100_000)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een bedrag in tussen 0 en 100.000 euro.");
        }

        var email = Clean(input.Email, 254);
        if (email is not null && !System.Net.Mail.MailAddress.TryCreate(email, out _))
        {
            throw new DomainException(ErrorCodes.Validation, "Het e-mailadres klopt niet.");
        }

        string? iban = null;
        if (input.Payment == AdvertiserPayment.Mandate)
        {
            iban = MembershipApplications.NormalizeIban(input.Iban);
            if (!input.MandateConsent)
            {
                throw new DomainException(ErrorCodes.Validation, "Vraag toestemming voor de automatische incasso (machtiging).");
            }
        }

        var now = clock.UtcNow.UtcDateTime;
        var number = (await db.Advertisers.MaxAsync(a => (int?)a.Number, cancellationToken) ?? 0) + 1;
        var year = await CampaignYearAsync(cancellationToken);
        var advertiser = new Advertiser
        {
            Id = IdGenerator.NewId(),
            Number = number,
            CompanyName = company,
            ContactName = Clean(input.ContactName, 150),
            Phone = Clean(input.Phone, 30),
            Email = email,
            AddressLine = Clean(input.AddressLine, 200),
            PostalCode = Clean(input.PostalCode, 10),
            City = Clean(input.City, 100),
            Kind = input.Kind,
            Payment = input.Payment,
            IbanProtected = iban is null ? null : ibans.Protect(iban),
            IbanLast4 = iban?[^4..],
            MandateReference = iban is null ? null : $"DVD-ADV-{number}-{now:yyyyMMdd}",
            CollectorMemberId = memberId,
            AddedViaApp = true,
        };
        advertiser.Years.Add(new AdvertiserYear
        {
            AdvertiserId = advertiser.Id,
            Year = year,
            Amount = Math.Round(input.Amount, 2),
            Status = AdvertiserYearStatus.Collected,
            StatusChangedAt = now,
            StatusChangedBy = actor.UserId,
            Note = Clean(input.Note, 500),
            PaidAt = input.Payment == AdvertiserPayment.Cash && input.CashReceived ? now : null,
            PaidBy = input.Payment == AdvertiserPayment.Cash && input.CashReceived ? actor.UserId : null,
        });
        db.Advertisers.Add(advertiser);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.added-via-app", "Advertiser", advertiser.Id.ToString(), null, $"{number} {company}"), cancellationToken);
        return advertiser.Id;
    }

    /// <summary>Contant ontvangen (fase 27d): alleen bij contante betaling en een opgehaalde bijdrage; anders gewist.</summary>
    private void ApplyCash(Advertiser advertiser, AdvertiserYear row, bool? received)
    {
        if (advertiser.Payment != AdvertiserPayment.Cash || row.Status != AdvertiserYearStatus.Collected)
        {
            (row.PaidAt, row.PaidBy) = (null, null);
        }
        else if (received is true && row.PaidAt is null)
        {
            (row.PaidAt, row.PaidBy) = (clock.UtcNow.UtcDateTime, actor.UserId);
        }
        else if (received is false)
        {
            (row.PaidAt, row.PaidBy) = (null, null);
        }
    }

    /// <summary>Zet alleen "contant ontvangen" (portal), zonder de stand te wijzigen.</summary>
    /// <summary>Ronde 1, 2 of 3 (of geen) waarin de adverteerder dit jaar meegaat (fase 27g, portal).</summary>
    public async Task SetRoundAsync(Guid advertiserId, int year, int? round, CancellationToken cancellationToken)
    {
        if (round is not (null or 1 or 2 or 3))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies ronde 1, 2 of 3.");
        }

        var advertiser = await db.Advertisers.Include(a => a.Years).SingleOrDefaultAsync(a => a.Id == advertiserId, cancellationToken) ?? throw NotFound();
        var row = advertiser.Years.SingleOrDefault(y => y.Year == year);
        if (row is null)
        {
            row = new AdvertiserYear { AdvertiserId = advertiserId, Year = year };
            advertiser.Years.Add(row);
        }

        var before = row.Round;
        row.Round = (byte?)round;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.round-changed", "Advertiser", advertiserId.ToString(), before?.ToString(CultureInfo.InvariantCulture),
            $"{year}: {round?.ToString(CultureInfo.InvariantCulture) ?? "geen"}"), cancellationToken);
    }

    public async Task SetCashReceivedAsync(Guid advertiserId, int year, bool received, CancellationToken cancellationToken)
    {
        var advertiser = await db.Advertisers.Include(a => a.Years).SingleOrDefaultAsync(a => a.Id == advertiserId, cancellationToken) ?? throw NotFound();
        var row = advertiser.Years.SingleOrDefault(y => y.Year == year);
        if (advertiser.Payment != AdvertiserPayment.Cash || row?.Status != AdvertiserYearStatus.Collected)
        {
            throw new DomainException(ErrorCodes.Validation, "Alleen een opgehaalde, contante bijdrage kan als ontvangen worden gemarkeerd.");
        }

        ApplyCash(advertiser, row, received);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.cash-received", "Advertiser", advertiserId.ToString(), null, $"{year}: {received}"), cancellationToken);
    }

    /// <summary>De volledige IBAN (alleen voor het portal, met het recht adverteerders beheren).</summary>
    public async Task<string?> IbanAsync(Guid id, CancellationToken cancellationToken)
    {
        var value = await db.Advertisers.AsNoTracking().Where(a => a.Id == id).Select(a => a.IbanProtected).SingleOrDefaultAsync(cancellationToken);
        return value is null ? null : ibans.Unprotect(value);
    }

    public async Task<Dictionary<Guid, string>> CollectorNamesAsync(IEnumerable<Guid?> ids, CancellationToken cancellationToken)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return await db.Members.AsNoTracking().Where(m => list.Contains(m.Id)).ToDictionaryAsync(m => m.Id, m => m.FullName, cancellationToken);
    }

    /// <summary>Naam om op te vergelijken: hoofdletters, zonder accenten en dubbele spaties ("ALFRED RASING" = "Alfred  Rasing").</summary>
    public static string NameKey(string name)
    {
        var normalized = name.Normalize(NormalizationForm.FormD);
        var letters = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return string.Join(' ', new string(letters).ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static string? Clean(string? value, int max)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
    }

    private static DomainException NotFound() => new(ErrorCodes.NotFound, "Adverteerder niet gevonden.", DomainErrorKind.NotFound);
}
