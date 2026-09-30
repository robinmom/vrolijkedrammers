using System.Globalization;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Ticketing;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Sales;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Sales;

/// <summary>Wat de kassa na een scan ziet (Figma "iOS / 9 Kassa – munten uitgeven").</summary>
public sealed record KassaResult(
    Guid? ScanId, TokenScanOutcome Outcome, string Title, string Message, string? HolderName, int? Quantity, string? OrderNumber, string? PaidWith);

/// <summary>Eén regel in de Kassalog van het portal.</summary>
public sealed record KassaLogRow(
    Guid Id, DateTime ScannedAt, DateTime? IssuedAt, TokenScanOutcome Outcome, string? Reason, string? MemberName, string? MemberGroup, int? Quantity,
    string? OrderNumber, string? PaidWith, string OperatorName, string? DeviceName);

public sealed record KassaDay(DateOnly Day, int Issued, int TokensIssued, int Refused, IReadOnlyList<KassaLogRow> Rows);

/// <summary>
/// Munten uitgeven bij de kassa (fase 19c, rol Kassa). De munten-QR is per bestelling, aan het toestel van het lid
/// gekoppeld en steeds vernieuwd; alleen online (besluit 30-09-2026). De scanner toont de bestelling; pas bij
/// "Bestelling uitgegeven" gaat de bestelling in één keer op uitgegeven, zodat hij nooit twee keer wordt uitgegeven.
/// Elke scan en uitgifte staat in de Kassalog.
/// </summary>
public sealed class TokenCollection(DrammersDbContext db, TicketValidation validation, IAuditLogger audit, IClock clock)
{
    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    /// <summary>Na zo lang moet de kassa opnieuw scannen voordat hij kan uitgeven.</summary>
    public static readonly TimeSpan IssueWithin = TimeSpan.FromMinutes(10);

    private DateTime Now => clock.UtcNow.UtcDateTime;

    private static string Time(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Loil).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string PaidWith(SalePaymentMethod method) => method switch
    {
        SalePaymentMethod.Cash => "contant",
        SalePaymentMethod.Free => "gratis",
        _ => "iDEAL",
    };

    public async Task<KassaResult> ScanAsync(Guid operatorId, string? installationId, string code, CancellationToken cancellationToken)
    {
        var device = string.IsNullOrWhiteSpace(installationId)
            ? null
            : await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == operatorId && d.InstallationId == installationId && d.Status == DeviceStatus.Active, cancellationToken);
        var scan = new TokenScan { Id = IdGenerator.NewId(), OperatorUserId = operatorId, OperatorDeviceId = device?.Id, ScannedAt = Now };
        var check = await validation.ValidateAsync(code, cancellationToken, purpose: QrPurpose.Tokens);
        var payload = QrPayload.TryDecode(code);
        var order = payload is { IsTokens: true }
            ? await (
                from t in db.OrderTickets.AsNoTracking()
                where t.PublicRef == payload.Ref
                join o in db.SaleOrders.AsNoTracking() on t.OrderId equals o.Id
                select new { Ticket = t, o.Id, o.Number, o.PaymentMethod, o.Status }).SingleOrDefaultAsync(cancellationToken)
            : null;
        if (order is not null)
        {
            scan.OrderTicketId = order.Ticket.Id;
            scan.OrderId = order.Id;
            scan.MemberId = order.Ticket.HolderMemberId;
            scan.Quantity = order.Ticket.Quantity;
        }

        KassaResult result;
        if (!check.IsValid)
        {
            scan.Outcome = TokenScanOutcome.Refused;
            var issued = order?.Ticket.Status == OrderTicketStatus.Used;
            scan.Reason = issued ? "AlreadyIssued" : check.Result.ToString();
            var lastIssue = issued
                ? await db.TokenScans.AsNoTracking().Where(s => s.OrderTicketId == order!.Ticket.Id && s.Outcome == TokenScanOutcome.Issued)
                    .Select(s => s.IssuedAt).FirstOrDefaultAsync(cancellationToken)
                : null;
            var message = issued
                ? $"Deze munten zijn al uitgegeven{(lastIssue is { } at ? $" om {Time(at)}" : "")} (bestelling {order!.Number})."
                : check.Result == QrCheck.Blocked && order is not null
                    ? $"Bestelling {order.Number} is geannuleerd of niet betaald."
                    : check.Message;
            result = new KassaResult(null, scan.Outcome, "Niet uitgeven", message, check.Ticket?.HolderName, scan.Quantity, order?.Number, null);
        }
        else
        {
            scan.Outcome = TokenScanOutcome.Ready;
            result = new KassaResult(scan.Id, scan.Outcome, "Munten uitgeven", "Persoonsgebonden · nog niet afgehaald", check.Ticket!.HolderName,
                scan.Quantity, order!.Number, PaidWith(order.PaymentMethod));
        }

