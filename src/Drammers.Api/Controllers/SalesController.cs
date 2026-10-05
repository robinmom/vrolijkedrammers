using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authentication;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Payments;
using Drammers.Infrastructure.Sales;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Controllers;

/// <summary>
/// Kaartverkoop (fase 19) voor iedereen, ook zonder account: in de app en op de webpagina. Optioneel ingelogd: een lid
/// ziet de gratis groepskaarten voor de pronkzitting en kan munten kopen. De prijs rekent de server uit; betalen gaat
/// via Mollie (iDEAL). Een gast opent de bestelling met het geheime token uit de e-mail of na het betalen.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/sales")]
public sealed class SalesController(TicketSales sales, IOptions<AuthOptions> authOptions) : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    private async Task<SaleBuyer> BuyerAsync()
    {
        if (HttpContext.User.Identity?.IsAuthenticated != true
            || !EnvironmentAccess.IsAllowed(HttpContext.User, authOptions.Value.EnvironmentAccessClaim, authOptions.Value.RequiredEnvironmentAccess))
        {
            return SaleBuyer.Guest;
        }

        var user = await CurrentUser.ResolveAsync(HttpContext);
        return user is { Status: AccountStatus.Active } ? new SaleBuyer(user.UserId, user.MemberId) : SaleBuyer.Guest;
    }

    /// <summary>Wat er nu te koop is, met de plaatsen die nog vrij zijn. Voor een ingelogd lid ook de groepskaarten.</summary>
    [HttpGet("products")]
    [ProducesResponseType<SaleCatalogResponse>(StatusCodes.Status200OK)]
    public async Task<SaleCatalogResponse> Products(CancellationToken cancellationToken)
    {
        var buyer = await BuyerAsync();
        var products = await sales.OnSaleAsync(cancellationToken);
        var stock = await sales.StockAsync(products, cancellationToken);
        var group = await sales.GroupAllowanceAsync(buyer.MemberId, cancellationToken);
        return new SaleCatalogResponse(
            [.. products.Select(p => SaleProductResponse.From(p, stock[p.Id]))],
            buyer.MemberId is not null, group);
    }

    [HttpPost("orders")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType<OrderCreated>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OrderCreated>> Order(OrderRequest request, CancellationToken cancellationToken)
    {
        var created = await sales.OrderAsync(request.ToInput(), await BuyerAsync(), request.Channel == SaleChannel.App ? SaleChannel.App : SaleChannel.Web,
            BaseUrl, cancellationToken, Request.Headers[DeviceCheck.HeaderName].ToString());
        return Created((string?)null, created);
    }

    /// <summary>De bestelling met de QR; <paramref name="t"/> is het token uit de e-mail of van het bestellen.</summary>
    [HttpGet("orders/{id:guid}")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType<OrderView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<OrderView> Get(Guid id, [FromQuery, Required] string t, CancellationToken cancellationToken) =>
        sales.ViewByTokenAsync(id, t, cancellationToken);

    /// <summary>De QR als afbeelding (SVG) voor de webpagina; geen scriptbibliotheek nodig in de browser.</summary>
    [HttpGet("orders/{id:guid}/tickets/{ticketId:guid}/qr.svg")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousStatusPolicy)]
    [Produces("image/svg+xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> QrImage(Guid id, Guid ticketId, [FromQuery, Required] string t, CancellationToken cancellationToken)
    {
        var code = await sales.TicketCodeByTokenAsync(id, t, ticketId, cancellationToken);
        using var generator = new QRCoder.QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCoder.QRCodeGenerator.ECCLevel.M);
        var svg = new QRCoder.SvgQRCode(data).GetGraphic(8, "#123047", "#FFFFFF", drawQuietZones: true);
        Response.Headers.CacheControl = "no-store";
        return Content(svg, "image/svg+xml");
    }

    /// <summary>De betaallink uit de e-mail: stuurt door naar een nieuwe betaling bij Mollie.</summary>
    [HttpGet("orders/{id:guid}/pay")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType(StatusCodes.Status302Found)]
    public async Task<IActionResult> Pay(Guid id, [FromQuery, Required] string t, CancellationToken cancellationToken)
    {
        try
        {
            return Redirect(await sales.PayAsync(id, t, BaseUrl, cancellationToken));
        }
        catch (DomainException ex) when (ex.Code is ErrorCodes.OrderNotPayable or ErrorCodes.PaymentFailed or ErrorCodes.PaymentsNotConfigured)
        {
            // Terug naar de bestelpagina, die de status en de reden toont.
            return Redirect($"{TicketSales.OrderLink(BaseUrl, id, t)}&melding={Uri.EscapeDataString(ex.Message)}");
        }
    }

    /// <summary>Op de wachtlijst als het vol is (bijv. de vrijdag van de pronkzitting).</summary>
    [HttpPost("waitlist")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType<WaitlistJoinedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<WaitlistJoinedResponse>> Waitlist(OrderRequest request, CancellationToken cancellationToken)
    {
        var id = await sales.JoinWaitlistAsync(request.ToInput(), await BuyerAsync(), request.Channel == SaleChannel.App ? SaleChannel.App : SaleChannel.Web, cancellationToken);
        return Created((string?)null, new WaitlistJoinedResponse(id));
    }
}

/// <summary>
/// Webhook van Mollie (fase 19): de body bevat alleen het id; de status wordt altijd bij Mollie opgehaald. Onbekende of
/// ongeldige ids krijgen gewoon 200 (geen informatie voor een aanvaller); lukt het ophalen niet, dan 503 zodat Mollie
/// het later opnieuw probeert.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/payments/mollie")]
public sealed class MollieWebhookController(TicketSales sales) : ControllerBase
{
    [HttpPost("webhook")]
    [Consumes("application/x-www-form-urlencoded")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> Webhook([FromForm] string? id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || !id.StartsWith("tr_", StringComparison.Ordinal) || id.Length > 40)
        {
            return Ok();
        }

        try
        {
            await sales.HandleWebhookAsync(id, $"{Request.Scheme}://{Request.Host}", cancellationToken);
            return Ok();
        }
        catch (Exception ex) when (ex is MollieException or HttpRequestException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
    }
}

/// <summary>
/// Mijn bestellingen (fase 19): kaarten en munten van de ingelogde gebruiker, en kaarten uit de groeps-QR delen met een
/// lid van dezelfde groep (fase 19b).
/// </summary>
[ApiController]
[Route("api/v1/me/orders")]
[RequirePermission(Drammers.SharedKernel.Authorization.Permissions.TicketReadOwn)]
public sealed class MeOrdersController(TicketSales sales) : ControllerBase
{
    private Guid MemberId => CurrentUser.Get(HttpContext)!.MemberId
        ?? throw new DomainException(ErrorCodes.MembersOnly, "Delen kan alleen als je als lid bent ingelogd.", DomainErrorKind.Forbidden);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<OrderView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<OrderView>> Mine(CancellationToken cancellationToken)
    {
        var user = CurrentUser.Get(HttpContext)!;
        return sales.MineAsync(user.UserId, user.MemberId, cancellationToken, Request.Headers[DeviceCheck.HeaderName].ToString());
    }

    [HttpGet("tickets/{ticketId:guid}/share-candidates")]
    [ProducesResponseType<IReadOnlyList<ShareCandidate>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<ShareCandidate>> ShareCandidates(Guid ticketId, CancellationToken cancellationToken) =>
        sales.ShareCandidatesAsync(ticketId, MemberId, cancellationToken);

    [HttpPost("tickets/{ticketId:guid}/share")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Share(Guid ticketId, ShareTicketRequest request, CancellationToken cancellationToken)
    {
        await sales.ShareAsync(ticketId, MemberId, request.MemberId, request.Quantity, cancellationToken);
        return NoContent();
    }
}

public sealed record ShareTicketRequest(Guid MemberId, [Range(1, 500)] int Quantity);

public sealed record OrderRequest(
    Guid ProductId,
    [Range(0, 500)] int MemberQuantity,
    [Range(0, 500)] int PaidQuantity,
    [StringLength(200)] string? BuyerName,
    [StringLength(254), EmailAddress] string? BuyerEmail,
    [StringLength(40)] string? BuyerPhone,
    [StringLength(500)] string? Remark,
    SaleChannel Channel = SaleChannel.Web)
{
    public OrderInput ToInput() => new(ProductId, MemberQuantity, PaidQuantity, BuyerName, BuyerEmail, BuyerPhone, Remark);
}

public sealed record WaitlistJoinedResponse(Guid Id);

public sealed record SaleCatalogResponse(IReadOnlyList<SaleProductResponse> Products, bool IsMember, GroupAllowance? Group);

public sealed record SaleProductResponse(
    Guid Id, SaleProductKind Kind, string Name, string? Description, Guid? EventId, DateOnly? Date, int PriceCents, int? Capacity,
    int? Remaining, bool SoldOut, int MaxPerOrder, DateTime? SaleClosesAt, bool MembersOnly, bool GroupOrders)
{
    public static SaleProductResponse From(SaleProduct p, ProductStock stock) => new(
        p.Id, p.Kind, p.Name, p.Description, p.EventId, p.Date, p.PriceCents, p.Capacity, stock.Remaining, stock.SoldOut, p.MaxPerOrder,
        p.SaleClosesAt, p.MembersOnly, p.GroupOrders);
}
