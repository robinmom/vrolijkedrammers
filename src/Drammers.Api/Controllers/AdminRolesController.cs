using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Permissions en rollen beheren (docs/05 §6, docs/07 §6).</summary>
[ApiController]
[Route("api/v1/admin")]
[RequirePermission(Permissions.RoleManage)]
public sealed class AdminRolesController(DrammersDbContext db, AccountAdministration administration) : ControllerBase
{
    [HttpGet("permissions")]
    [ProducesResponseType<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<PermissionResponse>> GetPermissions(CancellationToken cancellationToken) =>
        await db.Permissions.AsNoTracking().OrderBy(p => p.Id)
            .Select(p => new PermissionResponse(p.Code, p.Description, p.Category))
            .ToListAsync(cancellationToken);

    [HttpGet("roles")]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<RoleResponse>> GetRoles(CancellationToken cancellationToken)
    {
        var roles = await db.Roles.AsNoTracking().Include(r => r.Permissions).OrderBy(r => r.SortOrder).ToListAsync(cancellationToken);
        var codes = await db.Permissions.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);
        return [.. roles.Select(r => new RoleResponse(
            r.Id, r.Code, r.Name, r.Description, r.IsSystem,
            [.. r.Permissions.Select(p => codes[p.PermissionId]).Order(StringComparer.Ordinal)]))];
    }

    [HttpPost("roles")]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RoleResponse>> CreateRole(CreateRoleRequest request, CancellationToken cancellationToken)
    {
        var role = await administration.CreateRoleAsync(request.Code, request.Name, request.Description, request.Permissions, cancellationToken);
        var response = new RoleResponse(role.Id, role.Code, role.Name, role.Description, role.IsSystem, [.. request.Permissions.Distinct().Order(StringComparer.Ordinal)]);
        return Created($"/api/v1/admin/roles/{role.Id}", response);
    }

    [HttpPut("roles/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateRole(int id, UpdateRoleRequest request, CancellationToken cancellationToken)
    {
        await administration.UpdateRoleAsync(id, request.Name, request.Description, cancellationToken);
        return NoContent();
    }

    [HttpDelete("roles/{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteRole(int id, CancellationToken cancellationToken)
    {
        await administration.DeleteRoleAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("roles/{id:int}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetRolePermissions(int id, SetRolePermissionsRequest request, CancellationToken cancellationToken)
    {
        await administration.SetRolePermissionsAsync(id, request.Permissions, cancellationToken);
        return NoContent();
    }

    /// <summary>De gebruikers die deze rol hebben (geen verwijderde accounts), met of hun toewijzing tijdelijk is.</summary>
    [HttpGet("roles/{id:int}/members")]
    [ProducesResponseType<IReadOnlyList<RoleMemberResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<RoleMemberResponse>> GetRoleMembers(int id, CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == id, cancellationToken))
        {
            throw new DomainException(ErrorCodes.RoleNotFound, "Rol niet gevonden.", DomainErrorKind.NotFound);
        }

        return await db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == id)
            .Join(db.Users.Where(u => u.AccountStatus != AccountStatus.Deleted), ur => ur.UserId, u => u.Id,
                (ur, u) => new { u.Id, u.DisplayName, u.Email, ur.ValidTo })
            .OrderBy(x => x.DisplayName)
            .Select(x => new RoleMemberResponse(x.Id, x.DisplayName, x.Email, x.ValidTo != null))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Voegt de rol toe aan een gebruiker zonder zijn andere rollen te raken (no-op als hij de rol al heeft).</summary>
    [HttpPost("roles/{id:int}/members")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddRoleMember(int id, AddRoleMemberRequest request, CancellationToken cancellationToken)
    {
        await administration.AddUserToRoleAsync(request.UserId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Haalt de rol bij een gebruiker weg; weigert (409) als dit de laatste rolbeheerder zou wegnemen.</summary>
    [HttpDelete("roles/{id:int}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveRoleMember(int id, Guid userId, CancellationToken cancellationToken)
    {
        await administration.RemoveUserFromRoleAsync(userId, id, cancellationToken);
        return NoContent();
    }
}

public sealed record PermissionResponse(string Code, string Description, string Category);

public sealed record RoleResponse(int Id, string Code, string Name, string? Description, bool IsSystem, IReadOnlyList<string> Permissions);

public sealed record CreateRoleRequest(
    [Required, RegularExpression("^[a-z][a-z0-9-]{1,48}$")] string Code,
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description,
    [Required] IReadOnlyList<string> Permissions);

public sealed record UpdateRoleRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [StringLength(500)] string? Description);

public sealed record SetRolePermissionsRequest([Required] IReadOnlyList<string> Permissions);

public sealed record RoleMemberResponse(Guid UserId, string DisplayName, string Email, bool IsTemporary);

public sealed record AddRoleMemberRequest([Required] Guid UserId);
