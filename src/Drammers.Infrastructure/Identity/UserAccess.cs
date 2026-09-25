using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Drammers.Infrastructure.Identity;

/// <summary>Gebruiker zoals de autorisatie die nodig heeft: status, rollen en de afgeleide permissions.</summary>
public sealed record UserAccess(
    Guid UserId,
    string ExternalObjectId,
    string Email,
    string DisplayName,
    Guid? MemberId,
    AccountStatus Status,
    int PermissionsVersion,
    IReadOnlyList<RoleSummary> Roles,
    IReadOnlySet<string> Permissions);

public sealed record RoleSummary(string Code, string Name);

/// <summary>Zoekt de gebruiker en zijn permissions op (docs/07 §5); permissions staan niet in het token.</summary>
public interface IUserAccessService
{
    Task<UserAccess?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken cancellationToken);

    /// <summary>Direct ongeldig maken na een rol- of statuswijziging (zelfde instantie); andere instanties na de TTL.</summary>
    void Invalidate(string externalObjectId);
}

internal sealed class UserAccessService(DrammersDbContext db, IMemoryCache cache, IClock clock) : IUserAccessService
{
    /// <summary>Maximale vertraging van een rolwijziging op een andere instantie (docs/07 §5).</summary>
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<UserAccess?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey(externalObjectId), out UserAccess? cached))
        {
            return cached;
        }

        var user = await db.Users.AsNoTracking()
            .Where(u => u.ExternalObjectId == externalObjectId)
            .Select(u => new { u.Id, u.ExternalObjectId, u.Email, u.DisplayName, u.MemberId, u.AccountStatus, u.PermissionsVersion })
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            // Onbekende accounts kort cachen: beperkt databaseverkeer bij herhaalde pogingen.
            cache.Set(CacheKey(externalObjectId), (UserAccess?)null, TimeSpan.FromSeconds(30));
            return null;
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var roles = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == user.Id
                && (ur.ValidFrom == null || ur.ValidFrom <= today)
                && (ur.ValidTo == null || ur.ValidTo >= today))
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r)
            .OrderBy(r => r.SortOrder)
            .Select(r => new { r.Id, r.Code, r.Name })
            .ToListAsync(cancellationToken);

        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = await db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Join(db.Permissions, rp => rp.PermissionId, p => p.Id, (_, p) => p.Code)
            .Distinct()
            .ToListAsync(cancellationToken);

        var access = new UserAccess(
            user.Id, user.ExternalObjectId, user.Email, user.DisplayName, user.MemberId, user.AccountStatus, user.PermissionsVersion,
            [.. roles.Select(r => new RoleSummary(r.Code, r.Name))],
            permissions.ToHashSet(StringComparer.Ordinal));
        cache.Set(CacheKey(externalObjectId), access, CacheDuration);
        return access;
    }

    public void Invalidate(string externalObjectId) => cache.Remove(CacheKey(externalObjectId));

    private static string CacheKey(string externalObjectId) => $"user-access:{externalObjectId}";
}
