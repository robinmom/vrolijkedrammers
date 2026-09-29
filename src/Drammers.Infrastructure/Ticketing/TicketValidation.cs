using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
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
            var row = await (
                from t in db.Tickets.AsNoTracking()
                where t.PublicRef == payload.Ref
                join y in db.CarnivalYears.AsNoTracking() on t.CarnivalYearId equals y.Id
                join m in db.Members.AsNoTracking() on t.MemberId equals m.Id
                join d in db.Devices.AsNoTracking() on t.BoundDeviceId equals d.Id into ds
                from d in ds.DefaultIfEmpty()
                select new { t, y, m.FullName, m.MembershipStatus, DeviceActive = d != null && d.Status == DeviceStatus.Active, DeviceKey = d == null ? null : d.PublicKey }).SingleOrDefaultAsync(cancellationToken);
            if (row is not null)
            {
                var (from, to) = MemberTickets.Validity(row.y);
                // Buiten carnaval geldt het ticket tijdens een activiteit met toegangscontrole (QR en scannen gelijk).
                if ((moment < from || moment > to) && await windows.CurrentAsync(cancellationToken, moment) is { EventId: not null } access)
                {
                    var (start, end) = AccessWindows.Window(access);
                    (from, to) = (new DateTimeOffset(start, TimeSpan.Zero), new DateTimeOffset(end, TimeSpan.Zero));
                }

                snapshot = new TicketSnapshot(
                    row.t.PublicRef, row.t.CredentialVersion, row.t.Status, row.MembershipStatus == MembershipStatus.Active, from, to,
                    // Een afgemeld toestel telt niet meer als koppeling (codes daarvan zijn ongeldig).
                    row.t.BoundDeviceId is { } boundId && row.DeviceActive ? QrPayload.ShortDeviceId(boundId) : null,
                    row.DeviceKey is null ? null : Convert.FromBase64String(row.DeviceKey), row.FullName);
            }
        }

        var serverKeys = payload is { IsDeviceSigned: false } ? await keys.PublicKeysAsync(cancellationToken) : [];
        return TicketQrValidator.Validate(code, moment, _ => snapshot, serverKeys, purpose);
    }
}
