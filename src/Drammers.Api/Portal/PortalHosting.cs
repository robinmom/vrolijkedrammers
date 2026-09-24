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
            else
            {
                headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
            }

            return next(context);
        });

    public static WebApplication MapPortal(this WebApplication app)
    {
        // /beheer → /beheer/ (Vite gebruikt paden onder de base). Geen route: routing negeert de trailing slash.
        app.Use((context, next) =>
        {
            if (context.Request.Path.Value == BasePath)
            {
                context.Response.Redirect($"{BasePath}/{context.Request.QueryString}");
                return Task.CompletedTask;
            }

            return next(context);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions { OnPrepareResponse = context => SetCacheHeaders(context.Context) });

        // Client-side routes van het portal vallen terug op index.html; /api en /health blijven ProblemDetails geven.
        app.MapFallbackToFile($"{BasePath.TrimStart('/')}/{{*path:nonfile}}", $"{BasePath.TrimStart('/')}/index.html",
            new StaticFileOptions { OnPrepareResponse = context => SetCacheHeaders(context.Context) });
        return app;
    }

    // Bestanden in assets/ hebben een hash in de naam en veranderen nooit; index.html altijd opnieuw ophalen.
    private static void SetCacheHeaders(HttpContext context) =>
        context.Response.Headers.CacheControl = context.Request.Path.StartsWithSegments($"{BasePath}/assets")
            ? "public, max-age=31536000, immutable"
            : "no-cache";
}
