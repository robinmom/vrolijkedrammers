using System.Globalization;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Ticketing.Qr;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Ticketing;

public sealed record AccessCounts(int Inside, int Scans, int Refused);

public sealed record AccessStatus(AccessEvent? Current, AccessEvent? Next, AccessCounts? Counts);

/// <summary>Wat het deurpersoneel na een scan of inchecken ziet (fase 14, Figma 📷 Toegangscontrole).</summary>
public sealed record AccessResult(
    Guid? ScanId, AccessOutcome Outcome, string Title, string Message, string? HolderName, DateTime? PreviousAt, bool NeedsDecision,
    AccessCounts Counts);

public sealed record AccessHistoryItem(DateTime At, AccessMethod Method, AccessOutcome Outcome, AccessDecision? Decision, string EventTitle, string? Operator);

public sealed record MemberAccess(AccessEvent? Current, bool Inside, DateTime? InsideSince, string? TicketProblem, IReadOnlyList<AccessHistoryItem> History);

/// <summary>
/// Toegangscontrole bij de deur (fase 14): QR-codes scannen in de app en leden inchecken in het portal, in één
/// toegangslog per toegangsmoment: een activiteit met toegangscontrole, of anders de carnavalsdag
/// (<see cref="AccessWindows"/>; QR en scannen volgen dezelfde regels). Groen bij de eerste keer (of opnieuw op hetzelfde toestel), oranje
/// als het lid al via een ander toestel of handmatig binnen is (het deurpersoneel beslist), rood met de reden.
/// Geen bandjes (OQ-21); wie de rol Deurcontrole heeft mag scannen (OQ-73).
/// </summary>
public sealed class DoorAccess(DrammersDbContext db, TicketValidation validation, AccessWindows windows, IClock clock)
{
    private static readonly TimeZoneInfo Loil = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    private DateTime Now => clock.UtcNow.UtcDateTime;

    public async Task<AccessStatus> StatusAsync(CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        var next = current is null ? await windows.NextAsync(cancellationToken) : null;
        return new AccessStatus(current, next, current is null ? null : await CountsAsync(current, cancellationToken));
    }

