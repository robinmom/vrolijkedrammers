using Drammers.Api.Authentication;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Content;
using Drammers.Modules.Identity.Users;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Content;

/// <summary>
/// Publieke endpoints zijn "optioneel ingelogd" (fase 5): met een geldig token van een actief account ziet de gebruiker
/// ook content voor leden en zijn rollen; anders geldt hij als gast.
/// </summary>
public sealed class ContentViewerResolver(IHttpContextAccessor httpContextAccessor, IOptions<AuthOptions> authOptions)
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
        return user is { Status: AccountStatus.Active } ? new ContentViewer([.. user.Roles.Select(r => r.Code)]) : ContentViewer.Guest;
    }
}
