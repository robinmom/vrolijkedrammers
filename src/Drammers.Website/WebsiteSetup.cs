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
        app.MapGet("/robots.txt", () => Results.Text("User-agent: *\nDisallow: /beheer/\nDisallow: /api/\nSitemap: /sitemap.xml\n", "text/plain"))
            .AllowAnonymous().ExcludeFromDescription();
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
    private Modules.Content.Website.WebsiteSettings? _settings;
    private IReadOnlyList<MenuPage>? _menu;

    public async Task<Modules.Content.Website.WebsiteSettings> SettingsAsync() => _settings ??= await reader.SettingsAsync(default);

    public async Task<IReadOnlyList<MenuPage>> MenuPagesAsync() => _menu ??= await reader.MenuPagesAsync(default);
}
