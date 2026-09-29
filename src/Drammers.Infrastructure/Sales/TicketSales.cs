using System.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Email;
using Drammers.Infrastructure.Payments;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Ticketing;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Notifications;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Sales;

/// <summary>Wie bestelt: een ingelogde gebruiker (eventueel lid) of een gast.</summary>
public sealed record SaleBuyer(Guid? UserId, Guid? MemberId)
{
    public static readonly SaleBuyer Guest = new(null, null);
}

/// <summary>Een bestelling uit de app, de webpagina of het portal. De prijs rekent de server uit.</summary>
public sealed record OrderInput(
    Guid ProductId, int MemberQuantity, int PaidQuantity, string? BuyerName, string? BuyerEmail, string? BuyerPhone, string? Remark);

public sealed record OrderCreated(Guid OrderId, string Number, string Token, SaleOrderStatus Status, string? CheckoutUrl);

/// <summary>Kaarten die een groep (vrij veld 3) nog gratis kan bestellen: actieve leden min wat al besteld is (beide avonden).</summary>
public sealed record GroupAllowance(string GroupName, int ActiveMembers, int Ordered, int Remaining);

/// <summary>Stand van een product: verkocht (betaald of gratis), vastgehouden voor een openstaande betaling, nog vrij.</summary>
public sealed record ProductStock(int Sold, int Held, int? Remaining, bool SoldOut);

/// <summary>De bestelling zoals de koper hem ziet (na betalen of via de link in de e-mail).</summary>
public sealed record OrderView(
    Guid Id, string Number, SaleOrderStatus Status, SaleProductKind Kind, string ProductName, DateOnly? Date, string? GroupName,
    int MemberQuantity, int PaidQuantity, int AmountCents, string BuyerName, DateTime CreatedAt, DateTime? HoldUntil,
    IReadOnlyList<OrderTicketView> Tickets);

/// <summary>Een QR bij de bestelling; <see cref="Code"/> alleen zolang hij geldig is (niet bij munten: die gaan via de munten-QR).</summary>
public sealed record OrderTicketView(Guid Id, int Quantity, OrderTicketStatus Status, string? Code);

public enum PortalPayment
{
    /// <summary>Contant ontvangen: de kaarten zijn direct geldig.</summary>
    Cash,

    /// <summary>Betaallink per e-mail (48 uur geldig).</summary>
    PaymentLink,
}

