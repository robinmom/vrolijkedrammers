using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Aanrijtijden van de wagens (fase 16): meldplek, genereren op startnummervolgorde, per groep aanpassen, een Excel in
/// het formaat van de websitetabel inlezen (Stnr. + tijd) en publiceren.
/// </summary>
[ApiController]
[Route("api/v1/admin/parade/arrival-times")]
[RequirePermission(Permissions.ParadeImportArrivalTimes)]
public sealed class AdminParadeArrivalsController(ParadeArrivals arrivals) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ArrivalList>(StatusCodes.Status200OK)]
    public Task<ArrivalList> List(CancellationToken cancellationToken) => arrivals.ListAsync(cancellationToken);

    [HttpPut("location")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Location(ArrivalLocationRequest request, CancellationToken cancellationToken)
    {
        await arrivals.SetLocationAsync(request.Location, cancellationToken);
        return NoContent();
    }

    [HttpPost("generate")]
    [ProducesResponseType<ArrivalChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ArrivalChangedResponse> Generate(GenerateArrivalsRequest request, CancellationToken cancellationToken) =>
        new(await arrivals.GenerateAsync(request.First, request.IntervalMinutes, request.OnlyEmpty, cancellationToken));

    [HttpPut("{registrationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Set(Guid registrationId, SetArrivalRequest request, CancellationToken cancellationToken)
    {
        await arrivals.SetAsync(registrationId, request.ArrivalTime, cancellationToken);
        return NoContent();
    }

    [HttpPost("publish")]
    [ProducesResponseType<ArrivalChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ArrivalChangedResponse> Publish(CancellationToken cancellationToken) => new(await arrivals.PublishAsync(cancellationToken));

    [HttpPost("import/preview")]
    [RequestSizeLimit(AdminParadeExchangeController.MaxImportBytes + 100_000)]
    [ProducesResponseType<ArrivalImportPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ArrivalImportPreview> Preview(IFormFile file, CancellationToken cancellationToken)
    {
        var (rows, location) = Read(file);
        return await arrivals.PreviewImportAsync(rows, location, cancellationToken);
    }

    [HttpPost("import")]
    [RequestSizeLimit(AdminParadeExchangeController.MaxImportBytes + 100_000)]
    [ProducesResponseType<ArrivalChangedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ArrivalChangedResponse> Import(IFormFile file, CancellationToken cancellationToken)
    {
        var (rows, location) = Read(file);
        return new(await arrivals.ImportAsync(rows, location, cancellationToken));
    }

    /// <summary>
    /// De tabel zoals op de website: een kolom "Stnr." (of "Startnummer") en een tijdkolom. De tijdkolom heet
    /// "Aanrijtijd", of heeft de meldplek als kop (bijv. "Rotonde Holthuizen"): dan wordt dat de meldplek.
    /// </summary>
    private static (List<ArrivalImportRow> Rows, string? Location) Read(IFormFile file)
    {
        if (file.Length is 0 or > AdminParadeExchangeController.MaxImportBytes || !file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een Excel-bestand (.xlsx) van hooguit 5 MB.");
        }

        XLWorkbook workbook;
        try
        {
            var copy = new MemoryStream();
            using (var stream = file.OpenReadStream())
            {
                stream.CopyTo(copy);
            }

            copy.Position = 0;
            workbook = new XLWorkbook(copy);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit bestand kan niet worden gelezen. Sla het op als Excel-werkmap (.xlsx) en probeer het opnieuw.");
        }

        string[] other = ["categorie", "naam", "naam groep", "opgave", "soort"];
        using (workbook)
        {
            foreach (var sheet in workbook.Worksheets)
            {
                for (var headerRow = 1; headerRow <= 5; headerRow++)
                {
                    var cells = sheet.Row(headerRow).CellsUsed().ToList();
                    var start = cells.FirstOrDefault(c => Header(c) is "stnr." or "stnr" or "startnummer" or "startnr." or "startnr");
                    if (start is null)
                    {
                        continue;
                    }

                    var time = cells.FirstOrDefault(c => Header(c).StartsWith("aanrijtijd", StringComparison.Ordinal))
                        ?? cells.LastOrDefault(c => c != start && !other.Contains(Header(c)));
                    if (time is null)
                    {
                        throw new DomainException(ErrorCodes.Validation, "Geen tijdkolom gevonden naast \"Stnr.\".");
                    }

                    var name = cells.FirstOrDefault(c => Header(c) is "naam" or "naam groep");
                    var location = Header(time).StartsWith("aanrijtijd", StringComparison.Ordinal) ? null : time.GetFormattedString().Trim();
                    var rows = new List<ArrivalImportRow>();
                    var last = sheet.LastRowUsed()?.RowNumber() ?? headerRow;
                    for (var r = headerRow + 1; r <= last; r++)
                    {
                        var number = Text(sheet.Cell(r, start.Address.ColumnNumber));
                        var value = TimeText(sheet.Cell(r, time.Address.ColumnNumber));
                        if (number is not null || value is not null)
                        {
                            rows.Add(new ArrivalImportRow(r, number, value, name is null ? null : Text(sheet.Cell(r, name.Address.ColumnNumber))));
                        }
                    }

                    return (rows, location);
                }
            }
        }

        throw new DomainException(ErrorCodes.Validation, "Geen kolom \"Stnr.\" gevonden in de kopregel.");
    }

    private static string Header(IXLCell cell) => cell.GetFormattedString().Trim().ToLowerInvariant();

    private static string? Text(IXLCell cell)
    {
        var value = cell.CachedValue;
        var text = value.IsNumber ? value.GetNumber().ToString(CultureInfo.InvariantCulture) : value.IsBlank ? null : value.ToString(CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    /// <summary>Een Excel-tijd komt als tijd of als dagfractie binnen; tekst ("10:30 uur") blijft tekst.</summary>
    private static string? TimeText(IXLCell cell)
    {
        var value = cell.CachedValue;
        if (value.IsTimeSpan)
        {
            return value.GetTimeSpan().ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }

        if (value.IsDateTime)
        {
            return value.GetDateTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        }

        return Text(cell);
    }
}

public sealed record ArrivalLocationRequest([StringLength(100)] string? Location);

public sealed record GenerateArrivalsRequest(TimeOnly First, [Range(1, 60)] int IntervalMinutes, bool OnlyEmpty);

public sealed record SetArrivalRequest(TimeOnly? ArrivalTime);

public sealed record ArrivalChangedResponse(int Changed);
