using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Sales;

public sealed record SaleProductInput(
    SaleProductKind Kind, string Name, string? Description, Guid? EventId, DateOnly? Date, int PriceCents, int? Capacity, int MaxPerOrder,
    DateTime? SaleOpensAt, DateTime? SaleClosesAt, bool OnSale, int SortOrder);

public sealed record SaleProductRow(SaleProduct Product, ProductStock Stock, int RevenueCents, int Waiting);

/// <summary>Een toestel van het lid waar het bestuur munten naartoe kan verplaatsen.</summary>
public sealed record TokenDeviceOption(Guid Id, string Name, DateTime LastSeenAt);

/// <summary>
/// Aan welk toestel de munten van een bestelling gekoppeld zijn, of ze al eens verplaatst zijn (dan nooit meer) en naar
/// welke toestellen van het lid ze verplaatst kunnen worden.
/// </summary>
public sealed record TokenDevice(
    string? DeviceName, bool DeviceActive, DateTime? MovedAt, string? MovedBy, bool CanMove, IReadOnlyList<TokenDeviceOption> Options);

public sealed record SalesSummary(int RevenueCents, int Sold, int OpenPaymentLinks, int OpenAmountCents, int TokensToCollect, int TokensSold);

public sealed record SaleOrderRow(
    Guid Id, string Number, Guid ProductId, string ProductName, SaleOrderStatus Status, SalePaymentMethod PaymentMethod, SaleChannel Channel,
    string? GroupName, int MemberQuantity, int PaidQuantity, int AmountCents, string BuyerName, string BuyerEmail, string? BuyerPhone,
    string? Remark, bool BuyerIsMember, DateTime CreatedAt, DateTime? HoldUntil, DateTime? PaidAt, bool Collected);

public sealed record WaitlistRow(
    Guid Id, int Position, WaitlistStatus Status, string? GroupName, int MemberQuantity, int PaidQuantity, string BuyerName, string BuyerEmail,
    string? BuyerPhone, bool BuyerIsMember, string? Remark, DateTime CreatedAt, DateTime? InvitedAt, string? OrderNumber, bool Fits);

/// <summary>Eén regel op het avondoverzicht: een groep met het totaal, of losse kaarten op naam van de besteller.</summary>
public sealed record EveningRow(
    string Name, bool IsGroup, int Quantity, string Orderers, string? Phones, string? Emails, string Membership, string Paid, string? Remarks,
    IReadOnlyList<string> OrderNumbers);

public sealed record Evening(Guid ProductId, string Name, DateOnly? Date, int? Capacity, int Sold, int Held, int Waiting, IReadOnlyList<EveningRow> Rows);

/// <summary>Beheer van de kaartverkoop in het portal (fase 19, Figma 🎫 Kaartverkoop).</summary>
public sealed class SaleAdministration(DrammersDbContext db, TicketSales sales, IAuditLogger audit, IClock clock)
{
    private DateTime Now => clock.UtcNow.UtcDateTime;

