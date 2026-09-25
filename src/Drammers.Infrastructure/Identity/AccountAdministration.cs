using System.Text.Json;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Provisioning;
using Drammers.Modules.Identity.Roles;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Identity;

public sealed record RoleAssignment(string RoleCode, DateOnly? ValidFrom = null, DateOnly? ValidTo = null);

/// <summary>
/// Beheer van accounts, rollen en rechten (docs/07 §6): elke wijziging wordt geaudit, verhoogt
/// <c>permissions_version</c> van de betrokken gebruikers en maakt hun cache ongeldig. Lock-out-preventie: er blijft
/// altijd minstens één actieve gebruiker met <c>role.manage</c>.
/// </summary>
public sealed class AccountAdministration(
    DrammersDbContext db,
    IAuditLogger audit,
    IUserAccessService userAccess,
    IEntraUserDirectory entra,
    ICurrentActor actor,
    IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ----- Rollen van een gebruiker -------------------------------------------------------------------------------

    public async Task SetUserRolesAsync(Guid userId, IReadOnlyCollection<RoleAssignment> assignments, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);
        var roles = await ResolveRolesAsync(assignments.Select(a => a.RoleCode), cancellationToken);

        var before = await DescribeRolesAsync(user.Roles, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        user.Roles.RemoveAll(existing => roles.All(r => r.Id != existing.RoleId));
        foreach (var assignment in assignments)
        {
            var roleId = roles.Single(r => r.Code == assignment.RoleCode).Id;
            var existing = user.Roles.SingleOrDefault(r => r.RoleId == roleId);
            if (existing is null)
            {
                user.Roles.Add(new UserRole
                {
                    UserId = user.Id,
                    RoleId = roleId,
                    ValidFrom = assignment.ValidFrom,
                    ValidTo = assignment.ValidTo,
                    AssignedAt = now,
                    AssignedBy = actor.UserId,
                });
            }
            else
            {
                existing.ValidFrom = assignment.ValidFrom;
                existing.ValidTo = assignment.ValidTo;
            }
        }

        user.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await EnsureRoleManagerRemainsAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("user.roles.changed", "User", user.Id.ToString(), before, JsonSerializer.Serialize(assignments, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(user.ExternalObjectId);
    }

    // ----- Blokkeren ----------------------------------------------------------------------------------------------

    /// <summary>Blokkeert het account: 403 op de API, Entra-account uitgeschakeld en sessies ingetrokken.</summary>
    public Task BlockAsync(Guid userId, CancellationToken cancellationToken) => SetBlockedAsync(userId, blocked: true, cancellationToken);

    public Task UnblockAsync(Guid userId, CancellationToken cancellationToken) => SetBlockedAsync(userId, blocked: false, cancellationToken);

    private async Task SetBlockedAsync(Guid userId, bool blocked, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.UserNotFound, "Gebruiker niet gevonden.", DomainErrorKind.NotFound);
        var before = user.AccountStatus;
        user.AccountStatus = blocked ? AccountStatus.Blocked : AccountStatus.Active;
        user.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        await EnsureRoleManagerRemainsAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(blocked ? "user.blocked" : "user.unblocked", "User", user.Id.ToString(), $"{{\"status\":\"{before}\"}}", $"{{\"status\":\"{user.AccountStatus}\"}}"),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        userAccess.Invalidate(user.ExternalObjectId);

        // Na de commit: de API weigert het account nu al; Entra volgt (bij een fout kan dit veilig opnieuw).
        await entra.SetAccountEnabledAsync(user.ExternalObjectId, enabled: !blocked, cancellationToken);
        if (blocked)
        {
            await entra.RevokeSessionsAsync(user.ExternalObjectId, cancellationToken);
        }
    }

    // ----- Rollen en permissions ----------------------------------------------------------------------------------

    public async Task<Role> CreateRoleAsync(string code, string name, string? description, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        if (await db.Roles.AnyAsync(r => r.Code == code, cancellationToken))
        {
            throw new DomainException(ErrorCodes.RoleCodeTaken, $"Er bestaat al een rol met code '{code}'.", DomainErrorKind.Conflict);
        }

        var permissionIds = await ResolvePermissionIdsAsync(permissions, cancellationToken);
        var sortOrder = (await db.Roles.MaxAsync(r => (int?)r.SortOrder, cancellationToken) ?? 0) + 10;
        var role = new Role { Code = code, Name = name, Description = description, SortOrder = sortOrder };
        role.Permissions.AddRange(permissionIds.Select(id => new RolePermission { PermissionId = id }));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.Roles.Add(role);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("role.created", "Role", role.Id.ToString(), null, JsonSerializer.Serialize(new { code, name, permissions }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return role;
    }

    public async Task UpdateRoleAsync(int roleId, string name, string? description, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);
        var before = JsonSerializer.Serialize(new { role.Name, role.Description }, Json);
        role.Name = name;
        role.Description = description;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("role.updated", "Role", role.Id.ToString(), before, JsonSerializer.Serialize(new { name, description }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await FindRoleAsync(roleId, cancellationToken);
        if (role.IsSystem)
        {
            throw new DomainException(ErrorCodes.SystemRoleProtected, $"De systeemrol '{role.Name}' kan niet worden verwijderd.", DomainErrorKind.Conflict);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var affected = await AffectedUsersAsync(role.Id, cancellationToken);
        db.Roles.Remove(role);
        await db.SaveChangesAsync(cancellationToken);
        await EnsureRoleManagerRemainsAsync(cancellationToken);
        await BumpVersionsAsync(affected, cancellationToken);
        await audit.WriteAsync(new AuditEntry("role.deleted", "Role", role.Id.ToString(), JsonSerializer.Serialize(new { role.Code, role.Name }, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InvalidateAll(affected);
    }

    public async Task SetRolePermissionsAsync(int roleId, IReadOnlyCollection<string> permissions, CancellationToken cancellationToken)
    {
        var role = await db.Roles.Include(r => r.Permissions).SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.RoleNotFound, "Rol niet gevonden.", DomainErrorKind.NotFound);
        var permissionIds = await ResolvePermissionIdsAsync(permissions, cancellationToken);
        var before = await db.Permissions.Where(p => role.Permissions.Select(rp => rp.PermissionId).Contains(p.Id)).Select(p => p.Code).ToListAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        role.Permissions.RemoveAll(rp => !permissionIds.Contains(rp.PermissionId));
        role.Permissions.AddRange(permissionIds.Where(id => role.Permissions.All(rp => rp.PermissionId != id))
            .Select(id => new RolePermission { RoleId = role.Id, PermissionId = id }));
        await db.SaveChangesAsync(cancellationToken);
        await EnsureRoleManagerRemainsAsync(cancellationToken);
        var affected = await AffectedUsersAsync(role.Id, cancellationToken);
        await BumpVersionsAsync(affected, cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("role.permissions.changed", "Role", role.Id.ToString(), JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(permissions, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        InvalidateAll(affected);
    }

    // ----- Provisioning (fase 3: alleen handmatige bootstrap van beheerders) ---------------------------------------

    /// <summary>
    /// Maakt (of hervat) een beheerdersaccount: Entra-account (bestaand of nieuw) → lokale gebruiker met rollen.
    /// Idempotent op e-mailadres: een tweede aanroep maakt geen tweede account (ADR-014).
    /// </summary>
    public async Task<Guid> ProvisionAdministratorAsync(string email, string displayName, IReadOnlyCollection<string> roleCodes, CancellationToken cancellationToken)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var roles = await ResolveRolesAsync(roleCodes, cancellationToken);
        var saga = await db.AccountProvisioning.SingleOrDefaultAsync(
            p => p.SourceType == ProvisioningSourceType.Manual && p.SourceId == normalizedEmail, cancellationToken);
        if (saga is null)
        {
            saga = new AccountProvisioning
            {
                Id = IdGenerator.NewId(),
                SourceType = ProvisioningSourceType.Manual,
                SourceId = normalizedEmail,
                Kind = ProvisioningKind.Administrator,
                Step = ProvisioningStep.Pending,
                CreatedAt = clock.UtcNow.UtcDateTime,
            };
            db.AccountProvisioning.Add(saga);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (saga is { Step: ProvisioningStep.Completed, UserId: { } completedUserId })
        {
            return completedUserId;
        }

        saga.Attempts++;
        try
        {
            if (saga.EntraObjectId is null)
            {
                saga.EntraObjectId = await entra.FindByEmailAsync(normalizedEmail, cancellationToken)
                    ?? await entra.CreateAsync(normalizedEmail, displayName, cancellationToken);
                saga.Step = ProvisioningStep.AccountCreated;
                await db.SaveChangesAsync(cancellationToken);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            var user = await db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.ExternalObjectId == saga.EntraObjectId, cancellationToken);
            if (user is null)
            {
                user = new User
                {
                    Id = IdGenerator.NewId(),
                    ExternalObjectId = saga.EntraObjectId,
                    Email = normalizedEmail,
                    DisplayName = displayName,
                    AccountStatus = AccountStatus.Active,
                };
                db.Users.Add(user);
            }

            var now = clock.UtcNow.UtcDateTime;
            foreach (var role in roles.Where(r => user.Roles.All(ur => ur.RoleId != r.Id)))
            {
                user.Roles.Add(new UserRole { UserId = user.Id, RoleId = role.Id, AssignedAt = now, AssignedBy = actor.UserId });
            }

            user.PermissionsVersion++;
            saga.UserId = user.Id;
            saga.Step = ProvisioningStep.Completed;
            saga.CompletedAt = now;
            saga.LastError = null;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(
                new AuditEntry("user.provisioned", "User", user.Id.ToString(), null, JsonSerializer.Serialize(new { source = "Manual", roles = roleCodes }, Json)),
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            userAccess.Invalidate(user.ExternalObjectId);
            return user.Id;
        }
        catch (Exception ex) when (ex is not DomainException and not OperationCanceledException)
        {
            db.ChangeTracker.Clear();
            await db.AccountProvisioning.Where(p => p.Id == saga.Id).ExecuteUpdateAsync(
                s => s.SetProperty(p => p.LastError, ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message)
                    .SetProperty(p => p.Attempts, saga.Attempts),
                cancellationToken);
            throw new DomainException(ErrorCodes.ProvisioningFailed, "Het account kon niet worden aangemaakt; probeer het opnieuw.", DomainErrorKind.Conflict);
        }
    }

    // ----- Hulpfuncties -------------------------------------------------------------------------------------------

    /// <summary>Faalt (en rolt terug) als er geen actieve gebruiker met <c>role.manage</c> overblijft.</summary>
    private async Task EnsureRoleManagerRemainsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var roleManagePermissionId = await db.Permissions.Where(p => p.Code == Permissions.RoleManage).Select(p => p.Id).SingleAsync(cancellationToken);
        var anyManager = await db.Users
            .Where(u => u.AccountStatus == AccountStatus.Active)
            .AnyAsync(u => db.UserRoles.Any(ur => ur.UserId == u.Id
                && (ur.ValidFrom == null || ur.ValidFrom <= today) && (ur.ValidTo == null || ur.ValidTo >= today)
                && db.RolePermissions.Any(rp => rp.RoleId == ur.RoleId && rp.PermissionId == roleManagePermissionId)), cancellationToken);
        var anyUsers = await db.Users.AnyAsync(cancellationToken);
        if (anyUsers && !anyManager)
        {
            throw new DomainException(
                ErrorCodes.LockoutPrevented,
                "Deze wijziging zou niemand met het recht 'rollen beheren' overlaten. Wijs dat recht eerst aan iemand anders toe.",
                DomainErrorKind.Conflict);
        }
    }

    private async Task<List<Role>> ResolveRolesAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var wanted = codes.Distinct(StringComparer.Ordinal).ToList();
        var roles = await db.Roles.Where(r => wanted.Contains(r.Code)).ToListAsync(cancellationToken);
        var missing = wanted.Except(roles.Select(r => r.Code)).ToList();
        return missing.Count == 0
            ? roles
            : throw new DomainException(ErrorCodes.RoleNotFound, $"Onbekende rol(len): {string.Join(", ", missing)}.", DomainErrorKind.Validation);
    }

    private async Task<List<int>> ResolvePermissionIdsAsync(IEnumerable<string> codes, CancellationToken cancellationToken)
    {
        var wanted = codes.Distinct(StringComparer.Ordinal).ToList();
        var permissions = await db.Permissions.Where(p => wanted.Contains(p.Code)).ToListAsync(cancellationToken);
        var missing = wanted.Except(permissions.Select(p => p.Code)).ToList();
        return missing.Count == 0
            ? [.. permissions.Select(p => p.Id)]
            : throw new DomainException(ErrorCodes.UnknownPermission, $"Onbekende permission(s): {string.Join(", ", missing)}.");
    }

    private async Task<Role> FindRoleAsync(int roleId, CancellationToken cancellationToken) =>
        await db.Roles.SingleOrDefaultAsync(r => r.Id == roleId, cancellationToken)
        ?? throw new DomainException(ErrorCodes.RoleNotFound, "Rol niet gevonden.", DomainErrorKind.NotFound);

    private async Task<string?> DescribeRolesAsync(IEnumerable<UserRole> roles, CancellationToken cancellationToken)
    {
        var ids = roles.Select(r => r.RoleId).ToList();
        var codes = await db.Roles.Where(r => ids.Contains(r.Id)).Select(r => r.Code).ToListAsync(cancellationToken);
        return JsonSerializer.Serialize(codes, Json);
    }

    private async Task<List<(Guid Id, string ObjectId)>> AffectedUsersAsync(int roleId, CancellationToken cancellationToken)
    {
        var users = await db.UserRoles.Where(ur => ur.RoleId == roleId)
            .Join(db.Users, ur => ur.UserId, u => u.Id, (_, u) => new { u.Id, u.ExternalObjectId })
            .ToListAsync(cancellationToken);
        return [.. users.Select(u => (u.Id, u.ExternalObjectId))];
    }

    private async Task BumpVersionsAsync(IEnumerable<(Guid Id, string ObjectId)> users, CancellationToken cancellationToken)
    {
        var ids = users.Select(u => u.Id).ToList();
        await db.Users.Where(u => ids.Contains(u.Id)).ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionsVersion, u => u.PermissionsVersion + 1), cancellationToken);
    }

    private void InvalidateAll(IEnumerable<(Guid Id, string ObjectId)> users)
    {
        foreach (var (_, objectId) in users)
        {
            userAccess.Invalidate(objectId);
        }
    }
}
