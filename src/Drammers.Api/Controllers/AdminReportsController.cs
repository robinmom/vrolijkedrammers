using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Basisrapportage leden (fase 8): aantallen per status, rol, leeftijdsklasse, inschrijfjaar en groep. Alleen
/// aantallen, geen namen; de export wordt geaudit.
/// </summary>
[ApiController]
[Route("api/v1/admin/reports/members")]
[RequirePermission(Permissions.ReportView)]
public sealed class AdminReportsController(DrammersDbContext db, IClock clock, IAuditLogger audit) : ControllerBase
{
    /// <summary>Leeftijdsklassen op basis van de dansgarde/jeugd-indeling; zonder geboortedatum "Onbekend".</summary>
    private static readonly (string Label, int From, int To)[] AgeClasses =
        [("0–11", 0, 11), ("12–15", 12, 15), ("16–17", 16, 17), ("18–24", 18, 24), ("25–39", 25, 39), ("40–64", 40, 64), ("65+", 65, 200)];

    [HttpGet]
    [ProducesResponseType<MemberReportResponse>(StatusCodes.Status200OK)]
    public Task<MemberReportResponse> Get(CancellationToken cancellationToken) => BuildAsync(cancellationToken);

    /// <summary>Ledenverloop per carnavalsjaar (grafiek): nieuw, uitgeschreven en totaal.</summary>
    [HttpGet("history")]
    [ProducesResponseType<MemberHistoryReport>(StatusCodes.Status200OK)]
    public Task<MemberHistoryReport> History([FromServices] MemberHistory history, CancellationToken cancellationToken) =>
        history.ReportAsync(cancellationToken);