/// <summary>
/// Kaartverkoop (fase 19): bestellen, betalen via Mollie, contant of een betaallink in het portal, en de QR uitgeven.
/// Nooit meer verkopen dan de capaciteit: de productregel wordt tijdens het bestellen vergrendeld (UPDLOCK) en een
/// onbetaalde bestelling houdt de plaatsen vast tot <see cref="SaleOrder.HoldUntil"/>. Groepskaarten voor de
/// pronkzitting zijn gratis voor leden van de groep, tot het aantal actieve leden (beide avonden samen); dat is geen
/// reservering. Nooit terugbetalen: annuleren maakt alleen de QR ongeldig.
/// </summary>
public sealed partial class TicketSales(
    DrammersDbContext db, IMollieClient mollie, IEmailSender email, INotificationService notifications, TicketSigningKeys keys,
    IDataProtectionProvider protection, IAuditLogger audit, IClock clock, ILogger<TicketSales> logger)
{
    private readonly IDataProtector _tokens = protection.CreateProtector("Drammers.SaleOrderToken.v1");

    /// <summary>Zo lang houdt een bestelling in de app of op de webpagina de plaatsen vast tijdens het betalen.</summary>
    public static readonly TimeSpan CheckoutHold = TimeSpan.FromMinutes(30);

    /// <summary>Zo lang is een betaallink (portal, uitnodiging van de wachtlijst) geldig.</summary>
    public static readonly TimeSpan LinkHold = TimeSpan.FromHours(WaitlistEntry.InviteHours);

    private DateTime Now => clock.UtcNow.UtcDateTime;

    // ---- Catalogus -------------------------------------------------------------------------------------------------

    /// <summary>Producten van het actieve carnavalsjaar die nu te koop zijn.</summary>
    public async Task<IReadOnlyList<SaleProduct>> OnSaleAsync(CancellationToken cancellationToken)
    {
        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        if (year is null)
        {
            return [];
        }

        var now = Now;
        return await db.SaleProducts.AsNoTracking()
            .Where(p => p.CarnivalYearId == year && p.OnSale && (p.SaleOpensAt == null || p.SaleOpensAt <= now) && (p.SaleClosesAt == null || p.SaleClosesAt > now))
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Date).ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public static bool IsOnSale(SaleProduct p, DateTime now) =>
        p.OnSale && (p.SaleOpensAt is null || p.SaleOpensAt <= now) && (p.SaleClosesAt is null || p.SaleClosesAt > now);

    /// <summary>Verkocht en vastgehouden per product (één query voor een hele lijst).</summary>
    public async Task<IReadOnlyDictionary<Guid, ProductStock>> StockAsync(IReadOnlyCollection<SaleProduct> products, CancellationToken cancellationToken)
    {
        var ids = products.Select(p => p.Id).ToList();
        var now = Now;
        var rows = await db.SaleOrders.AsNoTracking()
            .Where(o => ids.Contains(o.ProductId) && (o.Status == SaleOrderStatus.Confirmed || (o.Status == SaleOrderStatus.AwaitingPayment && o.HoldUntil > now)))
            .GroupBy(o => new { o.ProductId, o.Status })
            .Select(g => new { g.Key.ProductId, g.Key.Status, Quantity = g.Sum(o => o.MemberQuantity + o.PaidQuantity) })
            .ToListAsync(cancellationToken);
        return products.ToDictionary(p => p.Id, p =>
        {
            var sold = rows.Where(r => r.ProductId == p.Id && r.Status == SaleOrderStatus.Confirmed).Sum(r => r.Quantity);
            var held = rows.Where(r => r.ProductId == p.Id && r.Status == SaleOrderStatus.AwaitingPayment).Sum(r => r.Quantity);
            int? remaining = p.Capacity is { } c ? Math.Max(0, c - sold - held) : null;
            return new ProductStock(sold, held, remaining, remaining == 0);
        });
    }

    /// <summary>De groep van het lid (vrij veld 3) met hoeveel kaarten de groep nog gratis kan bestellen.</summary>
    public async Task<GroupAllowance?> GroupAllowanceAsync(Guid? memberId, CancellationToken cancellationToken)
    {
        if (memberId is null)
        {
            return null;
        }

        var member = await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => new { m.ParadeGroupName, Status = m.LocalStatusOverride ?? m.MembershipStatus }).SingleOrDefaultAsync(cancellationToken);
        if (member is not { Status: MembershipStatus.Active } || string.IsNullOrWhiteSpace(member.ParadeGroupName))
        {
            return null;
        }

        return await AllowanceForGroupAsync(member.ParadeGroupName, cancellationToken);
    }

    public async Task<GroupAllowance?> AllowanceForGroupAsync(string groupName, CancellationToken cancellationToken)
    {
        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        if (year is null)
        {
            return null;
        }

        var name = groupName.Trim();
        var active = await db.Members.AsNoTracking().CountAsync(m => m.ParadeGroupName == name && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, cancellationToken);
        var now = Now;
        var ordered = await (
            from o in db.SaleOrders.AsNoTracking()
            join p in db.SaleProducts.AsNoTracking() on o.ProductId equals p.Id
            where o.CarnivalYearId == year && p.Kind == SaleProductKind.Pronkzitting && o.GroupName == name
                && (o.Status == SaleOrderStatus.Confirmed || (o.Status == SaleOrderStatus.AwaitingPayment && o.HoldUntil > now))
            select o.MemberQuantity).SumAsync(cancellationToken);
        return new GroupAllowance(name, active, ordered, Math.Max(0, active - ordered));
    }

    // ---- Bestellen -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Bestellen in de app of op de webpagina. Gratis groepskaarten alleen voor een ingelogd lid van de groep; munten
    /// alleen voor leden. Betaalde kaarten gaan altijd via Mollie (iDEAL): het antwoord bevat de link naar de betaalpagina.
    /// </summary>
    public async Task<OrderCreated> OrderAsync(OrderInput input, SaleBuyer buyer, SaleChannel channel, string baseUrl, CancellationToken cancellationToken)
    {
        var member = buyer.MemberId is { } memberId
            ? await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, cancellationToken)
            : null;
        var user = buyer.UserId is { } userId ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) : null;
        var name = Clean(input.BuyerName) ?? member?.FullName ?? user?.DisplayName;
        var mail = Clean(input.BuyerEmail) ?? member?.Email ?? user?.Email;
        var draft = new OrderDraft(
            input.ProductId, input.MemberQuantity > 0 ? member?.ParadeGroupName?.Trim() : null, input.MemberQuantity, input.PaidQuantity,
            name, mail, Clean(input.BuyerPhone) ?? member?.MobilePhone ?? member?.Phone, Clean(input.Remark), user?.Id, member?.Id, channel);

        if (input.MemberQuantity > 0 && member is null)
        {
            throw new DomainException(ErrorCodes.MembersOnly, "Log in als lid om gratis kaarten voor je groep te bestellen.", DomainErrorKind.Forbidden);
        }

        if (input.MemberQuantity > 0 && draft.GroupName is null)
        {
            throw new DomainException(ErrorCodes.GroupLimit, "Je staat bij geen groep ingeschreven; bestel losse kaarten of neem contact op met het bestuur.");
        }

        var (order, product) = await PlaceAsync(draft, SalePaymentMethod.Mollie, CheckoutHold, createdBy: null, cancellationToken);
        var token = await FinishNewOrderAsync(order, product, baseUrl, sendPaymentLink: false, cancellationToken);
        string? checkout = null;
        if (order.Status == SaleOrderStatus.AwaitingPayment)
        {
            checkout = await StartPaymentAsync(order, product, token, baseUrl, cancellationToken);
        }

        return new OrderCreated(order.Id, order.Number, token, order.Status, checkout);
    }

    /// <summary>
    /// Bestelling in het portal: gratis groepskaarten (een groep naar keuze), losse kaarten contant of met een betaallink
    /// per e-mail. Ook voor de vrije verkoop: alleen een betaallink voor een aantal kaarten.
    /// </summary>
    public async Task<OrderCreated> PortalOrderAsync(
        OrderInput input, string? groupName, PortalPayment payment, Guid createdBy, string baseUrl, CancellationToken cancellationToken)
    {
        var draft = new OrderDraft(
            input.ProductId, input.MemberQuantity > 0 ? Clean(groupName) : null, input.MemberQuantity, input.PaidQuantity,
            Clean(input.BuyerName), Clean(input.BuyerEmail), Clean(input.BuyerPhone), Clean(input.Remark), null, null, SaleChannel.Portal);
        if (input.MemberQuantity > 0 && draft.GroupName is null)
        {
            throw Invalid("Kies de groep voor de gratis groepskaarten.");
        }

        var method = payment == PortalPayment.Cash ? SalePaymentMethod.Cash : SalePaymentMethod.Mollie;
        var (order, product) = await PlaceAsync(draft, method, LinkHold, createdBy, cancellationToken);
        var token = await FinishNewOrderAsync(order, product, baseUrl, sendPaymentLink: true, cancellationToken);
        return new OrderCreated(order.Id, order.Number, token, order.Status, null);
    }

    private sealed record OrderDraft(
        Guid ProductId, string? GroupName, int MemberQuantity, int PaidQuantity, string? BuyerName, string? BuyerEmail, string? BuyerPhone,
        string? Remark, Guid? UserId, Guid? MemberId, SaleChannel Channel, Guid? WaitlistEntryId = null);

    /// <summary>Controleert en boekt de bestelling in één transactie met de productregel vergrendeld.</summary>
    private async Task<(SaleOrder Order, SaleProduct Product)> PlaceAsync(
        OrderDraft draft, SalePaymentMethod paidMethod, TimeSpan hold, Guid? createdBy, CancellationToken cancellationToken, bool ignoreSaleWindow = false)
    {
        if (draft.MemberQuantity < 0 || draft.PaidQuantity < 0 || draft.MemberQuantity + draft.PaidQuantity == 0)
        {
            throw Invalid("Kies hoeveel kaarten je wilt bestellen.");
        }

        if (string.IsNullOrWhiteSpace(draft.BuyerName) || draft.BuyerName.Length > 200)
        {
            throw Invalid("Vul je naam in.");
        }

        if (draft.BuyerEmail is null || draft.BuyerEmail.Length > 254 || !draft.BuyerEmail.Contains('@', StringComparison.Ordinal))
        {
            throw Invalid("Vul een geldig e-mailadres in.");
        }

        if (draft.Remark?.Length > 500 || draft.BuyerPhone?.Length > 40)
        {
            throw Invalid("De opmerking mag hooguit 500 tekens zijn en het telefoonnummer hooguit 40.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        await LockProductAsync(draft.ProductId, cancellationToken);
        var product = await db.SaleProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == draft.ProductId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ProductNotFound, "Dit product bestaat niet (meer).", DomainErrorKind.NotFound);
        var portal = draft.Channel == SaleChannel.Portal;
        if (!ignoreSaleWindow && !portal && !IsOnSale(product, Now))
        {
            throw new DomainException(ErrorCodes.ProductNotOnSale, $"{product.Name} is nu niet te koop.", DomainErrorKind.Conflict);
        }

        if (draft.MemberQuantity > 0 && !product.GroupOrders)
        {
            throw Invalid("Gratis groepskaarten zijn er alleen voor de pronkzitting.");
        }

        if (product.MembersOnly && draft.MemberId is null && !portal)
        {
            throw new DomainException(ErrorCodes.MembersOnly, "Munten zijn alleen voor leden: log in om munten te kopen. De aankoop is persoonsgebonden.", DomainErrorKind.Forbidden);
        }

        if (draft.PaidQuantity > product.MaxPerOrder && !portal)
        {
            throw Invalid($"Je kunt hooguit {product.MaxPerOrder} per bestelling kopen.");
        }

        if (draft.GroupName is { } group)
        {
            await LockGroupAsync(product.CarnivalYearId, group, cancellationToken);
            var allowance = await AllowanceForGroupAsync(group, cancellationToken);
            if (allowance is null || draft.MemberQuantity > allowance.Remaining)
            {
                throw new DomainException(ErrorCodes.GroupLimit,
                    $"De {group} kan nog {allowance?.Remaining ?? 0} gratis kaart(en) bestellen ({allowance?.ActiveMembers ?? 0} actieve leden, {allowance?.Ordered ?? 0} al besteld, beide avonden samen).",
                    DomainErrorKind.Conflict);
            }
        }

        var quantity = draft.MemberQuantity + draft.PaidQuantity;
        if (product.Capacity is { } capacity)
        {
            var stock = (await StockAsync([product], cancellationToken))[product.Id];
            if (stock.Remaining < quantity)
            {
                throw new DomainException(ErrorCodes.SoldOut,
                    stock.Remaining == 0
                        ? $"{product.Name} is vol."
                        : $"Er zijn nog {stock.Remaining} plaats(en) voor {product.Name}; je vroeg er {quantity}.",
                    DomainErrorKind.Conflict);
            }
        }

        var year = await db.CarnivalYears.AsNoTracking().SingleAsync(y => y.Id == product.CarnivalYearId, cancellationToken);
        var amount = draft.PaidQuantity * product.PriceCents;
        var order = new SaleOrder
        {
            Id = IdGenerator.NewId(),
            Number = $"{year.CarnivalStartDate.Year}-{await NextNumberAsync(year.Id, cancellationToken):D4}",
            CarnivalYearId = year.Id,
            ProductId = product.Id,
            Channel = draft.Channel,
            GroupName = draft.GroupName,
            MemberQuantity = draft.MemberQuantity,
            PaidQuantity = draft.PaidQuantity,
            AmountCents = amount,
            BuyerName = draft.BuyerName.Trim(),
            BuyerEmail = draft.BuyerEmail.Trim(),
            BuyerPhone = draft.BuyerPhone,
            Remark = draft.Remark,
            BuyerUserId = draft.UserId,
            BuyerMemberId = draft.MemberId,
            AccessTokenProtected = "",
            WaitlistEntryId = draft.WaitlistEntryId,
            CreatedAt = Now,
            CreatedByUserId = createdBy,
        };
        if (amount == 0 || paidMethod == SalePaymentMethod.Cash)
        {
            order.PaymentMethod = amount == 0 ? SalePaymentMethod.Free : SalePaymentMethod.Cash;
            order.Status = SaleOrderStatus.Confirmed;
            order.PaidAt = amount == 0 ? null : Now;
            await IssueTicketAsync(order, cancellationToken);
        }
        else
        {
            order.PaymentMethod = SalePaymentMethod.Mollie;
            order.Status = SaleOrderStatus.AwaitingPayment;
            order.HoldUntil = Now + hold;
        }

        db.SaleOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale-order.created", "SaleOrder", order.Id.ToString(), null, JsonSerializer.Serialize(new
        {
            order.Number,
            product = product.Name,
            order.GroupName,
            order.MemberQuantity,
            order.PaidQuantity,
            order.AmountCents,
            status = order.Status.ToString(),
            method = order.PaymentMethod.ToString(),
            channel = order.Channel.ToString(),
        })), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Latere stappen (token, betaling) werken met ExecuteUpdate; de bestelling niet meer volgen voorkomt een
        // concurrency-fout op de rowversion bij de volgende SaveChanges.
        db.ChangeTracker.Clear();
        return (order, product);
    }

    /// <summary>Token voor de koper en de e-mail: bevestiging met de kaarten, of (portal/wachtlijst) de betaallink.</summary>
    private async Task<string> FinishNewOrderAsync(SaleOrder order, SaleProduct product, string baseUrl, bool sendPaymentLink, CancellationToken cancellationToken)
    {
        var token = NewToken();
        var protectedToken = _tokens.Protect(token);
        await db.SaleOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.AccessTokenProtected, protectedToken), cancellationToken);
        order.AccessTokenProtected = protectedToken;
        if (order.Status == SaleOrderStatus.Confirmed)
        {
            await SendAsync(SaleMails.Confirmation(order, product, OrderLink(baseUrl, order.Id, token)), cancellationToken);
        }
        else if (sendPaymentLink)
        {
            await SendAsync(SaleMails.PaymentLink(order, product, PayLink(baseUrl, order.Id, token), invitedFromWaitlist: order.WaitlistEntryId is not null), cancellationToken);
        }

        return token;
    }

    /// <summary>Nieuwe Mollie-betaling voor een openstaande bestelling; geeft de link naar de betaalpagina.</summary>
    private async Task<string> StartPaymentAsync(SaleOrder order, SaleProduct product, string token, string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            var payment = await mollie.CreatePaymentAsync(new MollieNewPayment(
                order.AmountCents, $"{product.Name} · bestelling {order.Number}", OrderLink(baseUrl, order.Id, token), WebhookUrl(baseUrl), order.Id,
                $"{order.Id:N}-{Now.Ticks}"), cancellationToken);
            await db.SaleOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.MolliePaymentId, payment.Id), cancellationToken);
            order.MolliePaymentId = payment.Id;
            return payment.CheckoutUrl ?? throw new MollieException("Geen betaalpagina van Mollie.");
        }
        catch (Exception ex) when (ex is MollieException or HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            LogMollieFailed(logger, order.Number, ex);
            // De plaatsen direct weer vrijgeven als dit een nieuwe bestelling uit de app of van de webpagina was.
            if (order.Channel != SaleChannel.Portal && order.WaitlistEntryId is null)
            {
                await db.SaleOrders.Where(o => o.Id == order.Id && o.Status == SaleOrderStatus.AwaitingPayment)
                    .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, SaleOrderStatus.Expired), cancellationToken);
            }

            throw new DomainException(ErrorCodes.PaymentFailed, "Betalen lukt nu niet. Probeer het over een paar minuten opnieuw.", DomainErrorKind.Conflict);
        }
    }

    /// <summary>De betaallink uit de e-mail: maakt een nieuwe betaling en stuurt door naar Mollie.</summary>
    public async Task<string> PayAsync(Guid orderId, string token, string baseUrl, CancellationToken cancellationToken)
    {
        var order = await OrderByTokenAsync(orderId, token, cancellationToken);
        if (order.Status != SaleOrderStatus.AwaitingPayment || order.HoldUntil <= Now)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable,
                order.Status == SaleOrderStatus.Confirmed ? "Deze bestelling is al betaald." : "Deze betaallink is verlopen.", DomainErrorKind.Conflict);
        }

        // Een betaling die al loopt of net gelukt is, eerst bij Mollie nakijken.
        if (order.MolliePaymentId is { } previous && await SyncPaymentAsync(previous, cancellationToken, baseUrl) is { IsPaid: true })
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Deze bestelling is al betaald.", DomainErrorKind.Conflict);
        }

        var product = await db.SaleProducts.AsNoTracking().SingleAsync(p => p.Id == order.ProductId, cancellationToken);
        return await StartPaymentAsync(order, product, token, baseUrl, cancellationToken);
    }

    // ---- Betalingen ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Webhook van Mollie: bevat alleen het id; de status komt altijd van Mollie zelf (een vervalste "paid" geeft niets).
    /// Onbekende ids worden genegeerd.
    /// </summary>
    public async Task HandleWebhookAsync(string paymentId, string baseUrl, CancellationToken cancellationToken)
    {
        if (!await db.SaleOrders.AnyAsync(o => o.MolliePaymentId == paymentId, cancellationToken))
        {
            return;
        }

        await SyncPaymentAsync(paymentId, cancellationToken, baseUrl);
    }

    /// <summary>Haalt de betaling op bij Mollie en werkt de bestelling bij (idempotent).</summary>
    private async Task<MolliePayment?> SyncPaymentAsync(string paymentId, CancellationToken cancellationToken, string? baseUrl = null)
    {
        var payment = await mollie.GetPaymentAsync(paymentId, cancellationToken);
        var order = await db.SaleOrders.SingleOrDefaultAsync(o => o.MolliePaymentId == paymentId, cancellationToken);
        if (order is null || payment.OrderId != order.Id)
        {
            return payment;
        }

        if (payment.IsPaid && order.Status is SaleOrderStatus.AwaitingPayment or SaleOrderStatus.Expired)
        {
            if (payment.AmountCents != order.AmountCents)
            {
                LogAmountMismatch(logger, order.Number);
                return payment;
            }

            var late = order.Status == SaleOrderStatus.Expired;
            order.Status = SaleOrderStatus.Confirmed;
            order.PaidAt = payment.PaidAt ?? Now;
            await IssueTicketAsync(order, cancellationToken);
            await MarkWaitlistAsync(order, WaitlistStatus.Granted, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("sale-order.paid", "SaleOrder", order.Id.ToString(), null,
                JsonSerializer.Serialize(new { order.Number, paymentId, order.AmountCents, afterExpiry = late })), cancellationToken);
            var product = await db.SaleProducts.AsNoTracking().SingleAsync(p => p.Id == order.ProductId, cancellationToken);
            if (baseUrl is not null)
            {
                await SendConfirmationAsync(order, product, baseUrl, cancellationToken);
            }
        }
        else if (payment.IsFinallyUnpaid && order.Status == SaleOrderStatus.AwaitingPayment && order.WaitlistEntryId is null && order.Channel != SaleChannel.Portal)
        {
            // Mislukt of afgebroken in de app/op de webpagina: plaatsen direct vrij. Betaallinks blijven geldig tot ze verlopen.
            order.Status = SaleOrderStatus.Expired;
            await db.SaveChangesAsync(cancellationToken);
        }

        return payment;
    }

    private async Task SendConfirmationAsync(SaleOrder order, SaleProduct product, string baseUrl, CancellationToken cancellationToken)
    {
        await SendAsync(SaleMails.Confirmation(order, product, OrderLink(baseUrl, order.Id, _tokens.Unprotect(order.AccessTokenProtected))), cancellationToken);
        if (order.BuyerUserId is { } userId)
        {
            await notifications.EnqueueAsync(new SystemNotification(
                "Je kaarten staan klaar", $"{product.Name}: {order.Quantity} kaart(en), bestelling {order.Number}.",
                NotificationCategory.Tickets, new NotificationAudience(UserIds: [userId]), "drammers://kaarten"), cancellationToken);
        }
    }

    /// <summary>Onbetaalde bestellingen na de vasthoudtijd: eerst bij Mollie nakijken, dan de plaatsen vrijgeven.</summary>
    public async Task<int> ExpireAsync(string? baseUrl, CancellationToken cancellationToken)
    {
        var now = Now;
        var due = await db.SaleOrders.AsNoTracking()
            .Where(o => o.Status == SaleOrderStatus.AwaitingPayment && o.HoldUntil <= now)
            .Select(o => new { o.Id, o.MolliePaymentId, o.HoldUntil }).Take(200).ToListAsync(cancellationToken);
        var expired = 0;
        foreach (var item in due)
        {
            if (item.MolliePaymentId is { } paymentId)
            {
                var payment = await SyncPaymentAsync(paymentId, cancellationToken, baseUrl);
                // Nog bezig bij de bank: een kwartier respijt na de vasthoudtijd.
                if (payment is { Status: "open" or "pending" or "authorized" } && item.HoldUntil > now - TimeSpan.FromMinutes(15))
                {
                    continue;
                }
            }

            var order = await db.SaleOrders.SingleAsync(o => o.Id == item.Id, cancellationToken);
            if (order.Status != SaleOrderStatus.AwaitingPayment)
            {
                continue;
            }

            order.Status = SaleOrderStatus.Expired;
            await MarkWaitlistAsync(order, WaitlistStatus.Expired, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            expired++;
        }

        return expired;
    }

    // ---- Inzien ----------------------------------------------------------------------------------------------------

    public async Task<OrderView> ViewByTokenAsync(Guid orderId, string token, CancellationToken cancellationToken) =>
        await ViewAsync(await OrderByTokenAsync(orderId, token, cancellationToken), cancellationToken);

    public async Task<IReadOnlyList<OrderView>> MineAsync(Guid userId, Guid? memberId, CancellationToken cancellationToken)
    {
        var orders = await db.SaleOrders.AsNoTracking()
            .Where(o => (o.BuyerUserId == userId || (memberId != null && o.BuyerMemberId == memberId))
                && (o.Status == SaleOrderStatus.Confirmed || o.Status == SaleOrderStatus.AwaitingPayment))
            .OrderByDescending(o => o.CreatedAt).Take(50).ToListAsync(cancellationToken);
        var result = new List<OrderView>();
        foreach (var o in orders)
        {
            result.Add(await ViewAsync(o, cancellationToken));
        }

        return result;
    }

    private async Task<OrderView> ViewAsync(SaleOrder order, CancellationToken cancellationToken)
    {
        var product = await db.SaleProducts.AsNoTracking().SingleAsync(p => p.Id == order.ProductId, cancellationToken);
        var tickets = await db.OrderTickets.AsNoTracking().Where(t => t.OrderId == order.Id).OrderBy(t => t.CreatedAt).ToListAsync(cancellationToken);
        var views = new List<OrderTicketView>();
        foreach (var t in tickets)
        {
            var code = t.Status == OrderTicketStatus.Active && product.Kind != SaleProductKind.Tokens ? await CodeAsync(t, cancellationToken) : null;
            views.Add(new OrderTicketView(t.Id, t.Quantity, t.Status, code));
        }

        return new OrderView(
            order.Id, order.Number, order.Status, product.Kind, product.Name, product.Date, order.GroupName, order.MemberQuantity, order.PaidQuantity,
            order.AmountCents, order.BuyerName, order.CreatedAt, order.Status == SaleOrderStatus.AwaitingPayment ? order.HoldUntil : null, views);
    }

    /// <summary>De QR van een gekochte kaart: versie 3, door de server ondertekend, zonder toestel en verlooptijd.</summary>
    public async Task<string> CodeAsync(OrderTicket ticket, CancellationToken cancellationToken)
    {
        using var key = await keys.ActivePrivateKeyAsync(cancellationToken);
        var unsigned = QrPayload.Unsigned(QrPayload.OrderTicket, ticket.PublicRef, 1, new byte[8], new DateTimeOffset(ticket.CreatedAt, TimeSpan.Zero).ToUnixTimeSeconds(), 0);
        var signature = key.SignData(unsigned, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return Base45.Encode([.. unsigned, .. signature]);
    }

    private async Task<SaleOrder> OrderByTokenAsync(Guid orderId, string token, CancellationToken cancellationToken)
    {
        var order = await db.SaleOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken);
        if (order is null || string.IsNullOrEmpty(token) || !Matches(order, token))
        {
            throw new DomainException(ErrorCodes.OrderNotFound, "Bestelling niet gevonden.", DomainErrorKind.NotFound);
        }

        return order;
    }

    // ---- Beheer ----------------------------------------------------------------------------------------------------

    /// <summary>Contant ontvangen (alleen in het portal): de kaarten worden direct geldig.</summary>
    public async Task MarkPaidCashAsync(Guid orderId, string baseUrl, CancellationToken cancellationToken)
    {
        var order = await db.SaleOrders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.OrderNotFound, "Bestelling niet gevonden.", DomainErrorKind.NotFound);
        if (order.Status is not (SaleOrderStatus.AwaitingPayment or SaleOrderStatus.Expired))
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Alleen een openstaande of verlopen bestelling kan contant betaald worden.", DomainErrorKind.Conflict);
        }

        order.Status = SaleOrderStatus.Confirmed;
        order.PaymentMethod = SalePaymentMethod.Cash;
        order.PaidAt = Now;
        await IssueTicketAsync(order, cancellationToken);
        await MarkWaitlistAsync(order, WaitlistStatus.Granted, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale-order.paid-cash", "SaleOrder", order.Id.ToString(), null,
            JsonSerializer.Serialize(new { order.Number, order.AmountCents })), cancellationToken);
        var product = await db.SaleProducts.AsNoTracking().SingleAsync(p => p.Id == order.ProductId, cancellationToken);
        await SendConfirmationAsync(order, product, baseUrl, cancellationToken);
    }

    /// <summary>Annuleren door het bestuur: de QR vervalt, er wordt niets terugbetaald.</summary>
    public async Task CancelAsync(Guid orderId, string? reason, CancellationToken cancellationToken)
    {
        var order = await db.SaleOrders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.OrderNotFound, "Bestelling niet gevonden.", DomainErrorKind.NotFound);
        if (order.Status is SaleOrderStatus.Cancelled or SaleOrderStatus.Expired)
        {
            return;
        }

        var before = order.Status.ToString();
        order.Status = SaleOrderStatus.Cancelled;
        order.CancelledAt = Now;
        order.CancelReason = Clean(reason);
        await db.OrderTickets.Where(t => t.OrderId == order.Id && t.Status == OrderTicketStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OrderTicketStatus.Cancelled), cancellationToken);
        await MarkWaitlistAsync(order, WaitlistStatus.Withdrawn, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale-order.cancelled", "SaleOrder", order.Id.ToString(), JsonSerializer.Serialize(new { status = before }),
            JsonSerializer.Serialize(new { order.Number, reason = order.CancelReason })), cancellationToken);
    }

    /// <summary>De betaallink opnieuw mailen (met een nieuw token; de vasthoudtijd verandert niet).</summary>
    public async Task ResendPaymentLinkAsync(Guid orderId, string baseUrl, CancellationToken cancellationToken)
    {
        var order = await db.SaleOrders.SingleOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.OrderNotFound, "Bestelling niet gevonden.", DomainErrorKind.NotFound);
        if (order.Status != SaleOrderStatus.AwaitingPayment || order.HoldUntil <= Now)
        {
            throw new DomainException(ErrorCodes.OrderNotPayable, "Deze bestelling staat niet (meer) open voor betaling.", DomainErrorKind.Conflict);
        }

        var product = await db.SaleProducts.AsNoTracking().SingleAsync(p => p.Id == order.ProductId, cancellationToken);
        await SendAsync(SaleMails.PaymentLink(order, product, PayLink(baseUrl, order.Id, _tokens.Unprotect(order.AccessTokenProtected)), order.WaitlistEntryId is not null), cancellationToken);
    }

    // ---- Wachtlijst ------------------------------------------------------------------------------------------------

    /// <summary>Op de wachtlijst als het product vol is. Dezelfde regels als bestellen (groep, leden, aantallen).</summary>
    public async Task<Guid> JoinWaitlistAsync(OrderInput input, SaleBuyer buyer, SaleChannel channel, CancellationToken cancellationToken)
    {
        var product = await db.SaleProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == input.ProductId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ProductNotFound, "Dit product bestaat niet (meer).", DomainErrorKind.NotFound);
        if (!IsOnSale(product, Now))
        {
            throw new DomainException(ErrorCodes.ProductNotOnSale, $"{product.Name} is nu niet te koop.", DomainErrorKind.Conflict);
        }

        var stock = (await StockAsync([product], cancellationToken))[product.Id];
        if (stock.Remaining is null || stock.Remaining >= input.MemberQuantity + input.PaidQuantity)
        {
            throw new DomainException(ErrorCodes.NotSoldOut, "Er is nog plaats: je kunt gewoon bestellen.", DomainErrorKind.Conflict);
        }

        var member = buyer.MemberId is { } memberId
            ? await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, cancellationToken)
            : null;
        var user = buyer.UserId is { } userId ? await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId, cancellationToken) : null;
        if (input.MemberQuantity > 0)
        {
            var allowance = await GroupAllowanceAsync(member?.Id, cancellationToken)
                ?? throw new DomainException(ErrorCodes.MembersOnly, "Log in als lid van een groep om gratis groepskaarten aan te vragen.", DomainErrorKind.Forbidden);
            if (input.MemberQuantity > allowance.Remaining)
            {
                throw new DomainException(ErrorCodes.GroupLimit, $"De {allowance.GroupName} kan nog {allowance.Remaining} gratis kaart(en) bestellen.", DomainErrorKind.Conflict);
            }
        }

        if (product.MembersOnly && member is null)
        {
            throw new DomainException(ErrorCodes.MembersOnly, "Munten zijn alleen voor leden.", DomainErrorKind.Forbidden);
        }

        var name = Clean(input.BuyerName) ?? member?.FullName ?? user?.DisplayName;
        var mail = Clean(input.BuyerEmail) ?? member?.Email ?? user?.Email;
        if (name is null || mail is null || !mail.Contains('@', StringComparison.Ordinal) || input.MemberQuantity < 0 || input.PaidQuantity < 0
            || input.MemberQuantity + input.PaidQuantity == 0 || input.PaidQuantity > product.MaxPerOrder)
        {
            throw Invalid("Vul je naam, e-mailadres en het aantal kaarten in.");
        }

        var entry = new WaitlistEntry
        {
            Id = IdGenerator.NewId(),
            ProductId = product.Id,
            Status = WaitlistStatus.Waiting,
            Channel = channel,
            GroupName = input.MemberQuantity > 0 ? member?.ParadeGroupName?.Trim() : null,
            MemberQuantity = input.MemberQuantity,
            PaidQuantity = input.PaidQuantity,
            BuyerName = name,
            BuyerEmail = mail,
            BuyerPhone = Clean(input.BuyerPhone) ?? member?.MobilePhone ?? member?.Phone,
            Remark = Clean(input.Remark),
            BuyerUserId = user?.Id,
            BuyerMemberId = member?.Id,
            CreatedAt = Now,
        };
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        await SendAsync(SaleMails.WaitlistJoined(entry, product), cancellationToken);
        return entry.Id;
    }

    /// <summary>
    /// Plaatsen geven aan iemand op de wachtlijst, op volgorde of (bestuur) daarbuiten. Gratis groepskaarten zijn direct
    /// geldig; losse kaarten krijgen een betaallink (48 uur) of zijn contant betaald.
    /// </summary>
    public async Task<OrderCreated> GrantWaitlistAsync(Guid entryId, PortalPayment payment, Guid grantedBy, string baseUrl, CancellationToken cancellationToken)
    {
        var entry = await db.WaitlistEntries.AsNoTracking().SingleOrDefaultAsync(w => w.Id == entryId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.WaitlistNotFound, "Niet gevonden op de wachtlijst.", DomainErrorKind.NotFound);
        if (entry.Status != WaitlistStatus.Waiting)
        {
            throw new DomainException(ErrorCodes.WaitlistNotFound, "Deze aanvraag is al afgehandeld.", DomainErrorKind.Conflict);
        }

        var draft = new OrderDraft(entry.ProductId, entry.GroupName, entry.MemberQuantity, entry.PaidQuantity, entry.BuyerName, entry.BuyerEmail,
            entry.BuyerPhone, entry.Remark, entry.BuyerUserId, entry.BuyerMemberId, entry.Channel, entry.Id);
        var method = payment == PortalPayment.Cash ? SalePaymentMethod.Cash : SalePaymentMethod.Mollie;
        var (order, product) = await PlaceAsync(draft, method, LinkHold, grantedBy, cancellationToken, ignoreSaleWindow: true);
        await db.WaitlistEntries.Where(w => w.Id == entry.Id).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Status, order.Status == SaleOrderStatus.Confirmed ? WaitlistStatus.Granted : WaitlistStatus.Invited)
            .SetProperty(w => w.InvitedAt, Now)
            .SetProperty(w => w.OrderId, order.Id), cancellationToken);
        var token = await FinishNewOrderAsync(order, product, baseUrl, sendPaymentLink: true, cancellationToken);
        if (entry.BuyerUserId is { } userId)
        {
            await notifications.EnqueueAsync(new SystemNotification(
                "Er is plek voor je",
                order.Status == SaleOrderStatus.Confirmed
                    ? $"{product.Name}: je {order.Quantity} kaart(en) van de wachtlijst staan klaar."
                    : $"{product.Name}: er is plek voor {order.Quantity} kaart(en). Betaal binnen {WaitlistEntry.InviteHours} uur via de link in je e-mail.",
                NotificationCategory.Tickets, new NotificationAudience(UserIds: [userId]), "drammers://kaarten"), cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("waitlist.granted", "WaitlistEntry", entry.Id.ToString(), null,
            JsonSerializer.Serialize(new { order.Number, payment = payment.ToString() })), cancellationToken);
        return new OrderCreated(order.Id, order.Number, token, order.Status, null);
    }

    public async Task WithdrawWaitlistAsync(Guid entryId, CancellationToken cancellationToken)
    {
        var changed = await db.WaitlistEntries.Where(w => w.Id == entryId && w.Status == WaitlistStatus.Waiting)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WaitlistStatus.Withdrawn), cancellationToken);
        if (changed == 0)
        {
            throw new DomainException(ErrorCodes.WaitlistNotFound, "Niet (meer) op de wachtlijst.", DomainErrorKind.NotFound);
        }

        await audit.WriteAsync(new AuditEntry("waitlist.withdrawn", "WaitlistEntry", entryId.ToString()), cancellationToken);
    }

    private async Task MarkWaitlistAsync(SaleOrder order, WaitlistStatus status, CancellationToken cancellationToken)
    {
        if (order.WaitlistEntryId is { } id && await db.WaitlistEntries.SingleOrDefaultAsync(w => w.Id == id, cancellationToken) is { } entry
            && entry.Status is WaitlistStatus.Invited or WaitlistStatus.Waiting)
        {
            entry.Status = status;
        }
    }

    // ---- Hulpmiddelen ----------------------------------------------------------------------------------------------

    private async Task IssueTicketAsync(SaleOrder order, CancellationToken cancellationToken)
    {
        // Idempotent: één QR per bestelling (gedeelde kaarten krijgen later een eigen QR, fase 19b).
        if (db.OrderTickets.Local.Any(t => t.OrderId == order.Id) || await db.OrderTickets.AsNoTracking().AnyAsync(t => t.OrderId == order.Id, cancellationToken))
        {
            return;
        }

        db.OrderTickets.Add(new OrderTicket
        {
            Id = IdGenerator.NewId(),
            OrderId = order.Id,
            PublicRef = RandomNumberGenerator.GetBytes(16),
            Quantity = order.Quantity,
            HolderMemberId = order.BuyerMemberId,
            Status = OrderTicketStatus.Active,
            CreatedAt = Now,
        });
    }

    private async Task LockProductAsync(Guid productId, CancellationToken cancellationToken) =>
        await db.Database.SqlQuery<Guid>($"SELECT id AS [Value] FROM ticketing.SaleProduct WITH (UPDLOCK, HOLDLOCK) WHERE id = {productId}").ToListAsync(cancellationToken);

    /// <summary>Groepskaarten tellen over beide avonden: één groep tegelijk laten bestellen.</summary>
    private async Task LockGroupAsync(int yearId, string group, CancellationToken cancellationToken)
    {
        var resource = $"sale-group:{yearId}:{group.ToUpperInvariant()}";
        await db.Database.SqlQuery<int>(
            $"""
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {resource}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 10000;
            SELECT @result AS [Value];
            """).ToListAsync(cancellationToken);
    }

    private async Task<int> NextNumberAsync(int yearId, CancellationToken cancellationToken)
    {
        var next = (await db.Database.SqlQuery<int>(
            $"""
            UPDATE payments.SaleOrderSequence WITH (UPDLOCK, HOLDLOCK)
               SET last_number = last_number + 1
            OUTPUT inserted.last_number AS [Value]
             WHERE carnival_year_id = {yearId}
            """).ToListAsync(cancellationToken)).SingleOrDefault();
        if (next > 0)
        {
            return next;
        }

        await db.Database.ExecuteSqlAsync(
            $"""
            IF NOT EXISTS (SELECT 1 FROM payments.SaleOrderSequence WITH (UPDLOCK, HOLDLOCK) WHERE carnival_year_id = {yearId})
                INSERT INTO payments.SaleOrderSequence (carnival_year_id, last_number) VALUES ({yearId}, 0);
            """, cancellationToken);
        return await NextNumberAsync(yearId, cancellationToken);
    }

    private async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await email.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // De bestelling is al vastgelegd; een e-mail die niet weg kan mag dat niet ongedaan maken.
            LogMailFailed(logger, message.Subject, ex);
        }
    }

    public static string OrderLink(string baseUrl, Guid id, string token) => $"{baseUrl}/kaarten/bestelling/?id={id}&t={token}";

    public static string PayLink(string baseUrl, Guid id, string token) => $"{baseUrl}/api/v1/sales/orders/{id}/pay?t={token}";

    /// <summary>Mollie kan geen webhook op localhost aanroepen; lokaal zonder webhook (de status wordt dan opgehaald).</summary>
    private static string? WebhookUrl(string baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && !uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttps
            ? $"{baseUrl}/api/v1/payments/mollie/webhook"
            : null;

    private static string NewToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private bool Matches(SaleOrder order, string token)
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_tokens.Unprotect(order.AccessTokenProtected)), Encoding.UTF8.GetBytes(token));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DomainException Invalid(string message) => new(ErrorCodes.OrderInvalid, message);

    public static string Euro(int cents) => (cents / 100m).ToString("€ #,##0.00", CultureInfo.GetCultureInfo("nl-NL"));

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mollie-betaling voor bestelling {Number} kon niet gestart worden")]
    private static partial void LogMollieFailed(ILogger logger, string number, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Bedrag van de Mollie-betaling klopt niet bij bestelling {Number}; niet bevestigd")]
    private static partial void LogAmountMismatch(ILogger logger, string number);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail niet verstuurd: {Subject}")]
    private static partial void LogMailFailed(ILogger logger, string subject, Exception exception);
}
