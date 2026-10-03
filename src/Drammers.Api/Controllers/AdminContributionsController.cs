using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Lidmaatschappen, tarieven en contributie (fase 23a); alleen voor wie contributie beheert.</summary>
[ApiController]
[Route("api/v1/admin/contributions")]
[RequirePermission(Permissions.ContributionManage)]
public sealed class AdminContributionsController(Contributions contributions, IClock clock, IAuditLogger audit) : ControllerBase
{
    /// <summary>Contributie per actief lid op de peildatum (standaard vandaag; bij een incasso de incassodatum).</summary>
    [HttpGet]
    [ProducesResponseType<ContributionOverview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<ContributionOverview> Get([FromQuery] DateOnly? date, CancellationToken cancellationToken) =>
        contributions.BuildAsync(date ?? Today, cancellationToken);

    [HttpGet("export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] DateOnly? date, CancellationToken cancellationToken)
    {
        var overview = await contributions.BuildAsync(date ?? Today, cancellationToken);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Contributie");
        string[] headers = ["Lidnummer", "Naam", "Soort lidmaatschap", "Bedrag", "Status", "Partner", "Opmerking"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var i = 0; i < overview.Lines.Count; i++)
        {
            var line = overview.Lines[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = line.MemberNumber;
            sheet.Cell(row, 2).Value = line.FullName;
            sheet.Cell(row, 3).Value = Contributions.KindLabel(line.Kind, line.Senior);
            sheet.Cell(row, 4).Value = line.Amount;
            sheet.Cell(row, 4).Style.NumberFormat.Format = "€ #,##0.00";
            sheet.Cell(row, 5).Value = StatusLabel(line.Status);
            sheet.Cell(row, 6).Value = line.PartnerName;
            sheet.Cell(row, 7).Value = line.Note;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("report.contributions.exported", "Report", "contributions", null,
            $"{{\"date\":\"{overview.Date:yyyy-MM-dd}\",\"lines\":{overview.Lines.Count}}}"), cancellationToken);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"contributie-{overview.Date:yyyyMMdd}.xlsx");
    }

    [HttpGet("rates")]
    [ProducesResponseType<IReadOnlyList<ContributionRate>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ContributionRate>> Rates(CancellationToken cancellationToken) => contributions.RatesAsync(cancellationToken);

    /// <summary>Tarief toevoegen; met een bestaande ingangsdatum wordt dat tarief aangepast.</summary>
    [HttpPut("rates")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveRate(ContributionRateRequest request, CancellationToken cancellationToken)
    {
        await contributions.SaveRateAsync(new ContributionRate
        {
            ValidFrom = request.ValidFrom,
            OnePerson = request.OnePerson,
            TwoPersons = request.TwoPersons,
            OnePersonSenior = request.OnePersonSenior,
            TwoPersonsSenior = request.TwoPersonsSenior,
            Dansgarde = request.Dansgarde,
        }, cancellationToken);
        return NoContent();
    }

    [HttpDelete("rates/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> DeleteRate(int id, CancellationToken cancellationToken)
    {
        await contributions.DeleteRateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("members/{memberId:guid}")]
    [ProducesResponseType<MembershipSettings>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<MembershipSettings> GetMember(Guid memberId, CancellationToken cancellationToken) =>
        contributions.GetSettingsAsync(memberId, cancellationToken);

    /// <summary>Soort lidmaatschap, betaler (partner) en vrijstelling (bijv. Convent) van een lid.</summary>
    [HttpPut("members/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutMember(Guid memberId, MembershipSettingsRequest request, CancellationToken cancellationToken)
    {
        await contributions.SetSettingsAsync(memberId,
            new MembershipSettings(request.Kind, request.PayerMemberId, request.Exempt, request.ExemptReason), cancellationToken);
        return NoContent();
    }

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    private static string StatusLabel(ContributionStatus status) => status switch
    {
        ContributionStatus.Due => "Betaalt",
        ContributionStatus.Exempt => "Vrijgesteld",
        ContributionStatus.PaidByPartner => "Betaald door partner",
        _ => "Onbekend",
    };
}

public sealed record ContributionRateRequest(
    DateOnly ValidFrom, decimal OnePerson, decimal TwoPersons, decimal OnePersonSenior, decimal TwoPersonsSenior, decimal Dansgarde);

public sealed record MembershipSettingsRequest(MembershipKind? Kind, Guid? PayerMemberId, bool Exempt, [param: StringLength(200)] string? ExemptReason);
