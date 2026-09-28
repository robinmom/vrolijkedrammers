namespace Drammers.Api.Portal;

/// <summary>
/// Serveert het beheerportal (apps/admin, gebouwd naar wwwroot/beheer) vanuit de API-app, zodat alles in de EU
/// draait (OQ-76) en portal en API dezelfde origin delen (geen CORS nodig).
/// </summary>
public static class PortalHosting
{
    public const string BasePath = "/beheer";

    /// <summary>Openbare webpagina "Lid worden" (fase 9b); statisch, praat met dezelfde API.</summary>
    public const string JoinPath = "/lid-worden";

    /// <summary>Openbare webpagina "Inschrijven optocht" (fase 11c) voor iedereen zonder account; zelfde CSP als lid worden.</summary>
    public const string ParadePath = "/optocht-inschrijven";

    private const string JoinContentSecurityPolicy =
        "default-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

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
            else if (context.Request.Path.StartsWithSegments(JoinPath) || context.Request.Path.StartsWithSegments(ParadePath))
            {
                headers.ContentSecurityPolicy = JoinContentSecurityPolicy;
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
            if (context.Request.Path.Value is BasePath or JoinPath or ParadePath)
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
