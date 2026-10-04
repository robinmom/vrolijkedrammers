using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;

namespace Drammers.Api.Content;

/// <summary>
/// Voorbeelden van mailings voor het portal (fase 27a). Het portal toont de mail in een iframe; met srcdoc zou de strenge
/// CSP van het portal ook voor de mail gelden en verdwijnt alle opmaak (inline stijlen). Daarom krijgt het voorbeeld een
/// eigen adres met een eigen CSP: tien minuten geldig, met een onraadbare sleutel en zonder echte ontvangers.
/// </summary>
public sealed class MailingPreviewDocuments(IMemoryCache cache)
{
    public const string BasePath = "/mailing-voorbeeld";

    /// <summary>Mag inline stijlen, de lettertypen en afbeeldingen van de mail; geen scripts; alleen in het portal te tonen.</summary>
    public const string ContentSecurityPolicy =
        "default-src 'none'; style-src 'self' 'unsafe-inline'; font-src 'self' https:; img-src 'self' https: data:; " +
        "frame-ancestors 'self'; base-uri 'none'; form-action 'none'";

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public string Store(string html)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        cache.Set(Key(token), html, Lifetime);
        return $"{BasePath}/{token}";
    }

    public string? Find(string token) => cache.TryGetValue(Key(token), out string? html) ? html : null;

    private static string Key(string token) => $"mailing-preview:{token}";
}

public static class MailingPreviewEndpoints
{
    public static IEndpointRouteBuilder MapMailingPreviews(this IEndpointRouteBuilder app)
    {
        app.MapGet($"{MailingPreviewDocuments.BasePath}/{{token:length(32)}}", (string token, MailingPreviewDocuments previews, HttpContext context) =>
            {
                context.Response.Headers.CacheControl = "no-store";
                return previews.Find(token) is { } html ? Results.Content(html, "text/html; charset=utf-8") : Results.NotFound();
            })
            .AllowAnonymous()
            .ExcludeFromDescription();
        return app;
    }
}
