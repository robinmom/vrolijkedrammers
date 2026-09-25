using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Auditing;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Auditlog lezen (read-only), met gemaskeerde gevoelige velden (docs/07 §6, fase 4).</summary>
[ApiController]
[Route("api/v1/admin/audit-log")]
[RequirePermission(Permissions.AuditRead)]
public sealed class AdminAuditLogController(DrammersDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AuditLogEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AuditLogEntryResponse>> Search(
        [FromQuery] string? action,
        [FromQuery] string? entityType,
        [FromQuery] string? entityId,
        [FromQuery] Guid? actorUserId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<AuditLogEntryResponse>.Normalize(page, pageSize);
        var query = db.AuditLog.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action.StartsWith(action));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(a => a.EntityId == entityId);
        }

        if (actorUserId is { } actor)
        {
            query = query.Where(a => a.ActorUserId == actor);
        }

        if (from is { } f)
        {
            query = query.Where(a => a.OccurredAt >= f.ToUniversalTime());
        }

        if (to is { } t)
        {
            query = query.Where(a => a.OccurredAt < t.ToUniversalTime());
        }

        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(a => a.Id).Skip((p - 1) * size).Take(size)
            .GroupJoin(db.Users, a => a.ActorUserId, u => u.Id, (a, users) => new { a, users })
            .SelectMany(x => x.users.DefaultIfEmpty(), (x, u) => new { x.a, ActorName = u == null ? null : u.DisplayName })
            .ToListAsync(cancellationToken);

        return new PagedResult<AuditLogEntryResponse>(
            [.. rows.Select(r => new AuditLogEntryResponse(
                r.a.Id, r.a.OccurredAt, r.a.ActorType.ToString(), r.a.ActorUserId, r.ActorName, r.a.Action, r.a.EntityType, r.a.EntityId,
                AuditMasking.Apply(r.a.OldValues), AuditMasking.Apply(r.a.NewValues)))],
            p, size, total);
    }
}

public sealed record AuditLogEntryResponse(
    long Id, DateTime OccurredAt, string ActorType, Guid? ActorUserId, string? ActorName, string Action, string EntityType, string EntityId,
    string? OldValues, string? NewValues);
