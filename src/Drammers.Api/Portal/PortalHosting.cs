namespace Drammers.Api.Portal;

/// <summary>
/// Serveert het beheerportal (apps/admin, gebouwd naar wwwroot/beheer) vanuit de API-app, zodat alles in de EU
/// draait (OQ-76) en portal en API dezelfde origin delen (geen CORS nodig).
/// </summary>
public static class PortalHosting
{
    public const string BasePath = "/beheer";

    // connect-src: de API (zelfde origin) en de inlogpagina van Entra External ID.
    private const string ContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data: https:; connect-src 'self' https://*.ciamlogin.com; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    // De website (fase 21c): alles van de eigen origin; afbeeldingen ook van het Facebook-CDN (feed op de homepage).
    // Sinds 21d horen lid worden, optocht inschrijven, aanrijtijden en kaarten bij de website; connect-src ook naar de
    // inlogpagina van Entra External ID (tokens ophalen bij het inloggen voor de optocht).
    private const string WebsiteContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data: https://*.fbcdn.net; connect-src 'self' https://*.ciamlogin.com; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    // Met Cloudflare Turnstile (fase 21i, alleen als de sleutels zijn gezet): het script en het iframe van Cloudflare.
    private const string WebsiteWithTurnstileContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data: https://*.fbcdn.net; connect-src 'self' https://*.ciamlogin.com; " +
        "script-src 'self' https://challenges.cloudflare.com; frame-src https://challenges.cloudflare.com; " +
        "frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

    /// <summary>Headers voor alle responses; de portal-CSP alleen onder <see cref="BasePath"/>.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            if (context.Request.Path.StartsWithSegments(BasePath))
            {
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers.XFrameOptions = "DENY";
            }
            else if (Website.WebsiteSetup.IsWebsitePath(context.Request.Path))
            {
                var turnstile = context.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<Infrastructure.Contact.TurnstileOptions>>().Value.Enabled;
                headers.ContentSecurityPolicy = turnstile ? WebsiteWithTurnstileContentSecurityPolicy : WebsiteContentSecurityPolicy;
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
                headers.XFrameOptions = "DENY";
            }
            else
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            return next(context);
        });

    /// <summary>Redirect <c>/beheer</c> → <c>/beheer/</c> en statische bestanden; vóór authenticatie en autorisatie.</summary>
    public static WebApplication UsePortalStaticFiles(this WebApplication app)
    {
        // /beheer → /beheer/ (Vite gebruikt paden onder de base). Geen route: routing negeert de trailing slash.
        app.Use((context, next) =>
        {
            if (context.Request.Path.Value is BasePath)
            {
                context.Response.Redirect($"{context.Request.Path.Value}/{context.Request.QueryString}");
                return Task.CompletedTask;
            }

            return next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => SetCacheHeaders(context.Context) });
        return app;
    }

    /// <summary>Client-side routes van het portal vallen terug op index.html; /api en /health blijven ProblemDetails geven.</summary>
    public static WebApplication MapPortalFallback(this WebApplication app)
    {
        app.MapFallbackToFile($"{BasePath.TrimStart('/')}/{{*path:nonfile}}", $"{BasePath.TrimStart('/')}/index.html",
                new StaticFileOptions { OnPrepareResponse = context => SetCacheHeaders(context.Context) })
            .AllowAnonymous();
        return app;
    }

    // Bestanden in assets/ hebben een hash in de naam en veranderen nooit; index.html altijd opnieuw ophalen.
    private static void SetCacheHeaders(HttpContext context) =>
        context.Response.Headers.CacheControl = context.Request.Path.StartsWithSegments($"{BasePath}/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
}
