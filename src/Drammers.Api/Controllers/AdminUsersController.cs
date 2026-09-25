using System.ComponentModel.DataAnnotations;
using ClosedXML.Excel;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Gebruikers: beheerders aanmaken (fase 3), rollen toewijzen, blokkeren (docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin/users")]
public sealed class AdminUsersController(DrammersDbContext db, AccountAdministration administration, IAuditLogger audit) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<PagedResult<UserSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<UserSummaryResponse>> Search(
        [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<UserSummaryResponse>.Normalize(page, pageSize);
        var query = Filter(search);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(u => u.DisplayName).Skip((p - 1) * size).Take(size)
            .Select(u => new UserSummaryResponse(u.Id, u.Email, u.DisplayName, u.AccountStatus.ToString(), u.LastLoginAt))
            .ToListAsync(cancellationToken);
        return new PagedResult<UserSummaryResponse>(items, p, size, total);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(Permissions.RoleManage)]
    [ProducesResponseType<UserSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<UserSummaryResponse> Get(Guid id, CancellationToken cancellationToken) =>
        await db.Users.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UserSummaryResponse(u.Id, u.Email, u.DisplayName, u.AccountStatus.ToString(), u.LastLoginAt))
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);

    /// <summary>Gebruikerslijst als Excel met Nederlandse kolomnamen; de export zelf wordt geaudit (fase 4).</summary>
    [HttpGet("export")]
    [RequirePermission(Permissions.RoleManage)]
    [Produces("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Export([FromQuery] string? search, CancellationToken cancellationToken)
    {
        var users = await Filter(search).OrderBy(u => u.DisplayName)
            .Select(u => new { u.DisplayName, u.Email, u.AccountStatus, u.LastLoginAt, u.CreatedAt })
            .ToListAsync(cancellationToken);
        var roles = await db.UserRoles.AsNoTracking()
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, r.Name })
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new { u.Email, x.Name })
            .ToListAsync(cancellationToken);
        var rolesByEmail = roles.GroupBy(r => r.Email).ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Name).Order()));

        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Gebruikers");
        string[] headers = ["Naam", "E-mailadres", "Status", "Rollen", "Laatste login (UTC)", "Aangemaakt (UTC)"];
        for (var c = 0; c < headers.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = headers[c];
        }

        for (var i = 0; i < users.Count; i++)
        {
            var u = users[i];
            var row = i + 2;
            sheet.Cell(row, 1).Value = u.DisplayName;
            sheet.Cell(row, 2).Value = u.Email;
            sheet.Cell(row, 3).Value = StatusLabel(u.AccountStatus);
            sheet.Cell(row, 4).Value = rolesByEmail.GetValueOrDefault(u.Email, string.Empty);
            sheet.Cell(row, 5).Value = u.LastLoginAt;
            sheet.Cell(row, 6).Value = u.CreatedAt;
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        await audit.WriteAsync(new AuditEntry("user.exported", "User", "*", null, $"{{\"count\":{users.Count},\"search\":{System.Text.Json.JsonSerializer.Serialize(search)}}}"), cancellationToken);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"gebruikers-{DateTime.UtcNow:yyyyMMdd}.xlsx");
    }

    private IQueryable<Modules.Identity.Users.User> Filter(string? search)
    {
        var query = db.Users.AsNoTracking();
        return string.IsNullOrWhiteSpace(search)
            ? query
            : query.Where(u => u.Email.Contains(search) || u.DisplayName.Contains(search));
    }

    private static string StatusLabel(Modules.Identity.Users.AccountStatus status) => status switch
    {
        Modules.Identity.Users.AccountStatus.Active => "Actief",
        Modules.Identity.Users.AccountStatus.Blocked => "Geblokkeerd",
        Modules.Identity.Users.AccountStatus.Disabled => "Uitgeschakeld",
        _ => "Verwijderd",
    };

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
