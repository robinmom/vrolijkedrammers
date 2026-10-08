using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Advertisers;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Adverteerders (fase 27b), menukop "Adverteerders" in het portal: het Excel-overzicht inlezen, adverteerders beheren
/// en de campagne per jaar en per collectant volgen.
/// </summary>
[ApiController]
[Route("api/v1/admin/advertisers")]
[RequirePermission(Permissions.AdvertiserManage)]
public sealed partial class AdminAdvertisersController(DrammersDbContext db, AdvertiserAdministration advertisers, IAuditLogger audit) : ControllerBase
{
    public const long MaxImportBytes = 10 * 1024 * 1024;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<AdvertiserSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdvertiserSummaryResponse>> Search(
        [FromQuery] string? search, [FromQuery] Guid? collector, [FromQuery] AdvertiserKind? kind, [FromQuery] AdvertiserPayment? payment,
        [FromQuery] bool? active, CancellationToken cancellationToken)
    {
        var query = db.Advertisers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = int.TryParse(term, CultureInfo.InvariantCulture, out var number)
                ? query.Where(a => a.Number == number)
                : query.Where(a => a.CompanyName.Contains(term) || (a.ContactName != null && a.ContactName.Contains(term)) || (a.City != null && a.City.Contains(term)));
        }

        // Historie (fase 27f): de laatste vijf jaar tot en met het campagnejaar.
        var campaign = await advertisers.CampaignYearAsync(cancellationToken);
        var firstYear = campaign - 4;
        query = query.Where(a => (collector == null || a.CollectorMemberId == collector) && (kind == null || a.Kind == kind)
            && (payment == null || a.Payment == payment) && (active == null || a.Active == active));
        var rows = await query.OrderBy(a => a.Number)
            .Select(a => new
            {
                a.Id,
                a.Number,
                a.CompanyName,
                a.ContactName,
                a.City,
                a.Email,
                a.Kind,
                a.Payment,
                a.CollectorMemberId,
                a.ImportedCollectorName,
                a.IbanLast4,
                a.MandateReference,
                a.Active,
                a.AddedViaApp,
                Last = a.Years.Where(y => y.Amount > 0 || y.IsFree).OrderByDescending(y => y.Year).Select(y => new { y.Year, y.Amount, y.IsFree }).FirstOrDefault(),
                History = a.Years.Where(y => y.Year >= firstYear && y.Year <= campaign).OrderBy(y => y.Year)
                    .Select(y => new AdvertiserHistoryItem(y.Year, y.Amount, y.IsFree, y.Status)).ToList(),
            })
            .ToListAsync(cancellationToken);
        var names = await advertisers.CollectorNamesAsync(rows.Select(r => r.CollectorMemberId), cancellationToken);
        return [.. rows.Select(r => new AdvertiserSummaryResponse(r.Id, r.Number, r.CompanyName, r.ContactName, r.City, r.Email, r.Kind, r.Payment,
            r.CollectorMemberId, r.CollectorMemberId is { } c ? names.GetValueOrDefault(c) : null, r.ImportedCollectorName, r.IbanLast4 is not null,
            r.MandateReference is not null, r.Last?.Year, r.Last?.Amount, r.Last?.IsFree ?? false, r.Active, r.AddedViaApp, r.History))];
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdvertiserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdvertiserResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var a = await db.Advertisers.AsNoTracking().Include(x => x.Years).SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Adverteerder niet gevonden.", DomainErrorKind.NotFound);
        var names = await advertisers.CollectorNamesAsync([a.CollectorMemberId], cancellationToken);
        return new AdvertiserResponse(a.Id, a.Number, a.CompanyName, a.ContactName, a.Phone, a.Mobile, a.Email, a.AddressLine, a.PostalCode, a.City,
            a.Website, a.Page, a.Kind, a.Payment, a.IbanLast4 is null ? null : $"**** {a.IbanLast4}", a.MandateReference, a.CollectorMemberId,
            a.CollectorMemberId is { } c ? names.GetValueOrDefault(c) : null, a.ImportedCollectorName, a.Notes, a.Active, a.AddedViaApp,
            [.. a.Years.OrderByDescending(y => y.Year).Select(y => new AdvertiserYearResponse(y.Year, y.Amount, y.IsFree, y.Status, y.StatusChangedAt, y.Note))]);
    }

    /// <summary>De volledige IBAN, bijvoorbeeld om hem na te kijken; wordt gelogd.</summary>
    [HttpGet("{id:guid}/iban")]
    [ProducesResponseType<AdvertiserIbanResponse>(StatusCodes.Status200OK)]
    public async Task<AdvertiserIbanResponse> Iban(Guid id, CancellationToken cancellationToken)
    {
        var iban = await advertisers.IbanAsync(id, cancellationToken);
        await audit.WriteAsync(new AuditEntry("advertiser.iban-viewed", "Advertiser", id.ToString()), cancellationToken);
        return new AdvertiserIbanResponse(iban);
    }

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(AdvertiserRequest request, CancellationToken cancellationToken)
    {
        var id = await advertisers.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/advertisers/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, AdvertiserRequest request, CancellationToken cancellationToken)
    {
        await advertisers.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpGet("collectors")]
    [ProducesResponseType<IReadOnlyList<AdvertiserCollector>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<AdvertiserCollector>> Collectors(CancellationToken cancellationToken) => advertisers.CollectorsAsync(cancellationToken);

    // ----- Campagne ------------------------------------------------------------------------------------------------

    [HttpGet("campaign-year")]
    [ProducesResponseType<CampaignYearResponse>(StatusCodes.Status200OK)]
    public async Task<CampaignYearResponse> GetCampaignYear(CancellationToken cancellationToken) => new(await advertisers.CampaignYearAsync(cancellationToken));

    [HttpPut("campaign-year")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetCampaignYear(CampaignYearResponse request, CancellationToken cancellationToken)
    {
        await advertisers.SetCampaignYearAsync(request.Year, cancellationToken);
        return NoContent();
    }

    /// <summary>De stand van de campagne; zonder jaar het lopende campagnejaar.</summary>
    [HttpGet("status")]
    [ProducesResponseType<AdvertiserStatusReport>(StatusCodes.Status200OK)]
    public async Task<AdvertiserStatusReport> Status([FromQuery] int? year, [FromQuery] Guid? collector, CancellationToken cancellationToken) =>
        await advertisers.StatusAsync(year ?? await advertisers.CampaignYearAsync(cancellationToken), collector, cancellationToken);

    [HttpPut("{id:guid}/years/{year:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStatus(Guid id, int year, AdvertiserStatusRequest request, CancellationToken cancellationToken)
    {
        await advertisers.SetStatusAsync(id, year, request.Status, request.Amount, request.Note, cancellationToken, request.CashReceived);
        return NoContent();
    }

    /// <summary>Contant ontvangen (fase 27d) aan- of uitzetten, zonder de stand te wijzigen.</summary>
    [HttpPut("{id:guid}/years/{year:int}/cash")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetCash(Guid id, int year, CashReceivedRequest request, CancellationToken cancellationToken)
    {
        await advertisers.SetCashReceivedAsync(id, year, request.Received, cancellationToken);
        return NoContent();
    }

    /// <summary>Ronde 1, 2 of 3 (of geen) in het campagnejaar (fase 27g); kleurt de regel in de export.</summary>
    [HttpPut("{id:guid}/years/{year:int}/round")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetRound(Guid id, int year, AdvertiserRoundRequest request, CancellationToken cancellationToken)
    {
        await advertisers.SetRoundAsync(id, year, request.Round, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Het overzicht als Excel (fase 27g): per actieve adverteerder de pagina, het bedrijf, het bedrag van de campagne (in te
    /// vullen), de bijdragen van de twee jaren ervoor en de opmerking van de collectant. De hele regel krijgt een kleur:
    /// lichtrood als de adverteerder stopt, anders die van de ronde (1 lichtgroen, 2 lichtblauw, 3 lichtgeel). Op
    /// paginanummer, dan op naam.
    /// </summary>
    [HttpGet("export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var campaign = year ?? await advertisers.CampaignYearAsync(cancellationToken);
        var list = (await db.Advertisers.AsNoTracking().Include(a => a.Years).Where(a => a.Active).ToListAsync(cancellationToken))
            .OrderBy(a => PageNumber(a.Page) ?? int.MaxValue).ThenBy(a => a.Page).ThenBy(a => a.CompanyName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Campagne " + campaign.ToString(CultureInfo.InvariantCulture));
        string[] headers =
        [
            "Pagina", "Naam bedrijf", $"Bedrag {campaign}", $"Bijdrage {campaign - 1}", $"Bijdrage {campaign - 2}", "Opmerkingen",
        ];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        static XLCellValue Contribution(AdvertiserYear? y) =>
            y is null ? Blank.Value : y.IsFree ? "GRATIS" : y.Amount is { } amount ? amount : Blank.Value;

        var row = 2;
        foreach (var a in list)
        {
            var current = a.Years.SingleOrDefault(y => y.Year == campaign);
            XLCellValue[] values =
            [
                a.Page ?? "", a.CompanyName, Contribution(current), Contribution(a.Years.SingleOrDefault(y => y.Year == campaign - 1)),
                Contribution(a.Years.SingleOrDefault(y => y.Year == campaign - 2)), current?.Note ?? "",
            ];
            for (var c = 0; c < values.Length; c++)
            {
                sheet.Cell(row, c + 1).Value = values[c];
            }

            if (RowColor(current) is { } color)
            {
                sheet.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = color;
            }

            row++;
        }

        sheet.Range(2, 3, Math.Max(row - 1, 2), 5).Style.NumberFormat.Format = "€ #,##0.00";
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, Math.Min(row, 200), 8, 60);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("advertiser.exported", "Advertiser", "excel", null, $"{list.Count} adverteerders, campagne {campaign}"), cancellationToken);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"adverteerders-{campaign}.xlsx");
    }

    /// <summary>
    /// Kleur van de regel: lichtrood als de adverteerder dit jaar stopt (gaat voor de ronde), anders lichtgroen, lichtblauw
    /// of lichtgeel voor ronde 1, 2 en 3.
    /// </summary>
    internal static XLColor? RowColor(AdvertiserYear? year) => year switch
    {
        { Status: AdvertiserYearStatus.Stopped } => XLColor.FromHtml("#F4CCCC"),
        { Round: 1 } => XLColor.FromHtml("#D9EAD3"),
        { Round: 2 } => XLColor.FromHtml("#DDEBF7"),
        { Round: 3 } => XLColor.FromHtml("#FFF2CC"),
        _ => null,
    };

    /// <summary>Het paginanummer voor het sorteren; de kolom kan ook tekst bevatten (bijv. "2025 niet").</summary>
    private static int? PageNumber(string? page) =>
        int.TryParse(page?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : null;

    // ----- Import --------------------------------------------------------------------------------------------------

    /// <summary>Controleert het Excel-overzicht zonder iets op te slaan: nieuw, bijgewerkt, fouten en waarschuwingen per regel.</summary>
    [HttpPost("import/preview")]
    [RequestSizeLimit(MaxImportBytes + 1_000_000)]
    [ProducesResponseType<AdvertiserImportPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AdvertiserImportPreview> Preview(IFormFile file, CancellationToken cancellationToken) =>
        await advertisers.PreviewAsync(Read(file), cancellationToken);

    [HttpPost("import")]
    [RequestSizeLimit(MaxImportBytes + 1_000_000)]
    [ProducesResponseType<AdvertiserImportPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<AdvertiserImportPreview> Import(IFormFile file, CancellationToken cancellationToken) =>
        await advertisers.ApplyAsync(Read(file), cancellationToken);

    /// <summary>Het tabblad met de kolom "NAAM BEDRIJF" (in "Advertentie overzicht" heet dat "Campagne 2022").</summary>
    public static List<AdvertiserImportRow> Read(IFormFile file)
    {
        if (file.Length is 0 or > MaxImportBytes || !(file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || file.FileName.EndsWith(".xlsm", StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een Excel-bestand (.xlsx of .xlsm) van hooguit 10 MB.");
        }

        using var copy = new MemoryStream();
        using (var stream = file.OpenReadStream())
        {
            stream.CopyTo(copy);
        }

        copy.Position = 0;
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(copy);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit bestand kan niet worden gelezen. Sla het op als Excel-werkmap en probeer het opnieuw.");
        }

        using (workbook)
        {
            foreach (var sheet in workbook.Worksheets)
            {
                for (var headerRow = 1; headerRow <= 10; headerRow++)
                {
                    var headers = sheet.Row(headerRow).CellsUsed().ToDictionary(c => c.Address.ColumnNumber, Header);
                    if (!headers.ContainsValue("NAAM BEDRIJF"))
                    {
                        continue;
                    }

                    int? Column(Func<string, bool> match) => headers.Where(h => match(h.Value)).Select(h => (int?)h.Key).FirstOrDefault();
                    int? Named(string name) => Column(h => h == name);
                    var years = headers.Select(h => (h.Key, Match: ContributionHeader().Match(h.Value))).Where(h => h.Match.Success)
                        .ToDictionary(h => h.Key, h => int.Parse(h.Match.Groups[1].Value, CultureInfo.InvariantCulture));
                    var columns = new
                    {
                        Collector = Named("NAAM COLLECTANT"),
                        Number = Column(h => h is "NR." or "NR" or "NUMMER"),
                        Page = Named("PAGINA"),
                        Company = Named("NAAM BEDRIJF"),
                        Contact = Column(h => h.StartsWith("CONTACT", StringComparison.Ordinal)),
                        Phone = Column(h => h.StartsWith("TELEFOON", StringComparison.Ordinal)),
                        Mobile = Column(h => h.StartsWith("MOBIEL", StringComparison.Ordinal)),
                        Email = Column(h => h is "MAILADRES" or "E-MAIL" or "EMAIL"),
                        Address = Named("ADRES"),
                        PostalCode = Column(h => h is "POST-CODE" or "POSTCODE"),
                        City = Named("PLAATS"),
                        Kind = Named("A/V/G"),
                        Iban = Named("IBAN"),
                        Payment = Named("M/C/R/B"),
                        Particulars = Named("BIJZONDERHEDEN"),
                        Website = Named("SITE"),
                        Mandate = Column(h => h.StartsWith("SEPA", StringComparison.Ordinal)),
                        Remark = Named("OPMERKING"),
                    };

                    var rows = new List<AdvertiserImportRow>();
                    var last = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
                    for (var r = headerRow + 1; r <= last; r++)
                    {
                        string? Get(int? column) => column is { } c ? Text(sheet.Cell(r, c)) : null;
                        var company = Get(columns.Company);
                        var number = Get(columns.Number);
                        if (company is null && number is null)
                        {
                            continue;
                        }

                        rows.Add(new AdvertiserImportRow(r, Get(columns.Collector), number, Get(columns.Page), company, Get(columns.Contact), Get(columns.Phone),
                            Get(columns.Mobile), Get(columns.Email), Get(columns.Address), Get(columns.PostalCode), Get(columns.City), Get(columns.Kind),
                            Get(columns.Iban), Get(columns.Payment), Get(columns.Particulars), Get(columns.Website), Get(columns.Mandate),
                            years.ToDictionary(y => y.Value, y => Text(sheet.Cell(r, y.Key))), Get(columns.Remark)));
                    }

                    return rows;
                }
            }
        }

        throw new DomainException(ErrorCodes.Validation, "Geen tabblad gevonden met de kolom \"NAAM BEDRIJF\".");
    }

    /// <summary>Kolomkop in hoofdletters zonder regeleinden en dubbele spaties ("BIJDRAGE\n 2001" → "BIJDRAGE 2001").</summary>
    private static string Header(IXLCell cell) => string.Join(' ', cell.GetString().ToUpperInvariant().Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    private static string? Text(IXLCell cell)
    {
        var value = cell.Value;
        var text = value.IsNumber ? value.GetNumber().ToString("0.##", CultureInfo.InvariantCulture)
            : value.IsBlank ? null
            : value.IsError ? null
            : cell.GetFormattedString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    [GeneratedRegex(@"^BIJDRAGE (\d{4})$")]
    private static partial Regex ContributionHeader();
}

public sealed record AdvertiserSummaryResponse(
    Guid Id, int Number, string CompanyName, string? ContactName, string? City, string? Email, AdvertiserKind Kind, AdvertiserPayment Payment,
    Guid? CollectorMemberId, string? CollectorName, string? ImportedCollectorName, bool HasIban, bool HasMandate, int? LastYear, decimal? LastAmount,
    bool LastFree, bool Active, bool AddedViaApp, IReadOnlyList<AdvertiserHistoryItem> History);

public sealed record AdvertiserYearResponse(int Year, decimal? Amount, bool IsFree, AdvertiserYearStatus Status, DateTime? StatusChangedAt, string? Note);

public sealed record AdvertiserResponse(
    Guid Id, int Number, string CompanyName, string? ContactName, string? Phone, string? Mobile, string? Email, string? AddressLine, string? PostalCode,
    string? City, string? Website, string? Page, AdvertiserKind Kind, AdvertiserPayment Payment, string? MaskedIban, string? MandateReference,
    Guid? CollectorMemberId, string? CollectorName, string? ImportedCollectorName, string? Notes, bool Active, bool AddedViaApp,
    IReadOnlyList<AdvertiserYearResponse> Years);

public sealed record AdvertiserIbanResponse(string? Iban);

public sealed record CampaignYearResponse([Range(2000, 2100)] int Year);

public sealed record AdvertiserStatusRequest(
    AdvertiserYearStatus Status, [Range(0, 100000)] decimal? Amount, [StringLength(500)] string? Note, bool? CashReceived = null);

public sealed record CashReceivedRequest(bool Received);

public sealed record AdvertiserRoundRequest([Range(1, 3)] int? Round);

public sealed record AdvertiserRequest(
    [Range(1, 100000)] int Number,
    [Required, StringLength(200, MinimumLength = 1)] string CompanyName,
    [StringLength(150)] string? ContactName,
    [StringLength(30)] string? Phone,
    [StringLength(30)] string? Mobile,
    [StringLength(254)] string? Email,
    [StringLength(200)] string? AddressLine,
    [StringLength(10)] string? PostalCode,
    [StringLength(100)] string? City,
    [StringLength(200)] string? Website,
    [StringLength(50)] string? Page,
    AdvertiserKind Kind,
    AdvertiserPayment Payment,
    [StringLength(40)] string? Iban,
    [StringLength(35)] string? MandateReference,
    Guid? CollectorMemberId,
    [StringLength(2000)] string? Notes,
    bool Active = true)
{
    public AdvertiserInput ToInput() => new(Number, CompanyName, ContactName, Phone, Mobile, Email, AddressLine, PostalCode, City, Website, Page, Kind, Payment,
        Iban, MandateReference, CollectorMemberId, Notes, Active);
}
