using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Authorization;
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
