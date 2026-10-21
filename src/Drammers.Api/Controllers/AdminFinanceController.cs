using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Finance;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Inkomsten (fase 4a) en Mollie-afrekeningen/verzamelstaat (fase 4b): de adverteerders-facturen en de festival-
/// betaalverzoeken van een campagnejaar in één overzicht met CSV-export, plus de Mollie-uitbetalingen naar de bank
/// (omzet − Mollie-kosten = netto) met per uitbetaling de herleide inkomsten. Voor de penningmeester (recht Inkomsten bekijken).
/// </summary>
[ApiController]
[Route("api/v1/admin/finance")]
[RequirePermission(Permissions.FinanceRead)]
public sealed class AdminFinanceController(IncomeReports income, MollieSettlements settlements, AdvertiserAdministration advertisers) : ControllerBase
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

    [HttpGet("settlements")]
    [ProducesResponseType<SettlementOverview>(StatusCodes.Status200OK)]
    public async Task<SettlementOverview> Settlements(CancellationToken cancellationToken) =>
        await settlements.ListAsync(cancellationToken);

    [HttpGet("settlements/{id}")]
    [ProducesResponseType<SettlementDetail>(StatusCodes.Status200OK)]
    public async Task<SettlementDetail> Settlement(string id, CancellationToken cancellationToken) =>
        await settlements.DetailAsync(id, cancellationToken);

    [HttpGet("settlements/{id}/export")]
    [Produces("text/csv")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> SettlementExport(string id, CancellationToken cancellationToken)
    {
        var (fileName, content) = await settlements.ExportCsvAsync(id, cancellationToken);
        return File(content, "text/csv", fileName);
    }

    [HttpPost("settlements/{id}/send")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SettlementSend(string id, [FromBody] SettlementSendRequest request, CancellationToken cancellationToken)
    {
        await settlements.SendAsync(id, request.Recipient, cancellationToken);
        return NoContent();
    }
}

/// <summary>Naar welk e-mailadres de verzamelstaat gaat (standaard penningmeester@vrolijkedrammers.nl).</summary>
public sealed record SettlementSendRequest(string Recipient);