    private async Task<AccessEvent> RequireCurrentAsync(CancellationToken cancellationToken) =>
        await windows.CurrentAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.AccessNotActive, "Er is nu geen carnaval en geen activiteit met toegangscontrole.", DomainErrorKind.Conflict);

    /// <summary>Scans van één toegangsmoment: een activiteit of een carnavalsdag.</summary>
    private IQueryable<AccessScan> ScansOf(AccessEvent access) =>
        access.EventId is { } id
            ? db.AccessScans.AsNoTracking().Where(s => s.EventId == id)
            : db.AccessScans.AsNoTracking().Where(s => s.EventId == null && s.CarnivalDay == access.CarnivalDay);

    public async Task<AccessCounts> CountsAsync(AccessEvent access, CancellationToken cancellationToken)
    {
        var scans = await ScansOf(access).Select(s => new { s.MemberId, s.Outcome, s.Decision }).ToListAsync(cancellationToken);
        var inside = scans.Where(s => s.Outcome is AccessOutcome.Admitted or AccessOutcome.AdmittedAgain
                || (s.Outcome == AccessOutcome.Warning && s.Decision == AccessDecision.Admitted))
            .Select(s => s.MemberId).Distinct().Count();
        var refused = scans.Count(s => s.Outcome == AccessOutcome.Refused || s.Decision == AccessDecision.Refused);
        return new AccessCounts(inside, scans.Count, refused);
    }

    private async Task<AccessScan?> LastAdmissionAsync(AccessEvent access, Guid memberId, CancellationToken cancellationToken) =>
        (await ScansOf(access).Where(s => s.MemberId == memberId).OrderBy(s => s.ScannedAt).ToListAsync(cancellationToken)).FirstOrDefault(s => s.Admits);

    private static AccessScan NewScan(AccessEvent access) => new()
    {
        Id = IdGenerator.NewId(),
        EventId = access.EventId,
        CarnivalDay = access.EventId is null ? access.CarnivalDay : null,
    };

    private static string Time(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(utc, Loil).ToString("HH:mm", CultureInfo.InvariantCulture);

    private async Task<string> WhoAsync(AccessScan scan, bool details, CancellationToken cancellationToken)
    {
        if (!details)
        {
            return "";
        }

        var user = await db.Users.AsNoTracking().Where(u => u.Id == scan.OperatorUserId).Select(u => u.DisplayName).SingleOrDefaultAsync(cancellationToken);
        var device = scan.OperatorDeviceId is { } d ? await db.Devices.AsNoTracking().Where(x => x.Id == d).Select(x => x.Name).SingleOrDefaultAsync(cancellationToken) : null;
        return $" ({string.Join(", ", new[] { user, device }.Where(x => !string.IsNullOrWhiteSpace(x)))})";
    }

    /// <summary>Scan in de app. <paramref name="details"/> = het deurpersoneel mag zien wie eerder scande (<c>ticket.scan.details</c>).</summary>
    public async Task<AccessResult> ScanAsync(Guid operatorId, string? installationId, string code, bool details, CancellationToken cancellationToken)
    {
        var accessEvent = await RequireCurrentAsync(cancellationToken);
        var device = string.IsNullOrWhiteSpace(installationId)
            ? null
            : await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.UserId == operatorId && d.InstallationId == installationId && d.Status == DeviceStatus.Active, cancellationToken);
        var check = await validation.ValidateAsync(code, cancellationToken);
        var payload = QrPayload.TryDecode(code);
        var ticket = payload is null ? null : await db.Tickets.AsNoTracking().SingleOrDefaultAsync(t => t.PublicRef == payload.Ref, cancellationToken);
        var scan = NewScan(accessEvent);
        scan.TicketId = ticket?.Id;
        scan.MemberId = ticket?.MemberId;
        scan.Method = AccessMethod.Qr;
        scan.OperatorUserId = operatorId;
        scan.OperatorDeviceId = device?.Id;
        scan.ScannedAt = Now;

        string title, message;
        DateTime? previousAt = null;
        if (!check.IsValid)
        {
            (scan.Outcome, scan.Reason, title, message) = (AccessOutcome.Refused, check.Result.ToString(), "Geen toegang", check.Message);
        }
        else
        {
            var previous = await LastAdmissionAsync(accessEvent, ticket!.MemberId, cancellationToken);
            previousAt = previous?.ScannedAt;
            if (previous is null)
            {
                (scan.Outcome, title, message) = (AccessOutcome.Admitted, "Toegang geldig", "Eerste keer vanavond");
            }
            else if (previous.Method == AccessMethod.Qr && device is not null && previous.OperatorDeviceId == device.Id)
            {
                (scan.Outcome, title, message) = (AccessOutcome.AdmittedAgain, "Toegang geldig", $"Al eerder gescand op dit toestel om {Time(previous.ScannedAt)}");
            }
            else
            {
                scan.Outcome = AccessOutcome.Warning;
                scan.Reason = previous.Method == AccessMethod.Manual ? "CheckedInManually" : "OtherDevice";
                title = "Let op";
                message = previous.Method == AccessMethod.Manual
                    ? $"Vanavond al ingecheckt om {Time(previous.ScannedAt)}{await WhoAsync(previous, details, cancellationToken)}. Controleer of dit dezelfde persoon is."
                    : $"Vanavond al gescand op een ander toestel om {Time(previous.ScannedAt)}{await WhoAsync(previous, details, cancellationToken)}. Controleer of dit dezelfde persoon is.";
            }
        }

        db.AccessScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessResult(scan.Id, scan.Outcome, title, message, check.Ticket?.HolderName, previousAt, scan.Outcome == AccessOutcome.Warning,
            await CountsAsync(accessEvent, cancellationToken));
    }

    /// <summary>"Toch toelaten" of "Weigeren" bij oranje; alleen door wie scande en maar één keer.</summary>
    public async Task<AccessCounts> DecideAsync(Guid operatorId, Guid scanId, bool admit, CancellationToken cancellationToken)
    {
        var scan = await db.AccessScans.SingleOrDefaultAsync(s => s.Id == scanId && s.OperatorUserId == operatorId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Scan niet gevonden.", DomainErrorKind.NotFound);
        if (scan.Outcome != AccessOutcome.Warning || scan.Decision is not null)
        {
            throw new DomainException(ErrorCodes.InvalidStatusTransition, "Over deze scan is al beslist.", DomainErrorKind.Conflict);
        }

        scan.Decision = admit ? AccessDecision.Admitted : AccessDecision.Refused;
        scan.DecidedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
        var access = scan.EventId is { } eventId
            ? AccessWindows.ForEvent(await db.Events.AsNoTracking().SingleAsync(e => e.Id == eventId, cancellationToken))
            : AccessWindows.ForCarnivalDay(scan.CarnivalDay!.Value);
        return await CountsAsync(access, cancellationToken);
    }

    /// <summary>Toegangskaart bij een lid in het portal: actieve activiteit, al binnen en de toegangshistorie van dit carnavalsjaar.</summary>
    public async Task<MemberAccess> MemberAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var current = await windows.CurrentAsync(cancellationToken);
        var inside = current is null ? null : await LastAdmissionAsync(current, memberId, cancellationToken);
        var problem = await TicketProblemAsync(memberId, cancellationToken);
        var rows = await (
            from s in db.AccessScans.AsNoTracking()
            where s.MemberId == memberId
            join e in db.Events.AsNoTracking() on s.EventId equals e.Id into es
            from e in es.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on s.OperatorUserId equals u.Id
            orderby s.ScannedAt descending
            select new { s.ScannedAt, s.Method, s.Outcome, s.Decision, EventTitle = e == null ? null : e.Title, s.CarnivalDay, u.DisplayName })
            .Take(20).ToListAsync(cancellationToken);
        var history = rows.Select(r => new AccessHistoryItem(r.ScannedAt, r.Method, r.Outcome, r.Decision,
            r.EventTitle ?? AccessWindows.ForCarnivalDay(r.CarnivalDay!.Value).Title, r.DisplayName)).ToList();
        return new MemberAccess(current, inside is not null, inside?.ScannedAt, problem, history);
    }

    /// <summary>Waarom een lid geen toegang heeft (niet actief, ticket geblokkeerd); <c>null</c> als het in orde is.</summary>
    private async Task<string?> TicketProblemAsync(Guid memberId, CancellationToken cancellationToken)
    {
        var member = await db.Members.AsNoTracking().SingleOrDefaultAsync(m => m.Id == memberId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        if (member.MembershipStatus != MembershipStatus.Active)
        {
            return "Geen actief lidmaatschap.";
        }

        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        var blocked = year is not null && await db.Tickets.AnyAsync(t => t.CarnivalYearId == year && t.MemberId == memberId && t.Status == TicketStatus.Blocked, cancellationToken);
        return blocked ? "Ticket geblokkeerd." : null;
    }

    /// <summary>
    /// Handmatig inchecken in het portal (leden zonder smartphone, OQ-23). Is het lid al binnen, dan niets vastleggen
    /// tenzij <paramref name="force"/> ("Toch opnieuw inchecken").
    /// </summary>
    public async Task<AccessResult> CheckInAsync(Guid operatorId, Guid memberId, bool force, CancellationToken cancellationToken)
    {
        var accessEvent = await RequireCurrentAsync(cancellationToken);
        var member = await db.Members.AsNoTracking().Where(m => m.Id == memberId).Select(m => m.FullName).SingleOrDefaultAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotFound, "Lid niet gevonden.", DomainErrorKind.NotFound);
        var problem = await TicketProblemAsync(memberId, cancellationToken);
        if (problem is not null)
        {
            throw new DomainException(ErrorCodes.TicketUnavailable, $"Geen toegang: {problem}", DomainErrorKind.Conflict);
        }

        var previous = await LastAdmissionAsync(accessEvent, memberId, cancellationToken);
        if (previous is not null && !force)
        {
            return new AccessResult(null, AccessOutcome.Warning, "Al binnen", $"{member} is vanavond al binnen sinds {Time(previous.ScannedAt)}.",
                member, previous.ScannedAt, true, await CountsAsync(accessEvent, cancellationToken));
        }

        var year = await db.CarnivalYears.AsNoTracking().Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);
        var ticketId = year is null ? null : await db.Tickets.Where(t => t.CarnivalYearId == year && t.MemberId == memberId).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(cancellationToken);
        var scan = NewScan(accessEvent);
        scan.TicketId = ticketId;
        scan.MemberId = memberId;
        scan.Method = AccessMethod.Manual;
        scan.Outcome = previous is null ? AccessOutcome.Admitted : AccessOutcome.AdmittedAgain;
        scan.OperatorUserId = operatorId;
        scan.ScannedAt = Now;
        db.AccessScans.Add(scan);
        await db.SaveChangesAsync(cancellationToken);
        return new AccessResult(scan.Id, scan.Outcome, "Ingecheckt", $"{member} is ingecheckt om {Time(scan.ScannedAt)}.", member, previous?.ScannedAt, false,
            await CountsAsync(accessEvent, cancellationToken));
    }
}
