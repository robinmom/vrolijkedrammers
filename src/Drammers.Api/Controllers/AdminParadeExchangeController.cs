using System.Globalization;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Optocht exporteren en startnummers importeren (fase 12c): het deelnemersbestand van de optochtcommissie als Excel,
/// en hetzelfde bestand met ingevulde startnummers terug inlezen (voorbeeld → bevestigen, alles of niets).
/// </summary>
[ApiController]
[Route("api/v1/admin/parade")]
public sealed class AdminParadeExchangeController(ParadeExchange exchange, IAuditLogger audit) : ControllerBase
{
    public const int MaxImportBytes = 5 * 1024 * 1024;

    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>De kolommen van het deelnemersbestand, met de breedtes van het voorbeeldbestand.</summary>
    private static readonly (string Header, double Width)[] Columns =
    [
        ("Opgave", 7), ("Startnummer", 11), ("Naam groep", 28), ("contactpersoon", 20), ("adres", 30), ("tel nr", 14), ("mail adres", 28),
        ("Categorie", 30), ("Soort", 13), ("onderwerp", 45), ("kinderen", 9), ("volwassenen", 12), ("Muziek", 8), ("Bouw adres", 26),
        ("Stalling voor jury", 24), ("Lengte", 8), ("Extra info", 45), ("Tekst", 80),
    ];

    [HttpGet("export")]
    [RequirePermission(Permissions.ParadeExport)]
    [Produces(XlsxContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<FileContentResult> Export(CancellationToken cancellationToken)
    {
        var export = await exchange.ExportAsync(cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet($"deelnemersbestand {export.Year}");
        for (var c = 0; c < Columns.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = Columns[c].Header;
            sheet.Column(c + 1).Width = Columns[c].Width;
        }

        sheet.Row(1).Style.Font.Bold = true;
        var row = 2;
        foreach (var r in export.Rows)
        {
            object?[] values =
            [
                r.RegistrationNumber, r.StartNumber, r.GroupName, r.ContactName, r.ContactAddress, r.Phone, r.Email, r.Category, r.Kind, r.Subject,
                r.Children, r.Adults, r.HasMusic is { } music ? (music ? "Ja" : "Nee") : null, r.BuildAddress, r.JuryAddress, r.LengthMeters,
                r.ExtraInformation, r.Text,
            ];
            for (var c = 0; c < values.Length; c++)
            {
                var cell = sheet.Cell(row, c + 1);
                switch (values[c])
                {
                    case null:
                        break;
                    case int i:
                        cell.Value = i;
                        break;
                    case decimal d:
                        cell.Value = d;
                        break;
                    case string s:
                        // Een tekstwaarde blijft tekst (ClosedXML maakt er nooit een formule van).
                        cell.Value = s;
                        break;
                }
            }

            if (r.Fixed)
            {
                sheet.Row(row).Style.Font.Italic = true;
            }

            row++;
        }

        sheet.Range(1, 1, Math.Max(row - 1, 1), Columns.Length).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("parade.exported", "Parade", export.ParadeName, null, $"{{\"rows\":{export.Rows.Count}}}"), cancellationToken);
        return File(stream.ToArray(), XlsxContentType,
            $"Opgaven optocht {export.Year} {DateTime.UtcNow.ToString("d-M-yyyy", CultureInfo.InvariantCulture)}.xlsx");
    }

    /// <summary>Voorbeeld van een startnummerimport: wijzigingen oud → nieuw en alle fouten; er wordt nog niets opgeslagen.</summary>
    [HttpPost("start-numbers/import/preview")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [RequestSizeLimit(MaxImportBytes + 100_000)]
    [ProducesResponseType<StartNumberImportPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StartNumberImportPreview> Preview(IFormFile file, CancellationToken cancellationToken) =>
        await exchange.PreviewAsync(Read(file), cancellationToken);

    /// <summary>Leest de startnummers in (alles of niets); <c>version</c> uit het voorbeeld, anders 412.</summary>
    [HttpPost("start-numbers/import")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [RequestSizeLimit(MaxImportBytes + 100_000)]
    [ProducesResponseType<StartNumberImportResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<StartNumberImportResult> Import(IFormFile file, [FromForm] int version, CancellationToken cancellationToken) =>
        new(await exchange.ApplyAsync(version, Read(file), cancellationToken));

    /// <summary>
    /// Alleen .xlsx en alleen celwaarden (geen macro's, geen formules uitvoeren). Het blad met de kolommen Opgave en
    /// Startnummer in de kopregel (één van de eerste vijf regels); koppelen op Opgave, nooit op groepsnaam.
    /// </summary>
    private static List<StartNumberImportRow> Read(IFormFile file)
    {
        if (file.Length is 0 or > MaxImportBytes || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een Excel-bestand (.xlsx) van hooguit 5 MB.");
        }

        XLWorkbook workbook;
        try
        {
            using var stream = file.OpenReadStream();
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            copy.Position = 0;
            workbook = new XLWorkbook(copy);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit bestand kan niet worden gelezen. Sla het op als Excel-werkmap (.xlsx) en probeer het opnieuw.");
        }

        using (workbook)
        {
            foreach (var sheet in workbook.Worksheets)
            {
                for (var headerRow = 1; headerRow <= 5; headerRow++)
                {
                    var cells = sheet.Row(headerRow).CellsUsed().ToList();
                    var opgave = cells.FirstOrDefault(c => Header(c) == "opgave");
                    var start = cells.FirstOrDefault(c => Header(c) == "startnummer");
                    if (opgave is null || start is null)
                    {
                        continue;
                    }

                    var last = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
                    var rows = new List<StartNumberImportRow>();
                    for (var r = headerRow + 1; r <= last; r++)
                    {
                        var number = Text(sheet.Cell(r, opgave.Address.ColumnNumber));
                        var startNumber = Text(sheet.Cell(r, start.Address.ColumnNumber));
                        if (number is not null || startNumber is not null)
                        {
                            rows.Add(new StartNumberImportRow(r, number, startNumber));
                        }
                    }

                    return rows;
                }
            }
        }

        throw new DomainException(ErrorCodes.Validation, "Geen kolommen \"Opgave\" en \"Startnummer\" gevonden in de kopregel. Gebruik de export als basis.");
    }

    private static string Header(IXLCell cell) => cell.GetFormattedString().Trim().ToLowerInvariant();

    private static string? Text(IXLCell cell)
    {
        var value = cell.CachedValue;
        var text = value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture) : value.IsBlank ? null : value.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}

public sealed record StartNumberImportResult(int Changed);
