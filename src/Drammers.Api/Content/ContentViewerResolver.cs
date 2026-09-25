using Drammers.Api.Authentication;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Content;

/// <summary>
/// Publieke endpoints zijn "optioneel ingelogd" (fase 5): met een geldig token van een actief account ziet de gebruiker
/// ook content voor leden en zijn rollen; anders geldt hij als gast. Is het account gekoppeld aan een actief lid, dan
/// tellen ook de groepen van dat lid en content die aan het lid zelf is gericht (fase 8).
/// </summary>
public sealed class ContentViewerResolver(
    IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> authOptions, DrammersDbContext db, IClock clock)
{
    public async Task<ContentViewer> ResolveAsync()
    {
        var context = httpContextAccessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true
            || !EnvironmentAccess.IsAllowed(context.User, authOptions.Value.EnvironmentAccessClaim, authOptions.Value.RequiredEnvironmentAccess))
        {
            return ContentViewer.Guest;
        }

        var user = await CurrentUser.ResolveAsync(context);
        if (user is not { Status: AccountStatus.Active })
        {
            return ContentViewer.Guest;
        }

        var roles = user.Roles.Select(r => r.Code).ToList();
        if (user.MemberId is not { } memberId)
        {
            return new ContentViewer(roles);
        }

        // Alleen een actief lid telt mee voor doelgroepen Groep en Lid (een geschorst of inactief lid dus niet).
        var active = await db.Members.AsNoTracking()
            .AnyAsync(m => m.Id == memberId && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active, context.RequestAborted);
        if (!active)
        {
            return new ContentViewer(roles);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var groups = await db.GroupMemberships.AsNoTracking()
            .Where(gm => gm.MemberId == memberId && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Join(db.Groups.Where(g => g.Active), gm => gm.GroupId, g => g.Id, (gm, g) => g.Id)
            .ToListAsync(context.RequestAborted);
        return new ContentViewer(roles, memberId.ToString(), [.. groups.Select(g => g.ToString())]);
    }
}
