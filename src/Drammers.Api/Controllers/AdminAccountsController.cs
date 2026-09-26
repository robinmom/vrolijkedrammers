using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.AccountRequests;
using Drammers.Modules.Identity.Provisioning;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Accountverzoeken beoordelen en de provisioning volgen (fase 9, ADR-014).</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminAccountsController(DrammersDbContext db, MemberAccounts accounts, MyAccount myAccount) : ControllerBase
{
    [HttpGet("account-requests")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType<PagedResult<AccountRequestResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<AccountRequestResponse>> GetRequests(
        [FromQuery] AccountRequestStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<AccountRequestResponse>.Normalize(page, pageSize);
        var query = db.AccountRequests.AsNoTracking().Where(r => status == null || r.Status == status);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(r => r.RequestedAt).Skip((p - 1) * size).Take(size)
            .GroupJoin(db.Members, r => r.MemberId, m => m.Id, (r, ms) => new { r, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new { x.r, member = m })
            .ToListAsync(cancellationToken);

        // Suggestie voor het bestuur: het lid met het ingevulde lidnummer (ook bij een verkeerd e-mailadres).
        var numbers = rows.Select(x => x.r.MemberNumber).Distinct().ToList();
        var byNumber = await db.Members.AsNoTracking().Where(m => numbers.Contains(m.MemberNumber))
            .ToDictionaryAsync(m => m.MemberNumber, cancellationToken);
        var items = rows.Select(x =>
        {
            var suggested = x.member ?? byNumber.GetValueOrDefault(x.r.MemberNumber);
            return new AccountRequestResponse(
                x.r.Id, x.r.MemberNumber, x.r.Email, x.r.Status, x.r.MismatchReason, x.r.RejectionReason, x.r.RequestedAt, x.r.DecidedAt,
                suggested is null ? null : new AccountRequestMemberResponse(suggested.Id, suggested.MemberNumber, suggested.FullName, suggested.Email, suggested.LocalStatusOverride ?? suggested.MembershipStatus));
        }).ToList();
        return new PagedResult<AccountRequestResponse>(items, p, size, total);
    }

    [HttpPost("account-requests/{id:guid}/approve")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, ApproveAccountRequestRequest request, CancellationToken cancellationToken)
    {
        await accounts.ApproveAccountRequestAsync(id, request.MemberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("account-requests/{id:guid}/reject")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, RejectAccountRequestRequest request, CancellationToken cancellationToken)
    {
        await accounts.RejectAccountRequestAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Direct een account maken voor een lid (bron Manual; e-mailadres uit e-Boekhouden).</summary>
    [HttpPost("members/{id:guid}/provision-account")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType<ProvisioningStartedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<AcceptedResult> ProvisionAccount(Guid id, CancellationToken cancellationToken) =>
        Accepted((string?)null, new ProvisioningStartedResponse(await accounts.ProvisionForMemberAsync(id, cancellationToken)));

    [HttpGet("account-provisioning")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType<IReadOnlyList<ProvisioningResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ProvisioningResponse>> GetProvisioning([FromQuery] bool openOnly = true, CancellationToken cancellationToken = default) =>
        await db.AccountProvisioning.AsNoTracking()
            .Where(p => p.Kind == ProvisioningKind.Member && (!openOnly || p.Step != ProvisioningStep.Completed))
            .OrderByDescending(p => p.CreatedAt).Take(200)
            .GroupJoin(db.Members, p => p.MemberId, m => m.Id, (p, ms) => new { p, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new ProvisioningResponse(
                x.p.Id, x.p.SourceType, x.p.Step, x.p.MemberId, m == null ? null : m.FullName, x.p.MemberNumber, x.p.Attempts, x.p.LastError,
                x.p.CreatedAt, x.p.CompletedAt))
            .ToListAsync(cancellationToken);

    [HttpPost("account-provisioning/{id:guid}/retry")]
    [RequirePermission(Permissions.MemberApprove)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Retry(Guid id, CancellationToken cancellationToken)
    {
        await accounts.RetryAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpGet("users/{id:guid}/devices")]
    [RequirePermission(Permissions.MemberBlock)]
    [ProducesResponseType<IReadOnlyList<DeviceResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DeviceResponse>> GetDevices(Guid id, CancellationToken cancellationToken)
    {
        var devices = await db.Devices.AsNoTracking().Where(d => d.UserId == id)
            .OrderBy(d => d.Status).ThenByDescending(d => d.LastSeenAt).ToListAsync(cancellationToken);
        return [.. devices.Select(d => DeviceResponse.From(d, current: false))];
    }

    [HttpPost("devices/{id:guid}/revoke")]
    [RequirePermission(Permissions.MemberBlock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevokeDevice(Guid id, CancellationToken cancellationToken)
    {
        await myAccount.RevokeDeviceAsync(null, id, cancellationToken);
        return NoContent();
    }
}

public sealed record AccountRequestMemberResponse(Guid Id, string MemberNumber, string FullName, string? Email, Modules.Membership.Members.MembershipStatus Status);

public sealed record AccountRequestResponse(
    Guid Id, string MemberNumber, string Email, AccountRequestStatus Status, string? MismatchReason, string? RejectionReason,
    DateTime RequestedAt, DateTime? DecidedAt, AccountRequestMemberResponse? Member);

public sealed record ApproveAccountRequestRequest([param: Required] Guid MemberId);

public sealed record RejectAccountRequestRequest([param: StringLength(500)] string? Reason);

public sealed record ProvisioningStartedResponse(Guid ProvisioningId);

public sealed record ProvisioningResponse(
    Guid Id, ProvisioningSourceType SourceType, ProvisioningStep Step, Guid? MemberId, string? MemberName, string? MemberNumber,
    int Attempts, string? LastError, DateTime CreatedAt, DateTime? CompletedAt);
