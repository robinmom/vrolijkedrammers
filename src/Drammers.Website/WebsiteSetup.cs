using System.Text;
using Drammers.Infrastructure.Persistence;
using Drammers.Website.Content;
using Drammers.Website.Media;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Drammers.Website;

/// <summary>
/// De openbare website (fase 21c) in dezelfde app als de API: Razor Pages op de hoofdmap, een media-endpoint voor
/// afbeeldingen, <c>sitemap.xml</c> en <c>robots.txt</c>. Pagina's worden kort gecachet (alleen voor anonieme bezoekers).
/// </summary>
public static class WebsiteSetup
{
    public const string CachePolicy = "website";

    /// <summary>Paden die niet bij de website horen (API, portal en de losse webpagina's).</summary>
    public static readonly string[] OtherPaths = ["/api", "/health", "/beheer", "/openapi"];

    public static IServiceCollection AddWebsite(this IServiceCollection services, IConfiguration configuration)
    {
        // De website is openbaar; de API heeft een fallback-policy die anders een ingelogde gebruiker vraagt.
        services.AddRazorPages(options =>
        {
            options.Conventions.AllowAnonymousToFolder("/");
            // Jeugdprinsen: dezelfde galerij, alleen zichtbaar als het bestuur hem aanzet.
            options.Conventions.AddPageRoute("/Prinsen", "jeugdprinsen");
        });
        services.AddScoped<WebsiteReader>();
        services.AddScoped<SiteShell>();
        services.AddScoped<Redirects>();
        services.AddScoped<ContactLinks>();
        services.Configure<FacebookOptions>(configuration.GetSection(FacebookOptions.SectionName));
        services.AddMemoryCache();
        services.AddHttpClient<FacebookFeed>(http => http.Timeout = TimeSpan.FromSeconds(10));
        services.AddOutputCache(options =>
            options.AddPolicy(CachePolicy, policy => policy.Expire(TimeSpan.FromSeconds(60)).SetVaryByQuery("*").Tag(CachePolicy)));
        return services;
    }

    public static bool IsWebsitePath(PathString path) => !OtherPaths.Any(p => path.StartsWithSegments(p));

