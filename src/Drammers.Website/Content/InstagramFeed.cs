using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Website.Content;

/// <summary>
/// Instellingen <c>Website:Instagram:*</c> (2026-10-16, eerder de Facebookfeed van fase 21c). Instagram werkt via de Graph
/// API van Facebook: het Instagram-account (zakelijk of maker) is gekoppeld aan de Facebookpagina, en het paginatoken in
/// Key Vault (secret <see cref="TokenSecretName"/>, met de rechten instagram_basic en pages_show_list) geeft toegang.
/// <see cref="Token"/> alleen lokaal of in tests.
/// </summary>
public sealed class InstagramOptions
{
    public const string SectionName = "Website:Instagram";

    public Uri GraphUrl { get; set; } = new("https://graph.facebook.com/v23.0/");

    /// <summary>Id van de Facebookpagina van De Vrolijke Drammers (daaraan hangt het Instagram-account).</summary>
    public string PageId { get; set; } = "263171860384920";

    public string TokenSecretName { get; set; } = "facebook-page-token";

    public string? Token { get; set; }

    public int PostCount { get; set; } = 6;
}

/// <param name="ImageUrl">De foto; bij een video de voorbeeldafbeelding.</param>
/// <param name="Caption">Het bijschrift.</param>
/// <param name="CreatedAt">Wanneer de post is geplaatst.</param>
/// <param name="Link">De post op Instagram.</param>
/// <param name="Video">Een video of reel.</param>
public sealed record InstagramPost(string? ImageUrl, string? Caption, DateTime CreatedAt, string Link, bool Video);

/// <summary>
/// De laatste posts van Instagram, opgehaald door de server (Graph API) en getoond in de eigen huisstijl: bezoekers laden
/// geen script of cookies van Instagram, alleen de afbeeldingen. Een half uur in het geheugen; zonder token, zonder
/// gekoppeld Instagram-account of bij een fout blijft de lijst leeg en toont de site alleen de link naar Instagram.
/// </summary>
public sealed class InstagramFeed(HttpClient http, IOptions<InstagramOptions> options, IServiceProvider services, IMemoryCache cache, ILogger<InstagramFeed> logger)
{
    private const string CacheKey = "website:instagram-posts";

    public async Task<IReadOnlyList<InstagramPost>> LatestAsync(CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(CacheKey, out IReadOnlyList<InstagramPost>? cached) && cached is not null)
        {
            return cached;
        }

        IReadOnlyList<InstagramPost> posts = [];
        var lifetime = TimeSpan.FromMinutes(5);
        try
        {
            var token = await TokenAsync(cancellationToken);
            if (token is not null)
            {
                var o = options.Value;
                var access = Uri.EscapeDataString(token);
                var page = await http.GetFromJsonAsync<PageResponse>(new Uri(o.GraphUrl, $"{o.PageId}?fields=instagram_business_account&access_token={access}"), cancellationToken);
                if (page?.Account?.Id is { Length: > 0 } accountId)
                {
                    var media = await http.GetFromJsonAsync<MediaResponse>(new Uri(o.GraphUrl,
                        $"{accountId}/media?fields=caption,media_type,media_url,thumbnail_url,permalink,timestamp&limit={o.PostCount}&access_token={access}"), cancellationToken);
                    posts = [.. (media?.Data ?? []).Where(m => m.Permalink is not null)
                        .Select(m => new InstagramPost(m.MediaType == "VIDEO" ? m.ThumbnailUrl : m.MediaUrl, m.Caption, ParseTime(m.Timestamp), m.Permalink!, m.MediaType == "VIDEO"))];
                    lifetime = TimeSpan.FromMinutes(30);
                }
                else
                {
                    logger.LogWarning("Geen Instagram-account gekoppeld aan de Facebookpagina {PageId}", o.PageId);
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or Azure.RequestFailedException)
        {
            logger.LogWarning(ex, "Instagramposts konden niet worden opgehaald");
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
            // Nog geen token ingesteld: geen feed, alleen de link naar Instagram.
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

    private sealed record PageResponse([property: JsonPropertyName("instagram_business_account")] AccountRef? Account);

    private sealed record AccountRef([property: JsonPropertyName("id")] string? Id);

    private sealed record MediaResponse([property: JsonPropertyName("data")] List<Media>? Data);

    private sealed record Media(
        [property: JsonPropertyName("caption")] string? Caption,
        [property: JsonPropertyName("media_type")] string? MediaType,
        [property: JsonPropertyName("media_url")] string? MediaUrl,
        [property: JsonPropertyName("thumbnail_url")] string? ThumbnailUrl,
        [property: JsonPropertyName("permalink")] string? Permalink,
        [property: JsonPropertyName("timestamp")] string? Timestamp);
}
