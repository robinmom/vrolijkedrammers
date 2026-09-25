using System.Security.Claims;
using Drammers.Api.Authentication;
using Drammers.Infrastructure.Identity;
using Drammers.Modules.Identity.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Authorization;

/// <summary>
/// Vereist een bekend, actief account (geen just-in-time provisioning, ADR-014) en, in Dev/Acc, de claim
/// <c>environmentAccess</c> (B-02). Faalt met 403, niet 401: het token zelf is geldig.
/// </summary>
public sealed partial class ActiveUserHandler(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthOptions> authOptions,
    ILogger<ActiveUserHandler> logger) : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveUserRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        // Geen endpoint (onbekende route): niets te beschermen, de request eindigt in een 404.
        if (httpContext.GetEndpoint() is null)
        {
            context.Succeed(requirement);
            return;
        }

        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var user = await CurrentUser.ResolveAsync(httpContext);
        var options = authOptions.Value;
        var failure = Evaluate(context.User, user, options);
        if (failure is null)
        {
            context.Succeed(requirement);
        }
        else
        {
            LogDenied(logger, failure);
            context.Fail(new AuthorizationFailureReason(this, failure));
        }

        await RecordLoginAsync(httpContext, context.User, user, failure);
    }

    private static string? Evaluate(ClaimsPrincipal principal, UserAccess? user, AuthOptions options)
    {
        if (!EnvironmentAccess.IsAllowed(principal, options.EnvironmentAccessClaim, options.RequiredEnvironmentAccess))
        {
            return "environment-access";
        }

        if (options.RequirePortalMfa && !string.IsNullOrEmpty(options.PortalClientId)
            && principal.FindFirst("azp")?.Value == options.PortalClientId
            && !principal.FindAll("amr").Any(c => c.Value == "mfa"))
        {
            return "portal-mfa-required";
        }

        return user switch
        {
            null => "unknown-account",
            { Status: AccountStatus.Blocked } => "blocked",
            { Status: not AccountStatus.Active } => "inactive",
            _ => null,
        };
    }

    private static async Task RecordLoginAsync(HttpContext httpContext, ClaimsPrincipal principal, UserAccess? user, string? failure)
    {
        var tokenId = principal.FindFirst("uti")?.Value ?? principal.FindFirst("iat")?.Value;
        var objectId = CurrentUser.ObjectId(principal);
        if (tokenId is null || objectId is null)
        {
            return;
        }

        var result = failure switch
        {
            null => LoginResult.Success,
            "blocked" => LoginResult.Locked,
            _ => LoginResult.Failed,
        };
        var recorder = httpContext.RequestServices.GetService<ILoginRecorder>();
        if (recorder is not null)
        {
            await recorder.RecordAsync(
                $"{objectId}:{tokenId}", user?.UserId, objectId, result, failure,
                httpContext.Connection.RemoteIpAddress?.ToString(), httpContext.Request.Headers.UserAgent.ToString(),
                httpContext.RequestAborted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Toegang geweigerd: {Reason}")]
    private static partial void LogDenied(ILogger logger, string reason);
}

/// <summary>De gebruiker heeft de permission via een van zijn geldige rollen (docs/07 §5).</summary>
public sealed class PermissionHandler(IHttpContextAccessor httpContextAccessor) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
        {
            return;
        }

        var user = await CurrentUser.ResolveAsync(httpContext);
        if (user is { Status: AccountStatus.Active } && user.Permissions.Contains(requirement.Permission))
        {
            context.Succeed(requirement);
        }
    }
}
