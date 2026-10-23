using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Finance;
using Drammers.Modules.Membership.Finance;
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
public sealed class AdminFinanceController(
    IncomeReports income, FinanceReports finance, FinanceCollectiveInvoices collectiveInvoices,
    MollieSettlements settlements, AdvertiserAdministration advertisers) : ControllerBase
{
    [HttpGet("collective-invoices")]
    [ProducesResponseType<CollectiveInvoiceOverview>(StatusCodes.Status200OK)]
    public async Task<CollectiveInvoiceOverview> CollectiveInvoices(
        [FromQuery] int? carnivalYearId, [FromQuery] CollectiveInvoiceSource source, CancellationToken cancellationToken) =>
        await collectiveInvoices.OverviewAsync(carnivalYearId, source, cancellationToken);

    [HttpGet("collective-invoices/{id:guid}")]
    [ProducesResponseType<CollectiveInvoiceDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<CollectiveInvoiceDetail> CollectiveInvoice(Guid id, CancellationToken cancellationToken) =>
        await collectiveInvoices.DetailAsync(id, cancellationToken);

    [HttpPost("collective-invoices")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedResponse>> CreateCollectiveInvoice(CollectiveInvoiceRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await collectiveInvoices.GenerateAsync(request.CarnivalYearId, request.Source, cancellationToken)));

    [HttpGet("collective-invoices/{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> CollectiveInvoicePdfFile(Guid id, CancellationToken cancellationToken)
    {
        var detail = await collectiveInvoices.DetailAsync(id, cancellationToken);
        var pdf = CollectiveInvoicePdf.Generate(
            detail.Source, detail.Year, detail.Invoice.Number, detail.Invoice.InvoiceDate, detail.Lines, detail.Invoice.Total);
        return File(pdf, "application/pdf", $"Verzamelfactuur {detail.Invoice.Number}.pdf");
    }

    [HttpGet("overview")]
    [ProducesResponseType<FinanceOverviewReport>(StatusCodes.Status200OK)]
    public async Task<FinanceOverviewReport> Overview([FromQuery] int? carnivalYearId, CancellationToken cancellationToken) =>
        await finance.OverviewAsync(carnivalYearId, cancellationToken);

    [HttpGet("statistics")]
    [ProducesResponseType<FinanceStatisticsReport>(StatusCodes.Status200OK)]
    public async Task<FinanceStatisticsReport> Statistics([FromQuery] int? carnivalYearId, CancellationToken cancellationToken) =>
        await finance.StatisticsAsync(carnivalYearId, cancellationToken);

    [HttpGet("festival-invoices")]
    [ProducesResponseType<IReadOnlyList<FestivalInvoiceRow>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<FestivalInvoiceRow>> FestivalInvoices([FromQuery] int? carnivalYearId, CancellationToken cancellationToken) =>
        await finance.FestivalInvoicesAsync(carnivalYearId, cancellationToken);

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

/// <summary>Voor welk onderdeel en carnavalsjaar de volgende (delta)verzamelfactuur wordt gemaakt.</summary>
public sealed record CollectiveInvoiceRequest(int? CarnivalYearId, CollectiveInvoiceSource Source);
