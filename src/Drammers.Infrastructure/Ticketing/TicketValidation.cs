using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Sales;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

/// <summary>
/// Server-side validatie van een gescande code (fase 13, gebruikt door de scanner in fase 14): de gegevens uit de
/// database, de regels uit <see cref="TicketQrValidator"/>.
/// </summary>
public sealed class TicketValidation(DrammersDbContext db, TicketSigningKeys keys, AccessWindows windows, IClock clock)
{
    /// <summary>Controleert een code; <paramref name="at"/> is het moment van scannen (standaard nu; offline: het moment op het toestel).</summary>
    public async Task<QrValidation> ValidateAsync(string code, CancellationToken cancellationToken, DateTimeOffset? at = null, QrPurpose purpose = QrPurpose.Access)
    {
        var moment = at ?? clock.UtcNow;
        var payload = QrPayload.TryDecode(code);
        TicketSnapshot? snapshot = null;
        if (payload is not null)
        {
            // Munten-QR (fase 19b): de referentie is die van de muntenbestelling; blokkade en lidmaatschap komen van het
            // ledenticket van de houder, het toestel van de muntenbestelling zelf (het toestel van de aankoop, nooit over
            // te zetten). Een al uitgegeven of geannuleerde bestelling telt als geblokkeerd.
            byte[]? tokensRef = null;
            Guid? tokensDeviceId = null;
            var memberTicketRef = payload.Ref;
            if (payload.IsTokens)
            {
                var order = await (
                    from ot in db.OrderTickets.AsNoTracking()
                    where ot.PublicRef == payload.Ref
                    join o in db.SaleOrders.AsNoTracking() on ot.OrderId equals o.Id
                    join p in db.SaleProducts.AsNoTracking() on o.ProductId equals p.Id
                    where p.Kind == SaleProductKind.Tokens && o.Status == SaleOrderStatus.Confirmed
                    join t in db.Tickets.AsNoTracking() on ot.HolderMemberId equals t.MemberId
                    where t.CarnivalYearId == o.CarnivalYearId
                    select new { t.PublicRef, Usable = ot.Status == OrderTicketStatus.Active, ot.BoundDeviceId }).SingleOrDefaultAsync(cancellationToken);
                if (order is null)
                {
                    return TicketQrValidator.Validate(code, moment, _ => null, [], purpose);
                }

                tokensRef = order.Usable ? payload.Ref : null;
                tokensDeviceId = order.BoundDeviceId;
                memberTicketRef = order.PublicRef;
            }

            var row = await (
                from t in db.Tickets.AsNoTracking()
                where t.PublicRef == memberTicketRef
                join y in db.CarnivalYears.AsNoTracking() on t.CarnivalYearId equals y.Id
                join m in db.Members.AsNoTracking() on t.MemberId equals m.Id
                select new { t, y, m.FullName, MembershipStatus = m.LocalStatusOverride ?? m.MembershipStatus }).SingleOrDefaultAsync(cancellationToken);
            if (row is not null)
            {
                var boundDeviceId = payload.IsTokens ? tokensDeviceId : row.t.BoundDeviceId;
                // Een afgemeld toestel telt niet meer als koppeling (codes daarvan zijn ongeldig).
                var device = boundDeviceId is { } deviceId
                    ? await db.Devices.AsNoTracking().Where(d => d.Id == deviceId && d.Status == DeviceStatus.Active)
                        .Select(d => new { d.Id, d.PublicKey }).SingleOrDefaultAsync(cancellationToken)
                    : null;
                var (from, to) = MemberTickets.Validity(row.y);
                // Buiten carnaval geldt het ticket tijdens een activiteit met toegangscontrole (QR en scannen gelijk).
                if ((moment < from || moment > to) && await windows.CurrentAsync(cancellationToken, moment) is { EventId: not null } access)
                {
                    var (start, end) = AccessWindows.Window(access);
                    (from, to) = (new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(end, TimeSpan.Zero));
                }

                var status = payload.IsTokens && tokensRef is null ? TicketStatus.Blocked : row.t.Status;
                snapshot = new TicketSnapshot(
                    tokensRef ?? row.t.PublicRef, row.t.CredentialVersion, status, row.MembershipStatus == MembershipStatus.Active, from, to,
                    device is null ? null : QrPayload.ShortDeviceId(device.Id),
                    device?.PublicKey is null ? null : Convert.FromBase64String(device.PublicKey), row.FullName);
            }
        }

        var serverKeys = payload is { IsDeviceSigned: false } ? await keys.PublicKeysAsync(cancellationToken) : [];
        return TicketQrValidator.Validate(code, moment, _ => snapshot, serverKeys, purpose);
    }
}
