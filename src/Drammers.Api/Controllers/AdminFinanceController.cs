using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Finance;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Inkomsten (fase 4a): de adverteerders-facturen en de festival-betaalverzoeken van een campagnejaar in één overzicht,
/// met een CSV-export om in SnelStart over te nemen. Voor de penningmeester (recht Inkomsten bekijken).
/// </summary>
[ApiController]
[Route("api/v1/admin/finance")]
[RequirePermission(Permissions.FinanceRead)]
public sealed class AdminFinanceController(IncomeReports income, AdvertiserAdministration advertisers) : ControllerBase
{
    [HttpGet("income")]
    [ProducesResponseType<IncomeReport>(StatusCodes.Status200OK)]
    public async Task<IncomeReport> Income([FromQuery] int? year, CancellationToken cancellationToken) =>
        await income.ReportAsync(year ?? await advertisers.CampaignYearAsync(cancellationToken), cancellationToken);

    [HttpGet("income/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] int? year, CancellationToken cancellationToken)
    {
        var y = year ?? await advertisers.CampaignYearAsync(cancellationToken);
        return File(await income.ExportCsvAsync(y, cancellationToken), "text/csv", $"Inkomsten {y}.csv");
    }
}
