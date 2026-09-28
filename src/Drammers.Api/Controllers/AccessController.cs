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

/// <summary>Scanlog voor het bestuur (fase 14, <c>ticket.read</c>): per activiteit met toegangscontrole.</summary>
[ApiController]
[Route("api/v1/admin/access-scans")]
[RequirePermission(Permissions.TicketRead)]
public sealed class AdminAccessScansController(DrammersDbContext db, DoorAccess access) : ControllerBase
{
    [HttpGet("events")]
    [ProducesResponseType<IReadOnlyList<AccessEventSummary>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AccessEventSummary>> Events(CancellationToken cancellationToken)
    {
        var events = await db.Events.AsNoTracking().Where(e => e.AccessControl).OrderByDescending(e => e.StartAt).Take(50)
            .Select(e => new { e.Id, e.Title, e.StartAt, e.EndAt }).ToListAsync(cancellationToken);
        var result = new List<AccessEventSummary>();
        foreach (var e in events)
        {
            result.Add(new AccessEventSummary(e.Id, e.Title, e.StartAt, e.EndAt, await access.CountsAsync(e.Id, cancellationToken)));
        }

        return result;
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AccessScanRow>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AccessScanRow>> Search([FromQuery, Required] Guid eventId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<AccessScanRow>.Normalize(page, pageSize);
        var query =
            from s in db.AccessScans.AsNoTracking()
            where s.EventId == eventId
            join m in db.Members.AsNoTracking() on s.MemberId equals m.Id into ms
            from m in ms.DefaultIfEmpty()
            join u in db.Users.AsNoTracking() on s.OperatorUserId equals u.Id
            join d in db.Devices.AsNoTracking() on s.OperatorDeviceId equals d.Id into ds
            from d in ds.DefaultIfEmpty()
            orderby s.ScannedAt descending
            select new AccessScanRow(s.Id, s.ScannedAt, m == null ? null : m.FullName, s.Method, s.Outcome, s.Reason, s.Decision, u.DisplayName, d == null ? null : d.Name);
        return new PagedResult<AccessScanRow>(await query.Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken), p, size, await query.CountAsync(cancellationToken));
    }
}

public sealed record ScanRequest([Required, StringLength(300)] string Code);

public sealed record DecisionRequest(bool Admit);

public sealed record CheckInRequest(bool Force = false);

public sealed record AccessEventSummary(Guid Id, string Title, DateTime StartAt, DateTime? EndAt, AccessCounts Counts);

public sealed record AccessScanRow(
    Guid Id, DateTime ScannedAt, string? MemberName, AccessMethod Method, AccessOutcome Outcome, string? Reason, AccessDecision? Decision,
    string OperatorName, string? DeviceName);
