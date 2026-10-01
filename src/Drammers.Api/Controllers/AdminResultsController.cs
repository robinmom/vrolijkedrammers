using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

public enum ResultsExportKind
{
    /// <summary>Per categorie: plaats, startnummer, groep, motto, punten per criterium en totaal.</summary>
    Uitslag,

    /// <summary>Per categorie van de laatste naar de eerste plaats, om bij de prijsuitreiking voor te lezen.</summary>
    Zaallijst,
}

/// <summary>
/// Uitslag van de optocht (fase 22c, alleen de uitslagcommissie: <c>parade.result</c>): per categorie zodra alle
/// juryleden hebben ingediend, als Excel, en publiceren na de prijsuitreiking. Zonder <c>paradeId</c> de huidige optocht.
/// </summary>
[ApiController]
[Route("api/v1/admin/results")]
[RequirePermission(Permissions.ParadeResult)]
public sealed class AdminResultsController(ParadeResults results, IAuditLogger audit) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ResultOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ResultOverviewResponse> Get([FromQuery] Guid? paradeId, CancellationToken cancellationToken) =>
        ResultOverviewResponse.From(await results.OverviewAsync(paradeId, cancellationToken));

    [HttpGet("export")]
    [Produces(AdminParadeExchangeController.XlsxContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] Guid? paradeId, [FromQuery] ResultsExportKind kind, CancellationToken cancellationToken)
    {
        var overview = await results.OverviewAsync(paradeId, cancellationToken);
        using var workbook = new XLWorkbook();
        foreach (var category in overview.Categories)
        {
            var sheet = workbook.AddWorksheet(SheetName(category.Name, workbook));
            sheet.Cell(1, 1).Value = $"{overview.ParadeName} · {category.Name}";
            sheet.Cell(1, 1).Style.Font.Bold = true;
            sheet.Cell(1, 1).Style.Font.FontSize = 14;
            sheet.Cell(2, 1).Value = category.Ready
                ? $"{category.Jurors} juryleden · weging originaliteit {category.WeightOriginality}x, carnavalesk {category.WeightCarnivalesque}x, kwaliteit {category.WeightQuality}x, algemene indruk {category.WeightOverall}x · max {category.MaxPoints} punten"
                : $"Nog niet compleet: {category.Submitted} van {category.Jurors} juryleden hebben ingediend.";
            string[] headers = kind == ResultsExportKind.Uitslag
                ? ["Plaats", "Startnr.", "Groep", "Motto", "Originaliteit", "Carnavalesk", "Kwaliteit", "Algemene indruk", "Totaal"]
                : ["Prijs", "Punten", "Startnr.", "Groep", "Motto"];
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(4, c + 1).Value = headers[c];
            }

            sheet.Row(4).Style.Font.Bold = true;
            var rows = kind == ResultsExportKind.Uitslag ? category.Rows : [.. category.Rows.Reverse()];
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                object?[] values = kind == ResultsExportKind.Uitslag
                    ? [r.Place, r.StartNumber, r.GroupName, r.Motto, r.Originality, r.Carnivalesque, r.Quality, r.Overall, r.Total]
                    : [r.Place, r.Total, r.StartNumber, r.GroupName, r.Motto];
                for (var c = 0; c < values.Length; c++)
                {
                    sheet.Cell(i + 5, c + 1).Value = XLCellValue.FromObject(values[c]);
                }
            }

            sheet.Columns().AdjustToContents(4, 4 + rows.Count);
        }

        if (workbook.Worksheets.Count == 0)
        {
            workbook.AddWorksheet("Uitslag").Cell(1, 1).Value = "Nog geen categorieën met inzendingen.";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("parade.results-exported", "Parade", overview.ParadeId.ToString(), null, $"{{\"kind\":\"{kind}\"}}"), cancellationToken);
        return File(stream.ToArray(), AdminParadeExchangeController.XlsxContentType,
            $"{(kind == ResultsExportKind.Uitslag ? "uitslag" : "zaallijst")}-{overview.ParadeDate:yyyyMMdd}.xlsx");
    }

    /// <summary>Publiceren na de prijsuitreiking: daarna op website en in de app, optocht afgerond, mail naar secretaris en voorzitter.</summary>
    [HttpPost("publish")]
    [ProducesResponseType<PublishResultsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PublishResultsResponse> Publish(PublishResultsRequest request, CancellationToken cancellationToken) =>
        new(await results.PublishAsync(request.ParadeId, request.PrizeCeremonyHeld, cancellationToken));

    /// <summary>
    /// Foto's bij een inzending (fase 22d, ook achteraf): in het album van de uitslag, dat pas met de uitslag zichtbaar
    /// wordt. Hooguit 20 per keer.
    /// </summary>
    [HttpPost("entries/{registrationId:guid}/photos")]
    [RequestSizeLimit(20 * Infrastructure.Content.ContentFiles.MaxPhotoBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = 20 * Infrastructure.Content.ContentFiles.MaxPhotoBytes)]
    [ProducesResponseType<IReadOnlyList<CreatedResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<CreatedResponse>>> AddPhotos(Guid registrationId, IFormFileCollection files, CancellationToken cancellationToken)
    {
        if (files.Count is 0 or > 20)
        {
            throw new SharedKernel.Errors.DomainException(SharedKernel.Errors.ErrorCodes.Validation, "Upload 1 tot 20 foto's per keer.");
        }

        var ids = await results.AddPhotosAsync(registrationId,
            [.. files.Select(f => new Infrastructure.Content.UploadedFile(f.FileName, f.Length, f.OpenReadStream))], cancellationToken);
        return StatusCode(StatusCodes.Status201Created, ids.Select(x => new CreatedResponse(x)).ToList());
    }

    // Excel: hooguit 31 tekens, geen : \ / ? * [ ] en uniek.
    private static string SheetName(string name, XLWorkbook workbook)
    {
        var clean = new string([.. name.Where(c => !":\\/?*[]".Contains(c))]).Trim();
        clean = clean.Length > 31 ? clean[..31] : clean;
        var candidate = clean;
        for (var i = 2; workbook.Worksheets.Contains(candidate); i++)
        {
            candidate = $"{clean[..Math.Min(clean.Length, 28)]} {i}";
        }

        return candidate;
    }
}

public sealed record ResultRowResponse(
    int Place, Guid RegistrationId, int? StartNumber, string GroupName, string? Motto,
    decimal Originality, decimal Carnivalesque, decimal Quality, decimal Overall, decimal Total, int PhotoCount);

public sealed record CategoryResultResponse(
    int CategoryId, string Name, int Jurors, int Submitted, bool Ready, int WeightOriginality, int WeightCarnivalesque,
    int WeightQuality, int WeightOverall, int MaxPoints, int Entries, IReadOnlyList<ResultRowResponse> Rows);

public sealed record ResultOverviewResponse(Guid ParadeId, string ParadeName, DateOnly ParadeDate, DateTime? PublishedAt, IReadOnlyList<CategoryResultResponse> Categories)
{
    public static ResultOverviewResponse From(ParadeResultOverview o) => new(o.ParadeId, o.ParadeName, o.ParadeDate, o.PublishedAt,
        [.. o.Categories.Select(c => new CategoryResultResponse(c.CategoryId, c.Name, c.Jurors, c.Submitted, c.Ready, c.WeightOriginality,
            c.WeightCarnivalesque, c.WeightQuality, c.WeightOverall, c.MaxPoints, c.Entries,
            [.. c.Rows.Select(r => new ResultRowResponse(r.Place, r.RegistrationId, r.StartNumber, r.GroupName, r.Motto,
                r.Originality, r.Carnivalesque, r.Quality, r.Overall, r.Total, r.PhotoCount))]))]);
}

public sealed record PublishResultsRequest(Guid ParadeId, [Required] bool PrizeCeremonyHeld);

public sealed record PublishResultsResponse(DateTime PublishedAt);
