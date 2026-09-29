using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Ticketing;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Toegangscontrole bij de deur (fase 14): scannen in de app en inchecken in het portal met <c>ticket.scan</c> (rol
/// Deurcontrole, OQ-73); de scanlog voor het bestuur met <c>ticket.read</c>.
/// </summary>
[ApiController]
[RequirePermission(Permissions.TicketScan)]
public sealed class AccessController(DoorAccess access) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    private bool Details => CurrentUser.Get(HttpContext)!.Permissions.Contains(Permissions.TicketScanDetails);

    /// <summary>De activiteit met toegangscontrole van dit moment (of de volgende) met de tellers.</summary>
    [HttpGet("api/v1/access/status")]
    [ProducesResponseType<AccessStatus>(StatusCodes.Status200OK)]
    public Task<AccessStatus> Status(CancellationToken cancellationToken) => access.StatusAsync(cancellationToken);

    [HttpPost("api/v1/access/scan")]
    [EnableRateLimiting(AuthorizationSetup.ScannerPolicy)]
    [ProducesResponseType<AccessResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AccessResult> Scan(ScanRequest request, CancellationToken cancellationToken) =>
        access.ScanAsync(UserId, Request.Headers[DeviceCheck.HeaderName].ToString(), request.Code, Details, cancellationToken);

    /// <summary>Controlelijst voor offline scannen (fase 15, lichte variant); de app houdt die alleen in het geheugen.</summary>
    [HttpGet("api/v1/access/offline-pack")]
    [ProducesResponseType<OfflinePack>(StatusCodes.Status200OK)]
    public Task<OfflinePack> OfflinePack(CancellationToken cancellationToken) => access.OfflinePackAsync(cancellationToken);

    /// <summary>Offline scans uit de wachtrij (idempotent op <c>clientScanId</c>).</summary>
    [HttpPost("api/v1/access/offline-scans")]
    [ProducesResponseType<OfflineSyncResult>(StatusCodes.Status200OK)]
    public Task<OfflineSyncResult> SyncOffline(OfflineScansRequest request, CancellationToken cancellationToken) =>
        access.SyncOfflineAsync(UserId, Request.Headers[DeviceCheck.HeaderName].ToString(), request.Scans, cancellationToken);

    /// <summary>"Toch toelaten" of "Weigeren" bij oranje.</summary>
    [HttpPost("api/v1/access/scans/{id:guid}/decision")]
    [ProducesResponseType<AccessCounts>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AccessCounts> Decide(Guid id, DecisionRequest request, CancellationToken cancellationToken) =>
        access.DecideAsync(UserId, id, request.Admit, cancellationToken);

    /// <summary>Toegangskaart bij een lid in het portal.</summary>
    [HttpGet("api/v1/admin/members/{memberId:guid}/access")]
    [ProducesResponseType<MemberAccess>(StatusCodes.Status200OK)]
    public Task<MemberAccess> Member(Guid memberId, CancellationToken cancellationToken) => access.MemberAsync(memberId, cancellationToken);

    /// <summary>Handmatig inchecken (leden zonder smartphone, OQ-23); al binnen → geen record tenzij <c>force</c>.</summary>
    [HttpPost("api/v1/admin/members/{memberId:guid}/check-in")]
    [ProducesResponseType<AccessResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<AccessResult> CheckIn(Guid memberId, CheckInRequest request, CancellationToken cancellationToken) =>
        access.CheckInAsync(UserId, memberId, request.Force, cancellationToken);
}

/// <summary>
/// Scanlog voor het bestuur (fase 14, <c>ticket.read</c>): per toegangsmoment — een activiteit met toegangscontrole of
/// een carnavalsdag — alle scans en inchecks met de tellers.
/// </summary>
[ApiController]
[Route("api/v1/admin/access-scans")]
[RequirePermission(Permissions.TicketRead)]
public sealed class AdminAccessScansController(DrammersDbContext db, DoorAccess access, AccessWindows windows) : ControllerBase
{
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<AccessEventSummary>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AccessEventSummary>> Events(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking().Where(e => e.AccessControl).OrderByDescending(e => e.StartAt).Take(50).ToListAsync(cancellationToken);
        var days = await db.AccessScans.AsNoTracking().Where(s => s.CarnivalDay != null).Select(s => s.CarnivalDay!.Value).Distinct().ToListAsync(cancellationToken);
        var moments = events.Select(AccessWindows.ForEvent).Concat(days.Select(AccessWindows.ForCarnivalDay)).ToList();
        // Ook de lopende carnavalsdag, ook als er nog niet gescand is.
        if (await windows.CurrentAsync(cancellationToken) is { } current && moments.All(m => m.Key != current.Key))
        {
            moments.Add(current);
        }

        var result = new List<AccessEventSummary>();
        foreach (var m in moments.OrderByDescending(m => m.StartAt))
        {
            result.Add(new AccessEventSummary(m.Key, m.Title, m.StartAt, m.EndAt, await access.CountsAsync(m, cancellationToken)));
        }

        return result;
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AccessScanRow>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AccessScanRow>> Search([FromQuery, Required, StringLength(40)] string key, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<AccessScanRow>.Normalize(page, pageSize);
        var scans = db.AccessScans.AsNoTracking();
        scans = AccessWindows.TryParseDayKey(key, out var day)
            ? scans.Where(s => s.EventId == null && s.CarnivalDay == day)
            : Guid.TryParse(key, out var eventId) ? scans.Where(s => s.EventId == eventId) : scans.Where(_ => false);
        var query =
            from s in scans
            join m in db.Members.AsNoTracking() on s.MemberId equals m.Id into ms
            from m in ms.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on s.OperatorUserId equals u.Id
            join d in db.Devices.AsNoTracking() on s.OperatorDeviceId equals d.Id into ds
            from d in ds.DefaultIfEmpty()
            orderby s.ScannedAt descending
            select new AccessScanRow(s.Id, s.ScannedAt, m == null ? null : m.FullName, s.Method, s.Outcome, s.Reason, s.Decision, u.DisplayName, d == null ? null : d.Name,
                s.Offline, s.OfflineOutcome);
        return new PagedResult<AccessScanRow>(await query.Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken), p, size, await query.CountAsync(cancellationToken));
    }
}

public sealed record ScanRequest([Required, StringLength(300)] string Code);

public sealed record OfflineScansRequest([Required, MaxLength(500)] IReadOnlyList<OfflineScan> Scans);

public sealed record DecisionRequest(bool Admit);

public sealed record CheckInRequest(bool Force = false);

public sealed record AccessEventSummary(string Key, string Title, DateTime StartAt, DateTime? EndAt, AccessCounts Counts);

public sealed record AccessScanRow(
    Guid Id, DateTime ScannedAt, string? MemberName, AccessMethod Method, AccessOutcome Outcome, string? Reason, AccessDecision? Decision,
    string OperatorName, string? DeviceName, bool Offline, AccessOutcome? OfflineOutcome);