        db.TokenScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken);
        return result with { ScanId = scan.Outcome == TokenScanOutcome.Ready ? scan.Id : null };
    }

    /// <summary>"Bestelling uitgegeven": in één keer op uitgegeven; tegelijk op een ander toestel geeft een conflict.</summary>
    public async Task<KassaResult> IssueAsync(Guid operatorId, Guid scanId, CancellationToken cancellationToken)
    {
        var scan = await db.TokenScans.SingleOrDefaultAsync(s => s.Id == scanId && s.OperatorUserId == operatorId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Scan niet gevonden.", DomainErrorKind.NotFound);
        if (scan.Outcome != TokenScanOutcome.Ready || scan.OrderTicketId is not { } ticketId)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Deze scan is al afgehandeld.", DomainErrorKind.Conflict);
        }

        if (Now - scan.ScannedAt > IssueWithin)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Deze scan is te oud. Scan de munten-QR opnieuw.", DomainErrorKind.Conflict);
        }

        var claimed = await db.OrderTickets.Where(t => t.Id == ticketId && t.Status == OrderTicketStatus.Active)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, OrderTicketStatus.Used).SetProperty(t => t.UsedAt, Now), cancellationToken);
        if (claimed == 0)
        {
            scan.Outcome = TokenScanOutcome.Refused;
            scan.Reason = "AlreadyIssued";
            await db.SaveChangesAsync(cancellationToken);
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Deze munten zijn net al uitgegeven (op een ander toestel).", DomainErrorKind.Conflict);
        }

        scan.Outcome = TokenScanOutcome.Issued;
        scan.IssuedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        var order = await db.SaleOrders.AsNoTracking().Where(o => o.Id == scan.OrderId).Select(o => new { o.Number, o.PaymentMethod }).SingleAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("tokens.issued", "OrderTicket", ticketId.ToString(), null,
            JsonSerializer.Serialize(new { order.Number, scan.Quantity, scanId })), cancellationToken);
        var holder = await db.Members.AsNoTracking().Where(m => m.Id == scan.MemberId).Select(m => m.FullName).SingleOrDefaultAsync(cancellationToken);
        return new KassaResult(scan.Id, TokenScanOutcome.Issued, "Uitgegeven", $"{scan.Quantity} munten uitgegeven om {Time(scan.IssuedAt.Value)}.", holder,
            scan.Quantity, order.Number, PaidWith(order.PaymentMethod));
    }

    /// <summary>Kassalog van één dag (Loil, tot 06:00 de volgende ochtend), nieuwste bovenaan.</summary>
    public async Task<KassaDay> LogAsync(DateOnly? day, CancellationToken cancellationToken)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(Now, Loil) - AccessWindows.DayBoundary;
        var date = day ?? DateOnly.FromDateTime(local);
        var start = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.FromTimeSpan(AccessWindows.DayBoundary)), Loil);
        var end = start.AddDays(1);
        var rows = await (
            from s in db.TokenScans.AsNoTracking()
            where s.ScannedAt >= start && s.ScannedAt < end
            join m in db.Members.AsNoTracking() on s.MemberId equals m.Id into ms
            from m in ms.DefaultIfEmpty()
            join o in db.SaleOrders.AsNoTracking() on s.OrderId equals o.Id into os
            from o in os.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on s.OperatorUserId equals u.Id
            join d in db.Devices.AsNoTracking() on s.OperatorDeviceId equals d.Id into ds
            from d in ds.DefaultIfEmpty()
            orderby s.ScannedAt descending
            select new
            {
                s,
                MemberName = m == null ? null : m.FullName,
                MemberGroup = m == null ? null : m.ParadeGroupName,
                Number = o == null ? null : o.Number,
                Method = o == null ? (SalePaymentMethod?)null : o.PaymentMethod,
                u.DisplayName,
                DeviceName = d == null ? null : d.Name,
            }).Take(1000).ToListAsync(cancellationToken);
        var list = rows.Select(r => new KassaLogRow(
            r.s.Id, r.s.ScannedAt, r.s.IssuedAt, r.s.Outcome, r.s.Reason, r.MemberName, r.MemberGroup, r.s.Quantity, r.Number,
            r.Method is { } method ? PaidWith(method) : null, r.DisplayName, r.DeviceName)).ToList();
        return new KassaDay(date, list.Count(r => r.Outcome == TokenScanOutcome.Issued), list.Where(r => r.Outcome == TokenScanOutcome.Issued).Sum(r => r.Quantity ?? 0),
            list.Count(r => r.Outcome == TokenScanOutcome.Refused), list);
    }
}
