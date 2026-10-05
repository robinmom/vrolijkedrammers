using System.ComponentModel.DataAnnotations;
using System.Globalization;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Sales;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Verkoop in het portal (fase 19, Figma 🎫 Kaartverkoop; één pagina per soort product): producten, bestellingen (contant of met een betaallink),
/// de pronkzitting per avond met de export voor de tafelindeling, de wachtlijst en de munten. Alles met <c>sale.manage</c>.
/// </summary>
[ApiController]
[Route("api/v1/admin/sales")]
[RequirePermission(Permissions.SaleManage)]
public sealed class AdminSalesController(SaleAdministration administration, TicketSales sales, IAuditLogger audit, IClock clock) : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}";

    [HttpGet("summary")]
    [ProducesResponseType<SalesSummary>(StatusCodes.Status200OK)]
    public Task<SalesSummary> Summary([FromQuery] SaleProductKind? kind, CancellationToken cancellationToken) =>
        administration.SummaryAsync(cancellationToken, kind);

    [HttpGet("products")]
    [ProducesResponseType<IReadOnlyList<AdminSaleProductResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminSaleProductResponse>> Products(CancellationToken cancellationToken) =>
        [.. (await administration.ProductsAsync(cancellationToken)).Select(AdminSaleProductResponse.From)];

    [HttpPost("products")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Guid>> CreateProduct(SaleProductRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, (await administration.CreateProductAsync(request.ToInput(), cancellationToken)).Id);

    [HttpPut("products/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateProduct(Guid id, SaleProductRequest request, CancellationToken cancellationToken)
    {
        await administration.UpdateProductAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpGet("orders")]
    [ProducesResponseType<PagedResult<SaleOrderRow>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<SaleOrderRow>> Orders(
        [FromQuery] Guid? productId, [FromQuery] SaleProductKind? kind, [FromQuery] SaleOrderStatus? status, [FromQuery] string? search,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<SaleOrderRow>.Normalize(page, pageSize);
        var (items, total) = await administration.OrdersAsync(productId, status, search, p, size, cancellationToken, kind);
        return new PagedResult<SaleOrderRow>(items, p, size, total);
    }

    /// <summary>
    /// Nieuwe bestelling: gratis groepskaarten (groep uit vrij veld 3) en/of losse kaarten, contant ontvangen of met een
    /// betaallink per e-mail. Zonder groepskaarten is dit ook de losse betaallink voor de vrije verkoop.
    /// </summary>
    [HttpPost("orders")]
    [ProducesResponseType<PortalOrderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PortalOrderResponse>> CreateOrder(PortalOrderRequest request, CancellationToken cancellationToken)
    {
        var created = await sales.PortalOrderAsync(
            new OrderInput(request.ProductId, request.MemberQuantity, request.PaidQuantity, request.BuyerName, request.BuyerEmail, request.BuyerPhone, request.Remark),
            request.GroupName, request.Payment, CurrentUser.Get(HttpContext)!.UserId, BaseUrl, cancellationToken);
        return Created((string?)null, new PortalOrderResponse(created.OrderId, created.Number, created.Status));
    }

    [HttpPost("orders/{id:guid}/paid-cash")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PaidCash(Guid id, CancellationToken cancellationToken)
    {
        await sales.MarkPaidCashAsync(id, BaseUrl, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders/{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancelOrderRequest request, CancellationToken cancellationToken)
    {
        await sales.CancelAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Munten: aan welk toestel ze gekoppeld zijn en naar welke toestellen van het lid ze kunnen.</summary>
    [HttpGet("orders/{id:guid}/token-device")]
    [ProducesResponseType<TokenDevice>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<TokenDevice> TokenDevice(Guid id, CancellationToken cancellationToken) => administration.TokenDeviceAsync(id, cancellationToken);

    /// <summary>Munten één keer naar een ander toestel van het lid verplaatsen (met reden, in de auditlog).</summary>
    [HttpPost("orders/{id:guid}/token-device")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> MoveTokens(Guid id, MoveTokensRequest request, CancellationToken cancellationToken)
    {
        await administration.MoveTokensAsync(id, request.DeviceId, request.Reason, CurrentUser.Get(HttpContext)!.UserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("orders/{id:guid}/resend-link")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResendLink(Guid id, CancellationToken cancellationToken)
    {
        await sales.ResendPaymentLinkAsync(id, BaseUrl, cancellationToken);
        return NoContent();
    }

    /// <summary>Groepen (vrij veld 3) met hoeveel gratis kaarten ze nog kunnen bestellen, voor het bestelformulier.</summary>
    [HttpGet("groups")]
    [ProducesResponseType<IReadOnlyList<GroupAllowance>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<GroupAllowance>> Groups([FromServices] DrammersDbContext db, CancellationToken cancellationToken)
    {
        var names = await db.Members.AsNoTracking()
            .Where(m => m.ParadeGroupName != null && m.ParadeGroupName != "" && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
            .Select(m => m.ParadeGroupName!).Distinct().ToListAsync(cancellationToken);
        var result = new List<GroupAllowance>();
        foreach (var name in names.Order(StringComparer.CurrentCultureIgnoreCase))
        {
            if (await sales.AllowanceForGroupAsync(name, cancellationToken) is { } allowance)
            {
                result.Add(allowance);
            }
        }

        return result;
    }

    [HttpGet("products/{id:guid}/waitlist")]
    [ProducesResponseType<IReadOnlyList<WaitlistRow>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<WaitlistRow>> Waitlist(Guid id, CancellationToken cancellationToken) => administration.WaitlistAsync(id, cancellationToken);

    /// <summary>Plaatsen toekennen aan iemand op de wachtlijst (uitnodigen met een betaallink, of contant), ook buiten de volgorde.</summary>
    [HttpPost("waitlist/{id:guid}/grant")]
    [ProducesResponseType<PortalOrderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<PortalOrderResponse> Grant(Guid id, GrantWaitlistRequest request, CancellationToken cancellationToken)
    {
        var created = await sales.GrantWaitlistAsync(id, request.Payment, CurrentUser.Get(HttpContext)!.UserId, BaseUrl, cancellationToken);
        return new PortalOrderResponse(created.OrderId, created.Number, created.Status);
    }

    [HttpDelete("waitlist/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken cancellationToken)
    {
        await sales.WithdrawWaitlistAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("pronkzitting")]
    [ProducesResponseType<IReadOnlyList<Evening>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<Evening>> Pronkzitting(CancellationToken cancellationToken) => administration.EveningsAsync(cancellationToken);

    /// <summary>Export voor de tafelindeling: één tabblad per avond.</summary>
    [HttpGet("pronkzitting/export")]
    [Produces(AdminParadeExchangeController.XlsxContentType)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> PronkzittingExport(CancellationToken cancellationToken)
    {
        var evenings = await administration.EveningsAsync(cancellationToken);
        using var workbook = new XLWorkbook();
        string[] headers = ["Avond", "Groep / naam", "Aantal", "Besteller", "Telefoon", "E-mail", "Lid / niet-lid", "Betaald", "Opmerking / wensen", "Bestelnummers"];
        double[] widths = [22, 30, 8, 30, 16, 32, 22, 22, 45, 24];
        var dutch = CultureInfo.GetCultureInfo("nl-NL");
        foreach (var evening in evenings.DefaultIfEmpty())
        {
            var label = evening is null ? "Pronkzitting" : evening.Date?.ToString("dddd d MMMM", dutch) ?? evening.Name;
            var sheet = workbook.AddWorksheet(SheetName(label, workbook));
            for (var c = 0; c < headers.Length; c++)
            {
                sheet.Cell(1, c + 1).Value = headers[c];
                sheet.Column(c + 1).Width = widths[c];
            }

            sheet.Row(1).Style.Font.Bold = true;
            var row = 2;
            foreach (var r in evening?.Rows ?? [])
            {
                object?[] values = [label, r.Name, r.Quantity, r.Orderers, r.Phones, r.Emails, r.Membership, r.Paid, r.Remarks, string.Join(", ", r.OrderNumbers)];
                for (var c = 0; c < values.Length; c++)
                {
                    sheet.Cell(row, c + 1).Value = XLCellValue.FromObject(values[c]);
                }

                row++;
            }

            sheet.Cell(row, 2).Value = "Totaal";
            sheet.Cell(row, 3).Value = evening?.Rows.Sum(r => r.Quantity) ?? 0;
            sheet.Row(row).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            if (row > 2)
            {
                sheet.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        await audit.WriteAsync(new AuditEntry("sale.pronkzitting-exported", "SaleProduct", "pronkzitting"), cancellationToken);
        var today = TimeZoneInfo.ConvertTimeFromUtc(clock.UtcNow.UtcDateTime, TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam"));
        return File(stream.ToArray(), AdminParadeExchangeController.XlsxContentType,
            $"Pronkzitting tafelindeling {today.ToString("d-M-yyyy", CultureInfo.InvariantCulture)}.xlsx");
    }

    private static string SheetName(string label, XLWorkbook workbook)
    {
        var name = new string([.. label.Where(ch => ch is not ('[' or ']' or '*' or '?' or '/' or '\\' or ':'))]);
        name = name.Length > 31 ? name[..31] : name;
        var candidate = name;
        for (var i = 2; workbook.Worksheets.Contains(candidate); i++)
        {
            candidate = $"{name[..Math.Min(name.Length, 28)]} {i}";
        }

        return candidate;
    }

    /// <summary>Kassalog van één dag (standaard vandaag): elke scan van een munten-QR en elke uitgifte.</summary>
    [HttpGet("kassalog")]
    [ProducesResponseType<KassaDay>(StatusCodes.Status200OK)]
    public Task<KassaDay> KassaLog([FromServices] TokenCollection kassa, [FromQuery] DateOnly? day, CancellationToken cancellationToken) =>
        kassa.LogAsync(day, cancellationToken);

    [HttpGet("tokens")]
    [ProducesResponseType<IReadOnlyList<SaleOrderRow>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SaleOrderRow>> Tokens(CancellationToken cancellationToken) => administration.TokensAsync(cancellationToken);
}

public sealed record SaleProductRequest(
    SaleProductKind Kind,
    [Required, StringLength(120)] string Name,
    [StringLength(1000)] string? Description,
    Guid? EventId,
    DateOnly? Date,
    [Range(0, 100_000)] int PriceCents,
    [Range(0, 100_000)] int? Capacity,
    [Range(1, 500)] int MaxPerOrder,
    DateTime? SaleOpensAt,
    DateTime? SaleClosesAt,
    bool OnSale,
    int SortOrder)
{
    public SaleProductInput ToInput() => new(Kind, Name, Description, EventId, Date, PriceCents, Capacity, MaxPerOrder,
        SaleOpensAt?.ToUniversalTime(), SaleClosesAt?.ToUniversalTime(), OnSale, SortOrder);
}

public sealed record AdminSaleProductResponse(
    Guid Id, SaleProductKind Kind, string Name, string? Description, Guid? EventId, DateOnly? Date, int PriceCents, int? Capacity, int MaxPerOrder,
    DateTime? SaleOpensAt, DateTime? SaleClosesAt, bool OnSale, int SortOrder, int Sold, int Held, int? Remaining, int RevenueCents, int Waiting)
{
    public static AdminSaleProductResponse From(SaleProductRow r) => new(
        r.Product.Id, r.Product.Kind, r.Product.Name, r.Product.Description, r.Product.EventId, r.Product.Date, r.Product.PriceCents, r.Product.Capacity,
        r.Product.MaxPerOrder, r.Product.SaleOpensAt, r.Product.SaleClosesAt, r.Product.OnSale, r.Product.SortOrder, r.Stock.Sold, r.Stock.Held,
        r.Stock.Remaining, r.RevenueCents, r.Waiting);
}

public sealed record PortalOrderRequest(
    Guid ProductId,
    [StringLength(100)] string? GroupName,
    [Range(0, 500)] int MemberQuantity,
    [Range(0, 500)] int PaidQuantity,
    [Required, StringLength(200)] string BuyerName,
    [Required, StringLength(254), EmailAddress] string BuyerEmail,
    [StringLength(40)] string? BuyerPhone,
    [StringLength(500)] string? Remark,
    PortalPayment Payment);

public sealed record PortalOrderResponse(Guid Id, string Number, SaleOrderStatus Status);

public sealed record CancelOrderRequest([StringLength(500)] string? Reason);

public sealed record MoveTokensRequest(Guid DeviceId, [Required, StringLength(500, MinimumLength = 5)] string Reason);

public sealed record GrantWaitlistRequest(PortalPayment Payment);