    /// <summary>Buiten Productie: elke response krijgt <c>X-Robots-Tag: noindex</c> (ook als iemand naar Dev linkt).</summary>
    public static IApplicationBuilder UseNoIndexOutsideProduction(this IApplicationBuilder app, IHostEnvironment environment) =>
        environment.IsProduction()
            ? app
            : app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                    return Task.CompletedTask;
                });
                await next(context);
            });

    /// <summary>
    /// Eén hoofdadres (fase 7): staat <c>Website:CanonicalHost</c> (bijv. <c>www.vrolijkedrammers.nl</c>), dan gaat elk
    /// ander eigen domein (bijv. zonder www) daarheen, met pad en query. GET/HEAD met 301, de rest met 308 (methode en body
    /// blijven). Het azurewebsites-adres en localhost blijven werken (deploy, health checks en ontwikkelen).
    /// </summary>
    public static IApplicationBuilder UseCanonicalHost(this IApplicationBuilder app, IConfiguration configuration)
    {
        var canonical = configuration["Website:CanonicalHost"]?.Trim();
        if (string.IsNullOrEmpty(canonical))
        {
            return app;
        }

        return app.Use(async (context, next) =>
        {
            var host = context.Request.Host.Host;
            if (string.Equals(host, canonical, StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".azurewebsites.net", StringComparison.OrdinalIgnoreCase)
                || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                await next(context);
                return;
            }

            var method = context.Request.Method;
            context.Response.StatusCode = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                ? StatusCodes.Status301MovedPermanently
                : StatusCodes.Status308PermanentRedirect;
            context.Response.Headers.Location = $"https://{canonical}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
        });
    }

    /// <summary>
    /// Statuspagina's: een onbekend websiteadres toont de 404-pagina in de huisstijl; API, portal en media houden
    /// ProblemDetails. Vervangt <c>UseStatusCodePages</c> in de API.
    /// </summary>
    public static IApplicationBuilder UseStatusCodePagesWithWebsite(this IApplicationBuilder app)
    {
        static bool IsWebsitePage(HttpContext context) =>
            IsWebsitePath(context.Request.Path) && !context.Request.Path.StartsWithSegments("/media") && HttpMethods.IsGet(context.Request.Method);

        app.UseWhen(IsWebsitePage, branch => branch.UseStatusCodePagesWithReExecute("/niet-gevonden"));
        app.UseWhen(context => !IsWebsitePage(context), branch => branch.UseStatusCodePages());
        return app;
    }

    /// <summary>
    /// Na een geslaagde wijziging in het portal (<c>POST/PUT/DELETE /api/v1/admin/…</c>) de gecachte websitepagina's
    /// weggooien, zodat de wijziging meteen zichtbaar is. Na <c>UseOutputCache</c> aanroepen.
    /// </summary>
    public static IApplicationBuilder UseWebsiteCacheInvalidation(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            await next(context);
            if (!HttpMethods.IsGet(context.Request.Method) && context.Request.Path.StartsWithSegments("/api/v1/admin")
                && context.Response.StatusCode < 400)
            {
                var store = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.OutputCaching.IOutputCacheStore>();
                await store.EvictByTagAsync(CachePolicy, context.RequestAborted);
            }
        });

    public static WebApplication MapWebsite(this WebApplication app)
    {
        app.MapRazorPages().CacheOutput(CachePolicy);
        app.MapWebsiteMedia();
        // Alleen Productie mag in zoekmachines; Dev/Acc zijn kopieën en zouden anders dubbel gevonden worden.
        var robots = app.Environment.IsProduction()
            ? "User-agent: *\nDisallow: /beheer/\nDisallow: /api/\nSitemap: /sitemap.xml\n"
            : "User-agent: *\nDisallow: /\n";
        app.MapGet("/robots.txt", () => Results.Text(robots, "text/plain")).AllowAnonymous().ExcludeFromDescription();
        app.MapGet("/sitemap.xml", SitemapAsync).AllowAnonymous().ExcludeFromDescription().CacheOutput(CachePolicy);
        return app;
    }

    private static async Task<IResult> SitemapAsync(HttpContext context, [FromServices] DrammersDbContext db, CancellationToken cancellationToken)
    {
        var root = $"{context.Request.Scheme}://{context.Request.Host}";
        var paths = new List<string> { "/", "/agenda", "/nieuws", "/kader", "/prinsengalerie", "/onderscheidingen", "/fotos", "/optocht", "/contact", "/doe-mee" };
        paths.AddRange(await db.WebsitePages.AsNoTracking().Where(p => p.IsPublished).Select(p => "/" + p.Slug).ToListAsync(cancellationToken));
        paths.AddRange(await db.News.AsNoTracking().Where(n => n.ShowOnWebsite && n.Slug != null && n.Status == Modules.Content.Shared.PublicationStatus.Published)
            .Select(n => "/nieuws/" + n.Slug).ToListAsync(cancellationToken));
        paths.AddRange(await db.Awards.AsNoTracking().Where(a => a.IsPublished).Select(a => "/onderscheidingen/" + a.Slug).ToListAsync(cancellationToken));
        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
        foreach (var path in paths.Distinct())
        {
            xml.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(root + path)).Append("</loc></url>\n");
        }

        return Results.Text(xml.Append("</urlset>\n").ToString(), "application/xml");
    }
}

/// <summary>Gegevens voor de kop en voet van elke pagina (menu, social media), één keer per verzoek geladen.</summary>
public sealed class SiteShell(WebsiteReader reader)
{
    /// <summary>
    /// Feature flag (portal → Configuratie): pas als die aanstaat noemt de website de app (menu, Doe mee, nieuws,
    /// formulierteksten). Tot de lancering blijft de app zo uit beeld; aanzetten vraagt geen deploy.
    /// </summary>
    public const string AppFlag = "website.app";

    /// <summary>Feature flag: pas als die aanstaat staan "Kaarten en munten" en de kaartverkooppagina op de website.</summary>
    public const string TicketsFlag = "website.kaarten";

    private Modules.Content.Website.WebsiteSettings? _settings;
    private readonly Dictionary<string, bool> _flags = new(StringComparer.Ordinal);
    private IReadOnlyList<MenuPage>? _menu;

    public async Task<Modules.Content.Website.WebsiteSettings> SettingsAsync() => _settings ??= await reader.SettingsAsync(default);

    public async Task<IReadOnlyList<MenuPage>> MenuPagesAsync() => _menu ??= await reader.MenuPagesAsync(default);

    public Task<bool> ShowAppAsync() => FlagAsync(AppFlag);

    public Task<bool> ShowTicketsAsync() => FlagAsync(TicketsFlag);

    private async Task<bool> FlagAsync(string key)
    {
        if (!_flags.TryGetValue(key, out var enabled))
        {
            _flags[key] = enabled = await reader.FeatureEnabledAsync(key, default);
        }

        return enabled;
    }
}
