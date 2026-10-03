using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Content.Import;

/// <summary>Instellingen <c>Website:Import:*</c> (fase 21e).</summary>
public sealed class WordPressImportOptions
{
    public const string SectionName = "Website:Import";

    /// <summary>De oude WordPress-site; alleen afbeeldingen van deze host worden gedownload.</summary>
    public Uri WordPressUrl { get; set; } = new("https://vrolijkedrammers.nl/");
}

public sealed record WpText([property: JsonPropertyName("rendered")] string? Rendered);

public sealed record WpMedia([property: JsonPropertyName("source_url")] string? SourceUrl);

public sealed record WpTerm([property: JsonPropertyName("name")] string? Name, [property: JsonPropertyName("taxonomy")] string? Taxonomy);

public sealed record WpEmbedded(
    [property: JsonPropertyName("wp:featuredmedia")] List<WpMedia>? FeaturedMedia,
    [property: JsonPropertyName("wp:term")] List<List<WpTerm>>? Terms);

/// <summary>Een bericht, pagina of onderscheiding uit de WordPress REST-API.</summary>
public sealed record WpItem(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("date_gmt")] DateTime DateGmt,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("link")] string Link,
    [property: JsonPropertyName("parent")] long Parent,
    [property: JsonPropertyName("title")] WpText Title,
    [property: JsonPropertyName("content")] WpText? Content,
    [property: JsonPropertyName("excerpt")] WpText? Excerpt,
    [property: JsonPropertyName("class_list")] List<string>? ClassList,
    [property: JsonPropertyName("_embedded")] WpEmbedded? Embedded)
{
    public string? FeaturedImage => Embedded?.FeaturedMedia?.FirstOrDefault()?.SourceUrl;

    /// <summary><c>date_gmt</c> staat zonder tijdzone in de API maar is UTC.</summary>
    public DateTime DateUtc => DateTime.SpecifyKind(DateGmt, DateTimeKind.Utc);

    public IEnumerable<string> Categories =>
        Embedded?.Terms?.SelectMany(t => t).Where(t => t.Taxonomy == "category" && t.Name is not null).Select(t => WebUtility.HtmlDecode(t.Name!)) ?? [];
}

public sealed record WpPerson(string Name, string? Title, string? Function, string? Quote, string? ImageUrl);

public sealed record WpAward(string Recipient, string? TypeText, int? Year, string? ImageUrl, string? ContentHtml);

/// <summary>
/// Leest de oude site: de REST-API voor berichten, pagina's en onderscheidingen, de openbare pagina's voor wat niet in de
/// API staat (prinsen, kader en de tekst van een onderscheiding). Afbeeldingen alleen van de eigen host.
/// </summary>
public sealed partial class WordPressSource(HttpClient http, IOptions<WordPressImportOptions> options)
{
    private static readonly HtmlParser Parser = new();

    public const long MaxImageBytes = 25 * 1024 * 1024;

    private Uri Root => options.Value.WordPressUrl;

    public async Task<List<WpItem>> GetAllAsync(string type, bool embed, CancellationToken cancellationToken)
    {
        var result = new List<WpItem>();
        for (var page = 1; page <= 50; page++)
        {
            using var response = await http.GetAsync(new Uri(Root, $"wp-json/wp/v2/{type}?per_page=100&page={page}{(embed ? "&_embed=wp:featuredmedia,wp:term" : "")}"), cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest && page > 1)
            {
                break;
            }

            response.EnsureSuccessStatusCode();
            var items = await response.Content.ReadFromJsonAsync<List<WpItem>>(cancellationToken) ?? [];
            result.AddRange(items);
            var totalPages = response.Headers.TryGetValues("X-WP-TotalPages", out var values) && int.TryParse(values.FirstOrDefault(), out var total) ? total : 1;
            if (page >= totalPages || items.Count == 0)
            {
                break;
            }
        }

        return result;
    }

    /// <summary>Eén bericht of pagina op webadres; <c>null</c> als die niet (meer) bestaat.</summary>
    public async Task<WpItem?> GetBySlugAsync(string type, string slug, CancellationToken cancellationToken)
    {
        var items = await http.GetFromJsonAsync<List<WpItem>>(
            new Uri(Root, $"wp-json/wp/v2/{type}?slug={Uri.EscapeDataString(slug)}&_embed=wp:featuredmedia,wp:term"), cancellationToken);
        return items?.FirstOrDefault();
    }

