using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Gebruikers: beheerders aanmaken (fase 3), rollen toewijzen, blokkeren (docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(DrammersDbContext db, AccountAdministration administration) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<IReadOnlyList<UserSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<UserSummaryResponse>> Search([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u => u.Email.Contains(search) || u.DisplayName.Contains(search));
        }

        return await query.OrderBy(u => u.DisplayName).Take(100)
            .Select(u => new UserSummaryResponse(u.Id, u.Email, u.DisplayName, u.AccountStatus.ToString(), u.LastLoginAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Beheerder aanmaken of een bestaand Entra-account koppelen (ADR-014, bron Manual; idempotent op e-mail).</summary>
    [HttpPost]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserSummaryResponse>> Provision(ProvisionUserRequest request, CancellationToken cancellationToken)
    {
        var userId = await administration.ProvisionAdministratorAsync(request.Email, request.DisplayName, request.Roles, cancellationToken);
        var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == userId, cancellationToken);
        return Created($"/api/v1/admin/users/{userId}/roles",
            new UserSummaryResponse(user.Id, user.Email, user.DisplayName, user.AccountStatus.ToString(), user.LastLoginAt));
    }

    [HttpGet("{id:guid}/roles")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<IReadOnlyList<UserRoleResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<UserRoleResponse>> GetRoles(Guid id, CancellationToken cancellationToken)
    {
        if (!await db.Users.AnyAsync(u => u.Id == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);
        }

        return await db.UserRoles.AsNoTracking().Where(ur => ur.UserId == id)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new UserRoleResponse(r.Code, r.Name, ur.ValidFrom, ur.ValidTo))
            .ToListAsync(cancellationToken);
    }

    [HttpPut("{id:guid}/roles")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken cancellationToken)
    {
        await administration.SetUserRolesAsync(id, [.. request.Roles.Select(r => new RoleAssignment(r.RoleCode, r.ValidFrom, r.ValidTo))], cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/block")]
    [RequirePermission(Permissions.MemberBlock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Block(Guid id, CancellationToken cancellationToken)
    {
        await administration.BlockAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unblock")]
    [RequirePermission(Permissions.MemberBlock)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken cancellationToken)
    {
        await administration.UnblockAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record UserSummaryResponse(Guid Id, string Email, string DisplayName, string AccountStatus, DateTime? LastLoginAt);

public sealed record ProvisionUserRequest(
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(200, MinimumLength = 2)] string DisplayName,
    [Required, MinLength(1)] IReadOnlyList<string> Roles);

public sealed record UserRoleResponse(string Code, string Name, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record SetUserRolesRequest([Required] IReadOnlyList<RoleAssignmentRequest> Roles);

public sealed record RoleAssignmentRequest([Required] string RoleCode, DateOnly? ValidFrom, DateOnly? ValidTo);
