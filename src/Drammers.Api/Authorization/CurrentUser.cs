using System.Security.Claims;
using Drammers.Infrastructure.Identity;
using Drammers.SharedKernel.Auditing;

namespace Drammers.Api.Authorization;

/// <summary>De ingelogde gebruiker van deze request, één keer opgezocht en in <c>HttpContext.Items</c> bewaard.</summary>
public static class CurrentUser
{
    private const string ItemKey = "drammers:current-user";

    public static string? ObjectId(ClaimsPrincipal principal) => principal.FindFirst("oid")?.Value;

    public static async Task<UserAccess?> ResolveAsync(HttpContext httpContext)
    {
        if (httpContext.Items.TryGetValue(ItemKey, out var cached))
        {
            return cached as UserAccess;
        }

        UserAccess? user = null;
        var objectId = ObjectId(httpContext.User);
        var service = httpContext.RequestServices.GetService<IUserAccessService>();
        if (objectId is not null && service is not null)
        {
            user = await service.GetByExternalObjectIdAsync(objectId, httpContext.RequestAborted);
        }

        httpContext.Items[ItemKey] = user;
        return user;
    }

    /// <summary>Alleen na succesvolle autorisatie gevuld; anders <c>null</c>.</summary>
    public static UserAccess? Get(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ItemKey, out var user) ? user as UserAccess : null;
}

/// <summary>Actor voor audit en auditkolommen: de ingelogde gebruiker, anders het systeem.</summary>
public sealed class HttpCurrentActor(IHttpContextAccessor httpContextAccessor) : ICurrentActor
{
    public Guid? UserId => httpContextAccessor.HttpContext is { } context ? CurrentUser.Get(context)?.UserId : null;

    public ActorType Type => UserId is null ? ActorType.System : ActorType.User;
}
