using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Jubilarissen per carnavalsjaar, de instelbare jubilea en de correctie per lid (fase 20, OQ-30).</summary>
[ApiController]
[Route("api/v1/admin/jubilees")]
public sealed class AdminJubileesController(Jubilees jubilees, IAuditLogger audit) : ControllerBase
{
    /// <summary>Jubilarissen en actieve leden zonder inschrijfjaar; zonder <c>carnivalYearId</c> het actieve carnavalsjaar.</summary>
    [HttpGet]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<JubileeReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<JubileeReport> Get([FromQuery] int? carnivalYearId, CancellationToken cancellationToken) =>
        jubilees.BuildAsync(carnivalYearId, cancellationToken);

    /// <summary>Excel met de vaste kolommen uit docs/04 §14 en een tabblad met leden zonder inschrijfjaar; geaudit.</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.MemberExport)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] int? carnivalYearId, CancellationToken cancellationToken)
    {
        var report = await jubilees.BuildAsync(carnivalYearId, cancellationToken);
        using var workbook = new XLWorkbook();

        var sheet = workbook.AddWorksheet("Jubilarissen");
        string[] headers = ["Carnavalsjaar", "Lidnummer", "Naam", "Inschrijfjaar", "Jubileum telt vanaf", "Aantal jaren lid", "Jubileumcategorie", "Opmerking"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var i = 0; i < report.Jubilarians.Count; i++)
        {
            var j = report.Jubilarians[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = report.CarnivalYearName;
            sheet.Cell(row, 2).Value = j.MemberNumber;
            sheet.Cell(row, 3).Value = j.FullName;
            sheet.Cell(row, 4).Value = j.JoinYear;
            sheet.Cell(row, 5).Value = j.BaseYear;
            sheet.Cell(row, 6).Value = j.Years;
            sheet.Cell(row, 7).Value = $"{j.Years} jaar";
            sheet.Cell(row, 8).Value = j.Note;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();

        var missing = workbook.AddWorksheet("Zonder inschrijfjaar");
        missing.Cell(1, 1).Value = "Lidnummer";
        missing.Cell(1, 2).Value = "Naam";
        missing.Cell(1, 3).Value = "Plaats";
        for (var i = 0; i < report.WithoutJoinYear.Count; i++)
        {
            var m = report.WithoutJoinYear[i];
            missing.Cell(i + 2, 1).Value = m.MemberNumber;
            missing.Cell(i + 2, 2).Value = m.FullName;
            missing.Cell(i + 2, 3).Value = m.City;
        }

        missing.Row(1).Style.Font.Bold = true;
        missing.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("report.jubilees.exported", "Report", "jubilees", null,
            $"{{\"carnivalYearId\":{report.CarnivalYearId},\"jubilarians\":{report.Jubilarians.Count},\"withoutJoinYear\":{report.WithoutJoinYear.Count}}}"),
            cancellationToken);
        var fileYear = report.CarnivalYearName.Replace('/', '-');
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"jubilarissen-{fileYear}.xlsx");
    }

    [HttpGet("settings")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<JubileeSettingsResponse>(StatusCodes.Status200OK)]
    public async Task<JubileeSettingsResponse> GetSettings(CancellationToken cancellationToken) =>
        new(await jubilees.GetMilestonesAsync(cancellationToken));

    [HttpPut("settings")]
    [RequirePermission(Permissions.ConfigManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutSettings(JubileeSettingsRequest request, CancellationToken cancellationToken)
    {
        await jubilees.SetMilestonesAsync(request.Milestones, cancellationToken);
        return NoContent();
    }

    /// <summary>Het jaar waarvanaf het jubileum van dit lid telt; leeg = het inschrijfjaar volgen.</summary>
    [HttpPut("members/{memberId:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutMember(Guid memberId, JubileeOverrideRequest request, CancellationToken cancellationToken)
    {
        await jubilees.SetOverrideAsync(memberId, request.JoinYearOverride, request.Note, cancellationToken);
        return NoContent();
    }
}

public sealed record JubileeSettingsResponse(IReadOnlyList<int> Milestones);

public sealed record JubileeSettingsRequest([param: Required] IReadOnlyList<int> Milestones);

public sealed record JubileeOverrideRequest(short? JoinYearOverride, [param: StringLength(200)] string? Note);
