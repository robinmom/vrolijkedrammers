using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Advertisers.Invoices;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Facturen aan adverteerders (fase 27e): maken voor de opgehaalde bijdragen van een campagnejaar, als PDF per e-mail
/// versturen namens de penningmeester, en als PDF downloaden (bijvoorbeeld om te printen voor wie geen e-mailadres heeft).
/// </summary>
[ApiController]
[Route("api/v1/admin/advertisers/invoices")]
[RequirePermission(Permissions.AdvertiserManage)]
public sealed class AdminAdvertiserInvoicesController(AdvertiserInvoices invoices, AdvertiserAdministration advertisers) : ControllerBase
{
    [HttpGet("settings")]
    [ProducesResponseType<InvoiceSettings>(StatusCodes.Status200OK)]
    public Task<InvoiceSettings> GetSettings(CancellationToken cancellationToken) => invoices.GetSettingsAsync(cancellationToken);

    [HttpPut("settings")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutSettings(InvoiceSettingsRequest request, CancellationToken cancellationToken)
    {
        await invoices.SetSettingsAsync(new InvoiceSettings(request.Address, request.Kvk), cancellationToken);
        return NoContent();
    }

    /// <summary>Wie in het jaar een factuur krijgt; zonder jaar het lopende campagnejaar.</summary>
    [HttpGet]
    [ProducesResponseType<InvoiceOverview>(StatusCodes.Status200OK)]
    public async Task<InvoiceOverview> Overview([FromQuery] int? year, CancellationToken cancellationToken) =>
        await invoices.OverviewAsync(year ?? await advertisers.CampaignYearAsync(cancellationToken), cancellationToken);

    [HttpPost]
    [ProducesResponseType<InvoiceCountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<InvoiceCountResponse> Create(CreateInvoicesRequest request, CancellationToken cancellationToken)
    {
        var year = request.Year ?? await advertisers.CampaignYearAsync(cancellationToken);
        return new(await invoices.CreateAsync(year, request.Date, cancellationToken));
    }

    [HttpPost("send")]
    [ProducesResponseType<InvoiceCountResponse>(StatusCodes.Status200OK)]
    public async Task<InvoiceCountResponse> Send([FromQuery] int? year, CancellationToken cancellationToken) =>
        new(await invoices.SendAsync(year ?? await advertisers.CampaignYearAsync(cancellationToken), cancellationToken));

    [HttpGet("{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<FileContentResult> Pdf(Guid id, CancellationToken cancellationToken)
    {
        var (name, content) = await invoices.PdfAsync(id, cancellationToken);
        return File(content, "application/pdf", name);
    }

    /// <summary>Alle facturen van het jaar in één PDF; <c>withoutEmail=true</c>: alleen wie geen e-mailadres heeft.</summary>
    [HttpGet("pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<FileContentResult> AllPdf([FromQuery] int? year, [FromQuery] bool withoutEmail, CancellationToken cancellationToken)
    {
        var (name, content) = await invoices.AllPdfAsync(year ?? await advertisers.CampaignYearAsync(cancellationToken), withoutEmail, cancellationToken);
        return File(content, "application/pdf", name);
    }
}

public sealed record InvoiceSettingsRequest([StringLength(300)] string? Address, [StringLength(12)] string? Kvk);

public sealed record CreateInvoicesRequest(DateOnly Date, [Range(2000, 2100)] int? Year);

public sealed record InvoiceCountResponse(int Count);
