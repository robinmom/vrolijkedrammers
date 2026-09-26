using System.Security.Claims;
using System.Threading.RateLimiting;
using Drammers.SharedKernel.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;

namespace Drammers.Api.Authorization;

public static class AuthorizationSetup
{
    /// <summary>Strenge limiet voor anonieme formulieren (accountverzoek, later lid worden): 5 per 10 minuten per IP.</summary>
    public const string AnonymousFormsPolicy = "anonymous-forms";

    public static IServiceCollection AddDrammersAuthorization(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ActiveUserHandler>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<ICurrentActor, HttpCurrentActor>();
        services.AddAuthorization(options =>
        {
            // Deny by default: [Authorize] zonder policy én endpoints zonder annotatie vereisen een actief account.
            options.DefaultPolicy = PermissionPolicyProvider.ActiveUser;
            options.FallbackPolicy = PermissionPolicyProvider.Fallback;
        });

        // App Service zet het IP van de client in X-Forwarded-For; de front-end is de enige ingang.
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // Rate limiting (docs/05 §7): ingelogd per gebruiker, anoniem per IP.
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                context.HttpContext.Response.Headers.RetryAfter = "60";
                return ValueTask.CompletedTask;
            };
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var objectId = context.User.FindFirstValue("oid");
                return objectId is not null
                    ? RateLimitPartition.GetFixedWindowLimiter($"user:{objectId}", _ => Window(300))
                    : RateLimitPartition.GetFixedWindowLimiter($"ip:{context.Connection.RemoteIpAddress}", _ => Window(120));
            });
            options.AddPolicy(AnonymousFormsPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                $"form:{context.Connection.RemoteIpAddress}",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = context.RequestServices.GetRequiredService<IConfiguration>().GetValue("RateLimits:AnonymousForms", 5),
                    Window = TimeSpan.FromMinutes(10),
                    QueueLimit = 0,
                }));
        });

        return services;
    }

    private static FixedWindowRateLimiterOptions Window(int permitsPerMinute) =>
        new() { PermitLimit = permitsPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
}
