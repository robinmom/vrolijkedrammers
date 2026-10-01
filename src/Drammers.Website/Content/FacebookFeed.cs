using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Website.Content;

/// <summary>
/// Instellingen <c>Website:Facebook:*</c> (fase 21c). Het paginatoken staat in Key Vault (secret
/// <see cref="TokenSecretName"/>); <see cref="Token"/> alleen lokaal of in tests.
/// </summary>
public sealed class FacebookOptions
{
    public const string SectionName = "Website:Facebook";

    public Uri GraphUrl { get; set; } = new("https://graph.facebook.com/v23.0/");

    /// <summary>Id van de Facebookpagina van De Vrolijke Drammers.</summary>
    public string PageId { get; set; } = "263171860384920";

    public string TokenSecretName { get; set; } = "facebook-page-token";

    public string? Token { get; set; }

    public int PostCount { get; set; } = 3;
}

public sealed record FacebookPost(string? Message, DateTime CreatedAt, string? ImageUrl, string Link);

/// <summary>
/// De laatste berichten van de Facebookpagina, opgehaald door de server (Graph API) en getoond in de eigen huisstijl:
/// bezoekers laden niets van Facebook zelf (geen cookies of tracking). Een half uur in het geheugen; zonder token of bij
/// een fout blijft de lijst leeg en toont de site alleen de link naar de pagina.
/// </summary>
public sealed class FacebookFeed(HttpClient http, IOptions<FacebookOptions> options, IServiceProvider services, IMemoryCache cache, ILogger<FacebookFeed> logger)
{
    private const string CacheKey = "website:facebook-posts";

    public async Task<IReadOnlyList<FacebookPost>> LatestAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<FacebookPost>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<FacebookPost> posts = [];
        var lifetime = TimeSpan.FromMinutes(5);
        try
        {
            var token = await TokenAsync(cancellationToken);
            if (token is not null)
            {
                var o = options.Value;
                var url = new Uri(o.GraphUrl,
                    $"{o.PageId}/posts?fields=message,created_time,full_picture,permalink_url&limit={o.PostCount}&access_token={Uri.EscapeDataString(token)}");
                var response = await http.GetFromJsonAsync<GraphResponse>(url, cancellationToken);
                posts = [.. (response?.Data ?? []).Where(p => p.PermalinkUrl is not null)
                    .Select(p => new FacebookPost(p.Message, ParseTime(p.CreatedTime), p.FullPicture, p.PermalinkUrl!))];
                lifetime = TimeSpan.FromMinutes(30);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or Azure.RequestFailedException)
        {
            logger.LogWarning(ex, "Facebookberichten konden niet worden opgehaald");
        }

        return cache.Set(CacheKey, posts, lifetime);
    }

    private async Task<string?> TokenAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(options.Value.Token))
        {
            return options.Value.Token;
        }

        var secrets = services.GetService<SecretClient>();
        if (secrets is null)
        {
            return null;
        }

        try
        {
            return (await secrets.GetSecretAsync(options.Value.TokenSecretName, cancellationToken: cancellationToken)).Value.Value;
        }
        catch (Azure.RequestFailedException ex) when (ex.Status == 404)
        {
            // Nog geen token ingesteld: geen feed, alleen de link naar de pagina.
            return null;
        }
    }

    /// <summary>De Graph API geeft <c>2026-09-15T10:00:00+0000</c> (tijdzone zonder dubbele punt).</summary>
    internal static DateTime ParseTime(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return DateTime.MinValue;
        }

        var normalized = value.Length > 5 && value[^5] is '+' or '-' ? $"{value[..^2]}:{value[^2..]}" : value;
        return DateTimeOffset.TryParse(normalized, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
            ? parsed.UtcDateTime
            : DateTime.MinValue;
    }

    private sealed record GraphResponse([property: JsonPropertyName("data")] List<GraphPost>? Data);

    private sealed record GraphPost(
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("created_time")] string? CreatedTime,
        [property: JsonPropertyName("full_picture")] string? FullPicture,
        [property: JsonPropertyName("permalink_url")] string? PermalinkUrl);
}
