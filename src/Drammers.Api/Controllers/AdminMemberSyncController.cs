using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Import.Sync;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Ledensync met e-Boekhouden starten en volgen, conflicten afhandelen, mapping instellen (fase 8, ADR-010).</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminMemberSyncController(
    DrammersDbContext db, MemberSync sync, MemberSyncSettings settings, MemberAdministration members, ICurrentActor actor) : ControllerBase
{
    /// <summary>Start een run (standaard een dry-run); de worker voert hem uit. Volg de status via <c>/sync-jobs/{id}</c>.</summary>
    [HttpPost("members/import")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedResponse>> Import([FromQuery] bool dryRun = true, CancellationToken cancellationToken = default)
    {
        var id = await sync.RequestAsync(dryRun, SyncTrigger.Manual, actor.UserId, cancellationToken);
        return Accepted($"/api/v1/admin/sync-jobs/{id}", new CreatedResponse(id));
    }

    [HttpGet("sync-jobs")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType<PagedResult<SyncJobResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<SyncJobResponse>> Jobs([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<SyncJobResponse>.Normalize(page, pageSize);
        var total = await db.SyncJobs.CountAsync(cancellationToken);
        var items = await db.SyncJobs.AsNoTracking().OrderByDescending(j => j.RequestedAt).Skip((p - 1) * size).Take(size)
            .Select(ToResponse).ToListAsync(cancellationToken);
        return new PagedResult<SyncJobResponse>(items, p, size, total);
    }

    [HttpGet("sync-jobs/{id:guid}")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType<SyncJobResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<SyncJobResponse> Job(Guid id, CancellationToken cancellationToken) =>
        await db.SyncJobs.AsNoTracking().Where(j => j.Id == id).Select(ToResponse).SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NotFound, "Syncrun niet gevonden.", DomainErrorKind.NotFound);

    [HttpGet("sync-jobs/{id:guid}/items")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType<PagedResult<SyncJobItemResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<SyncJobItemResponse>> Items(
        Guid id, [FromQuery] SyncItemAction? action, [FromQuery] bool? includeUnchanged, [FromQuery] int? page, [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<SyncJobItemResponse>.Normalize(page, pageSize);
        var query = db.SyncJobItems.AsNoTracking().Where(i => i.SyncJobId == id);
        if (action is { } a)
        {
            query = query.Where(i => i.Action == a);
        }
        else if (includeUnchanged != true)
        {
            // Standaard alleen wat aandacht vraagt of veranderd is; "ongewijzigd" is bij een gewone run het grootste deel.
            query = query.Where(i => i.Action != SyncItemAction.Unchanged);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(i => i.Id).Skip((p - 1) * size).Take(size)
            .Select(i => new SyncJobItemResponse(i.Id, i.MemberNumber, i.MemberId, i.Action, i.ChangedFields, i.Message))
            .ToListAsync(cancellationToken);
        return new PagedResult<SyncJobItemResponse>(items, p, size, total);
    }

    [HttpGet("sync-conflicts")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType<IReadOnlyList<SyncConflictResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<SyncConflictResponse>> Conflicts([FromQuery] SyncConflictStatus? status, CancellationToken cancellationToken)
    {
        var query = db.SyncConflicts.AsNoTracking();
        query = status is { } s ? query.Where(c => c.Status == s) : query.Where(c => c.Status == SyncConflictStatus.Open);
        return await query.OrderByDescending(c => c.CreatedAt).Take(500)
            .Select(c => new SyncConflictResponse(c.Id, c.SyncJobId, c.Type, c.MemberNumber, c.MemberId, c.Details, c.Status, c.CreatedAt, c.ResolvedAt, c.ResolutionNote))
            .ToListAsync(cancellationToken);
    }

    [HttpPost("sync-conflicts/{id:guid}/resolve")]
    [RequirePermission(Permissions.ImportRun)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Resolve(Guid id, ResolveConflictRequest request, CancellationToken cancellationToken)
    {
        await members.ResolveConflictAsync(id, request.Resolution, request.Note, actor.UserId, cancellationToken);
        return NoContent();
    }

    [HttpGet("config/member-mapping")]
    [RequirePermission(Permissions.ConfigManage)]
    [ProducesResponseType<MemberFieldMapping>(StatusCodes.Status200OK)]
    public Task<MemberFieldMapping> GetMapping(CancellationToken cancellationToken) => settings.GetMappingAsync(cancellationToken);

    /// <summary>Mapping van de vrije velden wijzigen; daarna eerst een dry-run doen (ADR-010).</summary>
    [HttpPut("config/member-mapping")]
    [RequirePermission(Permissions.ConfigManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetMapping(MemberFieldMapping mapping, CancellationToken cancellationToken)
    {
        await settings.SetMappingAsync(mapping, cancellationToken);
        return NoContent();
    }

    private static readonly System.Linq.Expressions.Expression<Func<SyncJob, SyncJobResponse>> ToResponse = j => new SyncJobResponse(
        j.Id, j.Status, j.DryRun, j.Trigger, j.RequestedAt, j.StartedAt, j.CompletedAt, j.TotalInSource, j.Created, j.Updated,
        j.Unchanged, j.Missing, j.Deactivated, j.Reactivated, j.Warnings, j.Errors, j.Conflicts, j.ErrorMessage);
}

public sealed record SyncJobResponse(
    Guid Id, SyncJobStatus Status, bool DryRun, SyncTrigger Trigger, DateTime RequestedAt, DateTime? StartedAt, DateTime? CompletedAt,
    int TotalInSource, int Created, int Updated, int Unchanged, int Missing, int Deactivated, int Reactivated, int Warnings, int Errors,
    int Conflicts, string? ErrorMessage);

public sealed record SyncJobItemResponse(long Id, string MemberNumber, Guid? MemberId, SyncItemAction Action, string? ChangedFields, string? Message);

public sealed record SyncConflictResponse(
    Guid Id, Guid SyncJobId, SyncConflictType Type, string? MemberNumber, Guid? MemberId, string Details, SyncConflictStatus Status,
    DateTime CreatedAt, DateTime? ResolvedAt, string? ResolutionNote);

public sealed record ResolveConflictRequest(SyncConflictStatus Resolution, string? Note);
