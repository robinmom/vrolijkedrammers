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
public sealed class AdminJubileesController(Jubilees jubilees, JubileeInvitations invitations, IAuditLogger audit) : ControllerBase
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
        string[] headers = ["Carnavalsjaar", "Lidnummer", "Naam", "Inschrijfjaar", "Jubileum telt vanaf", "Aantal jaren lid", "Jubileumcategorie", "Opmerking", "Uitgenodigd op"];
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
            if (j.InvitedAt is { } invitedAt)
            {
                sheet.Cell(row, 9).Value = invitedAt;
                sheet.Cell(row, 9).Style.DateFormat.Format = "dd-mm-yyyy";
            }
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

    /// <summary>Sjabloon van de uitnodiging met de invulvelden die je kunt gebruiken.</summary>
    [HttpGet("invitation-template")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<JubileeInvitationTemplateResponse>(StatusCodes.Status200OK)]
    public async Task<JubileeInvitationTemplateResponse> GetInvitationTemplate(CancellationToken cancellationToken)
    {
        var template = await invitations.GetTemplateAsync(cancellationToken);
        return new(template.Subject, template.Body, template.ReplyTo, JubileeInvitations.Placeholders);
    }

    [HttpPut("invitation-template")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutInvitationTemplate(JubileeInvitationTemplateRequest request, CancellationToken cancellationToken)
    {
        await invitations.SetTemplateAsync(new JubileeInvitationTemplate(request.Subject, request.Body, request.ReplyTo), cancellationToken);
        return NoContent();
    }

    /// <summary>Jubilarissen uitnodigen (per e-mail, via de outbox); wie al is uitgenodigd of geen e-mailadres heeft, wordt overgeslagen.</summary>
    [HttpPost("invitations")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<JubileeInvitationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<JubileeInvitationResult> Invite(JubileeInviteRequest request, CancellationToken cancellationToken) =>
        invitations.InviteAsync(request.CarnivalYearId, request.MemberIds, cancellationToken);

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

public sealed record JubileeInvitationTemplateResponse(string Subject, string Body, string ReplyTo, IReadOnlyList<string> Placeholders);

public sealed record JubileeInvitationTemplateRequest(
    [param: Required, StringLength(200)] string Subject,
    [param: Required, StringLength(4000)] string Body,
    [param: Required, EmailAddress, StringLength(254)] string ReplyTo);

/// <summary>Zonder <c>MemberIds</c>: alle jubilarissen van het carnavalsjaar die nog niet zijn uitgenodigd.</summary>
public sealed record JubileeInviteRequest(int? CarnivalYearId, [param: MaxLength(500)] IReadOnlyList<Guid>? MemberIds);

public sealed record JubileeSettingsResponse(IReadOnlyList<int> Milestones);

public sealed record JubileeSettingsRequest([param: Required] IReadOnlyList<int> Milestones);

public sealed record JubileeOverrideRequest(short? JoinYearOverride, [param: StringLength(200)] string? Note);
