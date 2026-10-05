using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

/// <summary>Aankomsten en scans in één uur (begin van het uur in UTC; het portal toont Loil-tijd).</summary>
public sealed record HourBucket(DateTime HourStart, int Arrivals, int Scans);

public sealed record ReasonCount(string Reason, int Count);

/// <summary>Statistieken van één toegangsmoment (carnavalsdag of activiteit met toegangscontrole).</summary>
public sealed record AccessStats(
    AccessEvent Moment, int ActiveMembers, int Inside, int Scans, int RepeatsSameDevice, int RepeatsOtherDevice, int Refused,
    int ViaQr, int ViaCheckIn, int OfflineScans, int OfflineConflicts, IReadOnlyList<HourBucket> PerHour, IReadOnlyList<ReasonCount> RefusalReasons);

/// <summary>
/// "Klaar voor de deur": hoeveel actieve leden hun ledenticket aan een telefoon hebben gekoppeld (Mijn QR) en hoeveel
/// niet — die laatsten moeten bij de deur via de ledenlijst worden ingecheckt.
/// </summary>
public sealed record DoorReadiness(int ActiveMembers, int Bound, int BoundWithHardwareKey, int NotBound);

public sealed record AccessDashboard(bool Live, AccessStats? Stats, DoorReadiness Readiness);

/// <summary>
/// Toegangsstatistieken voor het bestuur (fase 14/15, <c>ticket.read</c>): per carnavalsdag of activiteit de unieke
/// bezoekers, herhaalde en geweigerde scans, aankomsten per uur, QR versus inchecken en offline scans; plus een blok op
/// het dashboard met de lopende (of laatste) dag en hoe klaar de leden zijn voor de deur.
/// </summary>
public sealed class AccessStatistics(DrammersDbContext db, AccessWindows windows, IClock clock)
{
    public async Task<AccessEvent> MomentAsync(string key, CancellationToken cancellationToken)
    {
        if (AccessWindows.TryParseDayKey(key, out var day))
        {
            return AccessWindows.ForCarnivalDay(day);
        }

        var id = Guid.TryParse(key, out var parsed) ? parsed : Guid.Empty;
        var e = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.AccessControl, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Carnavalsdag of activiteit niet gevonden.", DomainErrorKind.NotFound);
        return AccessWindows.ForEvent(e);
    }

    public async Task<AccessStats> StatsAsync(AccessEvent moment, CancellationToken cancellationToken)
    {
        var query = moment.EventId is { } eventId
            ? db.AccessScans.AsNoTracking().Where(s => s.EventId == eventId)
            : db.AccessScans.AsNoTracking().Where(s => s.EventId == null && s.CarnivalDay == moment.CarnivalDay);
        var scans = await query.OrderBy(s => s.ScannedAt).ToListAsync(cancellationToken);
        var first = scans.Where(s => s.Admits && s.MemberId != null).GroupBy(s => s.MemberId).Select(g => g.First()).ToList();
        var perHour = scans.GroupBy(s => new DateTime(s.ScannedAt.Year, s.ScannedAt.Month, s.ScannedAt.Day, s.ScannedAt.Hour, 0, 0, DateTimeKind.Utc))
            .Select(g => new HourBucket(g.Key, first.Count(f => f.ScannedAt >= g.Key && f.ScannedAt < g.Key.AddHours(1)), g.Count()))
            .OrderBy(h => h.HourStart).ToList();
        var refused = scans.Where(s => s.Outcome == AccessOutcome.Refused || s.Decision == AccessDecision.Refused).ToList();
        return new AccessStats(
            moment,
            await db.Members.CountAsync(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, cancellationToken),
            first.Count,
            scans.Count,
            scans.Count(s => s.Outcome == AccessOutcome.AdmittedAgain),
            scans.Count(s => s.Outcome == AccessOutcome.Warning),
            refused.Count,
            first.Count(f => f.Method == AccessMethod.Qr),
            first.Count(f => f.Method == AccessMethod.Manual),
            scans.Count(s => s.Offline),
            scans.Count(s => s.Offline && s.Outcome == AccessOutcome.Refused && s.OfflineOutcome is not null and not AccessOutcome.Refused),
            perHour,
            [.. refused.GroupBy(s => s.Reason ?? (s.Decision == AccessDecision.Refused ? "RefusedByDoor" : "Unknown"))
                .Select(g => new ReasonCount(g.Key, g.Count())).OrderByDescending(r => r.Count)]);
    }

    /// <summary>Alle carnavalsdagen en activiteiten met toegangscontrole naast elkaar (nieuwste eerst).</summary>
    public async Task<IReadOnlyList<AccessStats>> OverviewAsync(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking().Where(e => e.AccessControl).OrderByDescending(e => e.StartAt).Take(50).ToListAsync(cancellationToken);
        var days = await db.AccessScans.AsNoTracking().Where(s => s.CarnivalDay != null).Select(s => s.CarnivalDay!.Value).Distinct().ToListAsync(cancellationToken);
        var result = new List<AccessStats>();
        foreach (var moment in events.Select(AccessWindows.ForEvent).Concat(days.Select(AccessWindows.ForCarnivalDay))
                     .Where(m => m.StartAt <= clock.UtcNow.UtcDateTime).OrderByDescending(m => m.StartAt))
        {
            result.Add(await StatsAsync(moment, cancellationToken));
        }

        return result;
    }

    /// <summary>Dashboardblok: de lopende carnavalsdag/activiteit (live), anders de laatste met scans; plus klaar voor de deur.</summary>
    public async Task<AccessDashboard> DashboardAsync(CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        AccessStats? stats = current is null ? null : await StatsAsync(current, cancellationToken);
        if (stats is null)
        {
            var last = await db.AccessScans.AsNoTracking().OrderByDescending(s => s.ScannedAt).Select(s => new { s.EventId, s.CarnivalDay }).FirstOrDefaultAsync(cancellationToken);
            if (last is not null)
            {
                var moment = last.EventId is { } id
                    ? AccessWindows.ForEvent(await db.Events.AsNoTracking().SingleAsync(e => e.Id == id, cancellationToken))
                    : AccessWindows.ForCarnivalDay(last.CarnivalDay!.Value);
                stats = await StatsAsync(moment, cancellationToken);
            }
        }

        return new AccessDashboard(current is not null, stats, await ReadinessAsync(cancellationToken));
    }

    private async Task<DoorReadiness> ReadinessAsync(CancellationToken cancellationToken)
    {
        var year = await windows.ActiveYearAsync(cancellationToken);
        var active = await db.Members.CountAsync(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, cancellationToken);
        if (year is null)
        {
            return new DoorReadiness(active, 0, 0, active);
        }

        var bound = await (
            from t in db.Tickets.AsNoTracking()
            where t.CarnivalYearId == year.Id && t.BoundDeviceId != null
            join m in db.Members.AsNoTracking() on t.MemberId equals m.Id
            where (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active
            join d in db.Devices.AsNoTracking() on t.BoundDeviceId equals d.Id
            where d.Status == DeviceStatus.Active
            select d.PublicKey != null && MemberTickets.HardwareLevels.Contains(d.AttestationStatus!)).ToListAsync(cancellationToken);
        return new DoorReadiness(active, bound.Count, bound.Count(h => h), Math.Max(0, active - bound.Count));
    }
}
