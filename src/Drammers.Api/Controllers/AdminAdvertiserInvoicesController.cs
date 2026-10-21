using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Advertisers.Invoices;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
        await invoices.SetSettingsAsync(
            new InvoiceSettings(request.Address, request.Kvk, request.VatNumber, request.Phone, request.Website, request.ServiceFeeOn, request.ServiceFeeCents),
            cancellationToken);
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

    /// <summary>De factuur voor één adverteerder maken (als die nog ontbreekt).</summary>
    [HttpPost("advertiser/{advertiserId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateOne(Guid advertiserId, CreateInvoicesRequest request, CancellationToken cancellationToken)
    {
        var year = request.Year ?? await advertisers.CampaignYearAsync(cancellationToken);
        await invoices.CreateOneAsync(advertiserId, year, request.Date, cancellationToken);
        return NoContent();
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

    /// <summary>De betaalstatus bij Mollie opnieuw ophalen (fase 27o).</summary>
    [HttpPost("{id:guid}/refresh-payment")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RefreshPayment(Guid id, CancellationToken cancellationToken)
    {
        await invoices.RefreshPaymentAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Een factuur intrekken (de betaallink werkt dan niet meer).</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await invoices.CancelAsync(id, cancellationToken);
        return NoContent();
    }
}

/// <summary>De betaallink uit de factuurmail: stuurt door naar een iDEAL-betaling bij Mollie (fase 27o).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/advertisers/invoices")]
public sealed class AdvertiserInvoicePaymentController(AdvertiserInvoices invoices) : ControllerBase
{
    private string BaseUrl => Request.PublicBaseUrlFor();

    [HttpGet("{id:guid}/pay")]
    [EnableRateLimiting(AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Pay(Guid id, [FromQuery, Required] string t, CancellationToken cancellationToken)
    {
        try
        {
            return Redirect(await invoices.PayAsync(id, t, BaseUrl, cancellationToken));
        }
        catch (Drammers.SharedKernel.Errors.DomainException ex) when (PaymentNotices.Handles(ex))
        {
            return Redirect($"{BaseUrl}/?betaling={PaymentNotices.Code(ex)}");
        }
    }
}

public sealed record InvoiceSettingsRequest(
    [StringLength(300)] string? Address,
    [StringLength(12)] string? Kvk,
    [StringLength(14)] string? VatNumber = null,
    [StringLength(40)] string? Phone = null,
    [StringLength(100)] string? Website = null,
    bool ServiceFeeOn = false,
    [Range(0, 100000)] int ServiceFeeCents = 0);

public sealed record CreateInvoicesRequest(DateOnly Date, [Range(2000, 2100)] int? Year);

public sealed record InvoiceCountResponse(int Count);