    public async Task<string> GetHtmlAsync(string pathOrUrl, CancellationToken cancellationToken)
    {
        var uri = new Uri(Root, pathOrUrl);
        EnsureOwnHost(uri);
        return await http.GetStringAsync(uri, cancellationToken);
    }

    /// <summary>Downloadt een afbeelding (eerst het origineel, anders het opgegeven formaat); <c>null</c> als hij niet bestaat.</summary>
    public async Task<byte[]?> GetImageAsync(string url, CancellationToken cancellationToken)
    {
        foreach (var candidate in new[] { HtmlToMarkdown.OriginalImageUrl(url), url }.Distinct())
        {
            var uri = new Uri(candidate);
            EnsureOwnHost(uri);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return bytes.Length is > 0 and <= (int)MaxImageBytes ? bytes : null;
        }

        return null;
    }

    public bool IsOwnHost(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host.Equals(Root.Host, StringComparison.OrdinalIgnoreCase);

    private void EnsureOwnHost(Uri uri)
    {
        if (!uri.Host.Equals(Root.Host, StringComparison.OrdinalIgnoreCase) || uri.Scheme != Root.Scheme)
        {
            throw new InvalidOperationException($"Alleen bestanden van {Root.Host} worden overgezet.");
        }
    }

    // ----- Pagina's die niet in de REST-API staan --------------------------------------------------------------------

    /// <summary>
    /// De adressen onder een menu-item van de oude site (bijvoorbeeld Carnaval), in de volgorde van het menu. Het thema zet
    /// elk uitklapmenu in een <c>.dropdown-container</c>: de eerste link is het menu-item, de links in <c>.dropdown</c> eronder.
    /// </summary>
    public static List<string> ParseSubmenu(string html, string label)
    {
        var document = Parser.ParseDocument(html);
        var container = document.QuerySelectorAll(".dropdown-container")
            .FirstOrDefault(c => string.Equals(Text(c.QuerySelector("a")), label, StringComparison.OrdinalIgnoreCase));
        return container is null
            ? []
            : [.. container.QuerySelectorAll(".dropdown a[href]").Select(a => a.GetAttribute("href")!).Where(h => h.Length > 0).Distinct()];
    }

    /// <summary>Prinsen of jeugdprinsen uit het archief: titel (prinsennaam), naam, jaar en motto per profiel.</summary>
    public static List<WpPerson> ParsePeople(string html)
    {
        var document = Parser.ParseDocument(html);
        return [.. document.QuerySelectorAll(".member-profile").Select(p => new WpPerson(
            Text(p.QuerySelector(".prins-name") ?? p.QuerySelector("h3.member-name")) ?? "",
            Text(p.QuerySelector(".prins-title")),
            Text(p.QuerySelector(".member-function")),
            Text(p.QuerySelector(".member-committee"))?.Trim('"', '“', '”', '\'', ' '),
            p.QuerySelector("img")?.GetAttribute("src")))
            .Where(p => p.Name.Length > 0)];
    }

    /// <summary>Een onderscheiding: ontvanger (h1), soort en jaar (h2, bijvoorbeeld <c>'t drammertje (2025)</c>), foto en tekst.</summary>
    public static WpAward ParseAward(string html)
    {
        var document = Parser.ParseDocument(html);
        var article = document.QuerySelector("article.single-award-container") ?? document.Body!;
        var heading = Text(article.QuerySelector("header h2"));
        var year = heading is null ? null : YearPattern().Match(heading) is { Success: true } m ? int.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture) : (int?)null;
        return new WpAward(
            Text(article.QuerySelector("header h1")) ?? "",
            heading is null ? null : YearInParentheses().Replace(heading, "").Trim(),
            year,
            article.QuerySelector(".award-winner-image img")?.GetAttribute("src"),
            article.QuerySelector(".award-content")?.InnerHtml);
    }

    private static string? Text(AngleSharp.Dom.IElement? element)
    {
        var text = element?.TextContent;
        return string.IsNullOrWhiteSpace(text) ? null : WhitespaceRun().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex YearPattern();

    [GeneratedRegex(@"\(\s*\d{4}\s*\)")]
    private static partial Regex YearInParentheses();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