    private async Task<int> ActiveYearAsync(CancellationToken cancellationToken) =>
        await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NotFound, "Er is geen actief carnavalsjaar.", DomainErrorKind.NotFound);

    public async Task<IReadOnlyList<SaleProductRow>> ProductsAsync(CancellationToken cancellationToken)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var products = await db.SaleProducts.AsNoTracking().Where(p => p.CarnivalYearId == year)
            .OrderBy(p => p.SortOrder).ThenBy(p => p.Date).ThenBy(p => p.Name).ToListAsync(cancellationToken);
        var stock = await sales.StockAsync(products, cancellationToken);
        var ids = products.Select(p => p.Id).ToList();
        var revenue = await db.SaleOrders.AsNoTracking().Where(o => ids.Contains(o.ProductId) && o.Status == SaleOrderStatus.Confirmed)
            .GroupBy(o => o.ProductId).Select(g => new { g.Key, Sum = g.Sum(o => o.AmountCents) }).ToDictionaryAsync(g => g.Key, g => g.Sum, cancellationToken);
        var waiting = await db.WaitlistEntries.AsNoTracking().Where(w => ids.Contains(w.ProductId) && w.Status == WaitlistStatus.Waiting)
            .GroupBy(w => w.ProductId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);
        return [.. products.Select(p => new SaleProductRow(p, stock[p.Id], revenue.GetValueOrDefault(p.Id), waiting.GetValueOrDefault(p.Id)))];
    }

    /// <summary>Kerncijfers van het carnavalsjaar, of van één soort product (één pagina onder Verkoop).</summary>
    public async Task<SalesSummary> SummaryAsync(CancellationToken cancellationToken, SaleProductKind? kind = null)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var now = Now;
        var orders = OfKind(db.SaleOrders.AsNoTracking().Where(o => o.CarnivalYearId == year), kind);
        var revenue = await orders.Where(o => o.Status == SaleOrderStatus.Confirmed).SumAsync(o => o.AmountCents, cancellationToken);
        var sold = await orders.Where(o => o.Status == SaleOrderStatus.Confirmed).SumAsync(o => o.MemberQuantity + o.PaidQuantity, cancellationToken);
        var open = await orders.Where(o => o.Status == SaleOrderStatus.AwaitingPayment && o.HoldUntil > now)
            .GroupBy(_ => 1).Select(g => new { Count = g.Count(), Sum = g.Sum(o => o.AmountCents) }).SingleOrDefaultAsync(cancellationToken);
        var tokens = await (
            from o in orders
            join p in db.SaleProducts.AsNoTracking() on o.ProductId equals p.Id
            where p.Kind == SaleProductKind.Tokens && o.Status == SaleOrderStatus.Confirmed
            join t in db.OrderTickets.AsNoTracking() on o.Id equals t.OrderId
            select new { t.Quantity, t.Status }).ToListAsync(cancellationToken);
        return new SalesSummary(revenue, sold, open?.Count ?? 0, open?.Sum ?? 0,
            tokens.Where(t => t.Status == OrderTicketStatus.Active).Sum(t => t.Quantity), tokens.Sum(t => t.Quantity));
    }

    private IQueryable<SaleOrder> OfKind(IQueryable<SaleOrder> orders, SaleProductKind? kind) =>
        kind is { } k ? orders.Where(o => db.SaleProducts.Any(p => p.Id == o.ProductId && p.Kind == k)) : orders;

    public async Task<SaleProduct> CreateProductAsync(SaleProductInput input, CancellationToken cancellationToken)
    {
        var product = new SaleProduct { Id = IdGenerator.NewId(), CarnivalYearId = await ActiveYearAsync(cancellationToken), Name = "", CreatedAt = Now };
        await ApplyAsync(product, input, cancellationToken);
        db.SaleProducts.Add(product);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale-product.created", "SaleProduct", product.Id.ToString(), null, Snapshot(product)), cancellationToken);
        return product;
    }

    public async Task<SaleProduct> UpdateProductAsync(Guid id, SaleProductInput input, CancellationToken cancellationToken)
    {
        var product = await db.SaleProducts.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ProductNotFound, "Product niet gevonden.", DomainErrorKind.NotFound);
        var before = Snapshot(product);
        if (input.Kind != product.Kind && await db.SaleOrders.AnyAsync(o => o.ProductId == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.OrderInvalid, "Het soort product kan niet meer veranderen: er zijn al bestellingen.", DomainErrorKind.Conflict);
        }

        await ApplyAsync(product, input, cancellationToken);
        product.UpdatedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("sale-product.updated", "SaleProduct", product.Id.ToString(), before, Snapshot(product)), cancellationToken);
        return product;
    }

    private async Task ApplyAsync(SaleProduct product, SaleProductInput input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 120)
        {
            throw new DomainException(ErrorCodes.Validation, "Vul een naam in (hooguit 120 tekens).");
        }

        if (input.PriceCents is < 0 or > 100_000 || input.Capacity is < 0 or > 100_000 || input.MaxPerOrder is < 1 or > 500)
        {
            throw new DomainException(ErrorCodes.Validation, "Controleer de prijs, het aantal plaatsen en het maximum per bestelling.");
        }

        if (input.SaleOpensAt is { } from && input.SaleClosesAt is { } to && to <= from)
        {
            throw new DomainException(ErrorCodes.Validation, "De verkoop moet sluiten na de opening.");
        }

        if (input.Kind == SaleProductKind.EventTicket && input.EventId is null)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies de activiteit uit de agenda.");
        }

        if (input.EventId is { } eventId && !await db.Events.AnyAsync(e => e.Id == eventId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Die activiteit bestaat niet (meer).");
        }

        if (input.Capacity is { } capacity && product.Id != Guid.Empty)
        {
            var stock = (await sales.StockAsync([product], cancellationToken))[product.Id];
            if (capacity < stock.Sold + stock.Held)
            {
                throw new DomainException(ErrorCodes.Validation, $"Er zijn al {stock.Sold + stock.Held} plaatsen verkocht of vastgehouden; minder plaatsen kan niet.");
            }
        }

        product.Kind = input.Kind;
        product.Name = input.Name.Trim();
        product.Description = string.IsNullOrWhiteSpace(input.Description) ? null : input.Description.Trim();
        product.EventId = input.EventId;
        product.Date = input.Kind == SaleProductKind.Tokens ? null : input.Date;
        product.PriceCents = input.PriceCents;
        product.Capacity = input.Capacity;
        product.MaxPerOrder = input.MaxPerOrder;
        product.SaleOpensAt = input.SaleOpensAt;
        product.SaleClosesAt = input.SaleClosesAt;
        product.OnSale = input.OnSale;
        product.SortOrder = input.SortOrder;
    }

    private static string Snapshot(SaleProduct p) => JsonSerializer.Serialize(new
    {
        kind = p.Kind.ToString(),
        p.Name,
        p.Date,
        p.PriceCents,
        p.Capacity,
        p.MaxPerOrder,
        p.SaleOpensAt,
        p.SaleClosesAt,
        p.OnSale,
        p.EventId,
    });

    public async Task<(IReadOnlyList<SaleOrderRow> Items, int Total)> OrdersAsync(
        Guid? productId, SaleOrderStatus? status, string? search, int page, int pageSize, CancellationToken cancellationToken,
        SaleProductKind? kind = null)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var query = OfKind(db.SaleOrders.AsNoTracking().Where(o => o.CarnivalYearId == year), kind);
        if (productId is { } p)
        {
            query = query.Where(o => o.ProductId == p);
        }

        if (status is { } s)
        {
            query = query.Where(o => o.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.Number.Contains(term) || o.BuyerName.Contains(term) || o.BuyerEmail.Contains(term) || (o.GroupName != null && o.GroupName.Contains(term)));
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await (
            from o in query.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            join pr in db.SaleProducts.AsNoTracking() on o.ProductId equals pr.Id
            select new { o, pr.Name }).ToListAsync(cancellationToken);
        return ([.. await RowsAsync(rows.Select(r => (r.o, r.Name)).ToList(), cancellationToken)], total);
    }

    private async Task<IEnumerable<SaleOrderRow>> RowsAsync(List<(SaleOrder Order, string ProductName)> rows, CancellationToken cancellationToken)
    {
        var ids = rows.Select(r => r.Order.Id).ToList();
        var collected = await db.OrderTickets.AsNoTracking().Where(t => ids.Contains(t.OrderId) && t.Status == OrderTicketStatus.Used)
            .Select(t => t.OrderId).Distinct().ToListAsync(cancellationToken);
        return rows.Select(r => new SaleOrderRow(
            r.Order.Id, r.Order.Number, r.Order.ProductId, r.ProductName, r.Order.Status, r.Order.PaymentMethod, r.Order.Channel, r.Order.GroupName,
            r.Order.MemberQuantity, r.Order.PaidQuantity, r.Order.AmountCents, r.Order.BuyerName, r.Order.BuyerEmail, r.Order.BuyerPhone, r.Order.Remark,
            r.Order.BuyerMemberId is not null || r.Order.GroupName is not null, r.Order.CreatedAt,
            r.Order.Status == SaleOrderStatus.AwaitingPayment ? r.Order.HoldUntil : null, r.Order.PaidAt, collected.Contains(r.Order.Id)));
    }

    public async Task<IReadOnlyList<WaitlistRow>> WaitlistAsync(Guid productId, CancellationToken cancellationToken)
    {
        var product = await db.SaleProducts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ProductNotFound, "Product niet gevonden.", DomainErrorKind.NotFound);
        var stock = (await sales.StockAsync([product], cancellationToken))[product.Id];
        var entries = await (
            from w in db.WaitlistEntries.AsNoTracking().Where(w => w.ProductId == productId && w.Status != WaitlistStatus.Withdrawn)
            join o in db.SaleOrders.AsNoTracking() on w.OrderId equals o.Id into os
            from o in os.DefaultIfEmpty()
            orderby w.CreatedAt
            select new { w, OrderNumber = o == null ? null : o.Number }).ToListAsync(cancellationToken);
        var position = 0;
        return [.. entries.Select(e => new WaitlistRow(
            e.w.Id, e.w.Status == WaitlistStatus.Waiting ? ++position : 0, e.w.Status, e.w.GroupName, e.w.MemberQuantity, e.w.PaidQuantity,
            e.w.BuyerName, e.w.BuyerEmail, e.w.BuyerPhone, e.w.BuyerMemberId is not null, e.w.Remark, e.w.CreatedAt, e.w.InvitedAt, e.OrderNumber,
            stock.Remaining is null || e.w.Quantity <= stock.Remaining))];
    }

    /// <summary>
    /// Pronkzitting per avond: groepen met het totaal aantal personen en losse kaarten op naam (betaald of openstaand),
    /// gesorteerd op groep en dan op naam. Bron voor het portaloverzicht en de export voor de tafelindeling.
    /// </summary>
    public async Task<IReadOnlyList<Evening>> EveningsAsync(CancellationToken cancellationToken)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var products = await db.SaleProducts.AsNoTracking().Where(p => p.CarnivalYearId == year && p.Kind == SaleProductKind.Pronkzitting)
            .OrderBy(p => p.Date).ThenBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var stock = await sales.StockAsync(products, cancellationToken);
        var ids = products.Select(p => p.Id).ToList();
        var now = Now;
        var orders = await db.SaleOrders.AsNoTracking()
            .Where(o => ids.Contains(o.ProductId) && (o.Status == SaleOrderStatus.Confirmed || (o.Status == SaleOrderStatus.AwaitingPayment && o.HoldUntil > now)))
            .OrderBy(o => o.CreatedAt).ToListAsync(cancellationToken);
        var waiting = await db.WaitlistEntries.AsNoTracking().Where(w => ids.Contains(w.ProductId) && w.Status == WaitlistStatus.Waiting)
            .GroupBy(w => w.ProductId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellationToken);

        return [.. products.Select(p =>
        {
            var mine = orders.Where(o => o.ProductId == p.Id).ToList();
            var groups = mine.Where(o => o.GroupName is not null).GroupBy(o => o.GroupName!, StringComparer.OrdinalIgnoreCase)
                .Select(g => Row(g.Key, isGroup: true, [.. g]));
            var loose = mine.Where(o => o.GroupName is null).Select(o => Row(o.BuyerName, isGroup: false, [o]));
            var rows = groups.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                .Concat(loose.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
            return new Evening(p.Id, p.Name, p.Date, p.Capacity, stock[p.Id].Sold, stock[p.Id].Held, waiting.GetValueOrDefault(p.Id), rows);
        })];
    }

    private static EveningRow Row(string name, bool isGroup, List<SaleOrder> orders)
    {
        static string? Join(IEnumerable<string?> values) =>
            string.Join("; ", values.Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.OrdinalIgnoreCase)) is { Length: > 0 } s ? s : null;

        var free = orders.Sum(o => o.MemberQuantity);
        var paid = orders.Sum(o => o.PaidQuantity);
        var membership = (free, paid) switch
        {
            ( > 0, 0) => "Lid",
            (0, _) => isGroup ? "Niet-lid" : orders.Any(o => o.BuyerMemberId is not null) ? "Lid (losse kaarten)" : "Niet-lid",
            _ => $"Lid ({free}) en niet-lid ({paid})",
        };
        var open = orders.Where(o => o.Status == SaleOrderStatus.AwaitingPayment).Sum(o => o.PaidQuantity);
        var paidText = paid == 0
            ? "Gratis (contributie)"
            : open == 0
                ? string.Join(" + ", orders.Where(o => o.PaidQuantity > 0).Select(o => o.PaymentMethod == SalePaymentMethod.Cash ? "contant" : "iDEAL").Distinct()) is var how && how.Length > 0 ? $"Betaald ({how})" : "Betaald"
                : $"{open} nog niet betaald";
        return new EveningRow(
            name, isGroup, free + paid, Join(orders.Select(o => o.BuyerName))!, Join(orders.Select(o => o.BuyerPhone)), Join(orders.Select(o => o.BuyerEmail)),
            membership, paidText, Join(orders.Select(o => o.Remark)), [.. orders.Select(o => o.Number)]);
    }

    /// <summary>De munten-QR van een betaalde muntenbestelling die nog niet is afgehaald.</summary>
    private async Task<OrderTicket> TokenTicketAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var ticket = await (
            from t in db.OrderTickets
            where t.OrderId == orderId && t.SharedFromTicketId == null
            join o in db.SaleOrders on t.OrderId equals o.Id
            join p in db.SaleProducts on o.ProductId equals p.Id
            where p.Kind == SaleProductKind.Tokens && o.Status == SaleOrderStatus.Confirmed
            select t).SingleOrDefaultAsync(cancellationToken);
        return ticket ?? throw new DomainException(ErrorCodes.OrderNotFound, "Geen betaalde muntenbestelling gevonden.", DomainErrorKind.NotFound);
    }

    private IQueryable<Device> HolderDevices(Guid? holderMemberId) =>
        from d in db.Devices
        join u in db.Users on d.UserId equals u.Id
        where holderMemberId != null && u.MemberId == holderMemberId && d.Status == DeviceStatus.Active
        select d;

    public async Task<TokenDevice> TokenDeviceAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var ticket = await TokenTicketAsync(orderId, cancellationToken);
        var current = ticket.BoundDeviceId is { } boundId
            ? await db.Devices.AsNoTracking().Where(d => d.Id == boundId).Select(d => new { d.Name, Active = d.Status == DeviceStatus.Active }).SingleOrDefaultAsync(cancellationToken)
            : null;
        var movedBy = ticket.MovedByUserId is { } by
            ? await db.Users.AsNoTracking().Where(u => u.Id == by).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken)
            : null;
        var options = await HolderDevices(ticket.HolderMemberId).AsNoTracking().Where(d => d.Id != ticket.BoundDeviceId)
            .OrderByDescending(d => d.LastSeenAt).Select(d => new TokenDeviceOption(d.Id, d.Name, d.LastSeenAt)).ToListAsync(cancellationToken);
        var canMove = ticket.Status == OrderTicketStatus.Active && ticket.MovedAt is null;
        return new TokenDevice(current?.Name, current?.Active == true, ticket.MovedAt, movedBy, canMove, canMove ? options : []);
    }

    /// <summary>
    /// Munten één keer naar een ander (actief) toestel van hetzelfde lid verplaatsen, bijv. bij een kapotte of verloren
    /// telefoon. Alleen het bestuur, met een reden; daarna nooit meer. Codes van het oude toestel zijn direct ongeldig.
    /// </summary>
    public async Task MoveTokensAsync(Guid orderId, Guid deviceId, string reason, Guid movedBy, CancellationToken cancellationToken)
    {
        var why = reason.Trim();
        if (why.Length is < 5 or > 500)
        {
            throw new DomainException(ErrorCodes.Validation, "Geef een reden van 5 tot 500 tekens.");
        }

        var ticket = await TokenTicketAsync(orderId, cancellationToken);
        if (ticket.Status != OrderTicketStatus.Active)
        {
            throw new DomainException(ErrorCodes.OrderInvalid, "Deze munten zijn al afgehaald of geannuleerd.", DomainErrorKind.Conflict);
        }

        if (ticket.MovedAt is not null)
        {
            throw new DomainException(ErrorCodes.OrderInvalid, "Deze munten zijn al een keer verplaatst; dat kan maar één keer.", DomainErrorKind.Conflict);
        }

        if (deviceId == ticket.BoundDeviceId || !await HolderDevices(ticket.HolderMemberId).AnyAsync(d => d.Id == deviceId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een ander aangemeld toestel van het lid zelf.");
        }

        var previous = ticket.BoundDeviceId;
        ticket.BoundDeviceId = deviceId;
        ticket.BoundAt = Now;
        ticket.MovedAt = Now;
        ticket.MovedByUserId = movedBy;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("order-ticket.moved", "OrderTicket", ticket.Id.ToString(),
            JsonSerializer.Serialize(new { deviceId = previous }), JsonSerializer.Serialize(new { deviceId, reason = why })), cancellationToken);
    }

    /// <summary>Munten: per bestelling wie, hoeveel, betaald en of ze al zijn afgehaald.</summary>
    public async Task<IReadOnlyList<SaleOrderRow>> TokensAsync(CancellationToken cancellationToken)
    {
        var year = await ActiveYearAsync(cancellationToken);
        var rows = await (
            from o in db.SaleOrders.AsNoTracking()
            join p in db.SaleProducts.AsNoTracking() on o.ProductId equals p.Id
            where o.CarnivalYearId == year && p.Kind == SaleProductKind.Tokens && o.Status != SaleOrderStatus.Expired
            orderby o.CreatedAt descending
            select new { o, p.Name }).Take(1000).ToListAsync(cancellationToken);
        return [.. await RowsAsync(rows.Select(r => (r.o, r.Name)).ToList(), cancellationToken)];
    }
}