    /// <summary>Cijfers van een carnavalsjaar zelf invullen (leeg = weer berekenen).</summary>
    [HttpPut("history/{year:int}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetHistory(int year, MemberHistoryInput input, [FromServices] MemberHistory history, CancellationToken cancellationToken)
    {
        await history.SetAsync(year, input, cancellationToken);
        return NoContent();
    }

    /// <summary>Oudere jaren importeren uit Excel: kolommen Jaar, Nieuw, Uitgeschreven en Totaal.</summary>
    [HttpPost("history/import")]
    [RequirePermission(Permissions.MemberUpdate)]
    [RequestSizeLimit(5_000_000)]
    [ProducesResponseType<HistoryImportedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<HistoryImportedResponse> ImportHistory(IFormFile file, [FromServices] MemberHistory history, CancellationToken cancellationToken)
    {
        List<MemberHistoryImportRow> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheets.First();
            var header = sheet.FirstRowUsed() ?? throw new DomainException(ErrorCodes.Validation, "Het Excel-bestand is leeg.");
            int? Column(params string[] names) => header.CellsUsed().FirstOrDefault(c =>
                names.Any(n => c.GetString().Trim().StartsWith(n, StringComparison.OrdinalIgnoreCase)))?.Address.ColumnNumber;
            var year = Column("jaar", "carnavalsjaar", "seizoen") ?? throw new DomainException(ErrorCodes.Validation, "Kolom \"Jaar\" ontbreekt.");
            var (added, left, total) = (Column("nieuw"), Column("uitgeschreven", "uit", "opgezegd", "vertrokken"), Column("totaal"));
            if (added is null && left is null && total is null)
            {
                throw new DomainException(ErrorCodes.Validation, "Geen kolommen \"Nieuw\", \"Uitgeschreven\" of \"Totaal\" gevonden.");
            }

            string? Cell(IXLRow row, int? column) => column is null ? null : row.Cell(column.Value).GetString();
            rows = [.. sheet.RowsUsed().Where(r => r.RowNumber() > header.RowNumber())
                .Select(r => new MemberHistoryImportRow(r.RowNumber(), r.Cell(year).GetString(), Cell(r, added), Cell(r, left), Cell(r, total)))];
        }
        catch (Exception ex) when (ex is not DomainException and not OperationCanceledException)
        {
            throw new DomainException(ErrorCodes.Validation, "Dit is geen leesbaar Excel-bestand (.xlsx).");
        }

        return new HistoryImportedResponse(await history.ImportAsync(rows, cancellationToken));
    }

    [HttpGet("export")]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export(CancellationToken cancellationToken)
    {
        var report = await BuildAsync(cancellationToken);
        using var workbook = new XLWorkbook();
        AddSheet(workbook, "Status", "Status", report.ByStatus);
        AddSheet(workbook, "Rollen", "Rol", report.ByRole);
        AddSheet(workbook, "Leeftijd", "Leeftijdsklasse", report.ByAgeClass);
        AddSheet(workbook, "Inschrijfjaar", "Inschrijfjaar", report.ByJoinYear);
        AddSheet(workbook, "Groepen", "Groep", report.ByGroup);
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("report.members.exported", "Report", "members", null, $"{{\"total\":{report.Total}}}"), cancellationToken);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ledenrapport-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private async Task<MemberReportResponse> BuildAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var members = await db.Members.AsNoTracking()
            .Select(m => new { m.Id, Status = m.LocalStatusOverride ?? m.MembershipStatus, m.BirthDate, m.JoinYear })
            .ToListAsync(cancellationToken);
        var active = members.Where(m => m.Status == MembershipStatus.Active).ToList();
        var activeIds = active.Select(m => m.Id).ToHashSet();

        var byStatus = members.GroupBy(m => m.Status).OrderBy(g => g.Key)
            .Select(g => new ReportRow(StatusLabel(g.Key), g.Count())).ToList();

        // Rollen via het gekoppelde app-account; leden zonder account tellen niet mee.
        var roleRows = await db.Users.AsNoTracking()
            .Where(u => u.MemberId != null && u.AccountStatus == AccountStatus.Active)
            .SelectMany(u => u.Roles.Select(r => new { u.MemberId, r.RoleId }))
            .Join(db.Roles, x => x.RoleId, r => r.Id, (x, r) => new { x.MemberId, r.Name })
            .ToListAsync(cancellationToken);
        var byRole = roleRows.Where(r => activeIds.Contains(r.MemberId!.Value)).GroupBy(r => r.Name).OrderBy(g => g.Key)
            .Select(g => new ReportRow(g.Key, g.Select(x => x.MemberId).Distinct().Count())).ToList();

        var byAge = AgeClasses.Select(c => new ReportRow(c.Label, active.Count(m => m.BirthDate is { } b && Age(b, today) is var age && age >= c.From && age <= c.To)))
            .Append(new ReportRow("Onbekend", active.Count(m => m.BirthDate is null))).ToList();

        var byJoinYear = active.GroupBy(m => m.JoinYear).OrderBy(g => g.Key ?? short.MaxValue)
            .Select(g => new ReportRow(g.Key?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "Onbekend", g.Count())).ToList();

        var groupRows = await db.GroupMemberships.AsNoTracking()
            .Where(gm => (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Join(db.Groups, gm => gm.GroupId, g => g.Id, (gm, g) => new { gm.MemberId, g.Name })
            .ToListAsync(cancellationToken);
        var byGroup = groupRows.Where(r => activeIds.Contains(r.MemberId)).GroupBy(r => r.Name).OrderBy(g => g.Key)
            .Select(g => new ReportRow(g.Key, g.Count())).ToList();

        return new MemberReportResponse(members.Count, active.Count, byStatus, byRole, byAge, byJoinYear, byGroup);
    }

    private static int Age(DateOnly birth, DateOnly today) =>
        today.Year - birth.Year - (today < birth.AddYears(today.Year - birth.Year) ? 1 : 0);

    private static string StatusLabel(MembershipStatus status) => status switch
    {
        MembershipStatus.Active => "Actief",
        MembershipStatus.Inactive => "Inactief",
        MembershipStatus.Suspended => "Geschorst",
        MembershipStatus.Deceased => "Overleden",
        _ => status.ToString(),
    };

    private static void AddSheet(XLWorkbook workbook, string name, string header, IReadOnlyList<ReportRow> rows)
    {
        var sheet = workbook.AddWorksheet(name);
        sheet.Cell(1, 1).Value = header;
        sheet.Cell(1, 2).Value = "Aantal";
        for (var i = 0; i < rows.Count; i++)
        {
            sheet.Cell(i + 2, 1).Value = rows[i].Label;
            sheet.Cell(i + 2, 2).Value = rows[i].Count;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
    }
}

public sealed record ReportRow(string Label, int Count);

/// <summary>Rollen, leeftijd, inschrijfjaar en groepen tellen alleen actieve leden; status telt alle leden.</summary>
public sealed record MemberReportResponse(
    int Total, int Active, IReadOnlyList<ReportRow> ByStatus, IReadOnlyList<ReportRow> ByRole, IReadOnlyList<ReportRow> ByAgeClass,
    IReadOnlyList<ReportRow> ByJoinYear, IReadOnlyList<ReportRow> ByGroup);

public sealed record HistoryImportedResponse(int Years);
