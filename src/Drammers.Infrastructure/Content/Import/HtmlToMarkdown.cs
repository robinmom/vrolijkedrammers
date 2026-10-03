using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Drammers.Infrastructure.Content.Import;

/// <summary>Tekst uit WordPress als Markdown, plus de afbeeldingen die erin stonden (voor een fotoalbum).</summary>
public sealed record ConvertedHtml(string Markdown, IReadOnlyList<string> Images);

/// <summary>
/// Zet WordPress-HTML om naar Markdown (fase 21e): alinea's, koppen, vet/cursief, links, lijsten en tabellen. Afbeeldingen
/// en galerijen komen niet in de tekst maar in <see cref="ConvertedHtml.Images"/>; scripts, stijlen en formulieren vallen weg.
/// </summary>
public static partial class HtmlToMarkdown
{
    private static readonly HtmlParser Parser = new();

    public static ConvertedHtml Convert(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return new ConvertedHtml("", []);
        }

        var document = Parser.ParseDocument($"<body>{html}</body>");
        var images = new List<string>();
        var markdown = new StringBuilder();
        Blocks(document.Body!, markdown, images);
        var text = MultipleBlankLines().Replace(markdown.ToString(), "\n\n").Trim();
        return new ConvertedHtml(text, images.Distinct().ToList());
    }

    /// <summary>Platte tekst (voor een samenvatting), zonder HTML-tags en met entities omgezet.</summary>
    public static string PlainText(string? html, int maxLength = int.MaxValue)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        var text = Whitespace().Replace(Parser.ParseDocument($"<body>{html}</body>").Body!.TextContent, " ").Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', maxLength - 1);
        return text[..(cut > 0 ? cut : maxLength - 1)].TrimEnd(',', ';', ':') + "…";
    }

    /// <summary>Het adres van de originele afbeelding: WordPress zet de maat in de naam (<c>foto-1024x768.jpg</c>).</summary>
    public static string OriginalImageUrl(string url) => SizeSuffix().Replace(url, "$1");

    private static void Blocks(INode parent, StringBuilder output, List<string> images)
    {
        foreach (var node in parent.ChildNodes)
        {
            if (node is not IElement element)
            {
                var text = Clean(node.TextContent);
                if (text.Trim().Length > 0)
                {
                    output.Append(text.Trim()).Append("\n\n");
                }

                continue;
            }

            switch (element.LocalName)
            {
                case "script" or "style" or "form" or "noscript" or "iframe" or "svg" or "button":
                    break;
                case "img":
                    AddImage(element, images);
                    break;
                case "figure" or "picture":
                    CollectImages(element, images);
                    var caption = element.QuerySelector("figcaption");
                    if (caption is not null && element.QuerySelectorAll("img").Length <= 1 && caption.TextContent.Trim().Length > 0)
                    {
                        output.Append('*').Append(Clean(caption.TextContent).Trim()).Append("*\n\n");
                    }

                    break;
                case "p":
                    CollectImages(element, images);
                    var paragraph = Inline(element).Trim();
                    if (paragraph.Length > 0)
                    {
                        output.Append(paragraph).Append("\n\n");
                    }

                    break;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                    var heading = Inline(element).Trim().Trim('*').Trim();
                    if (heading.Length > 0)
                    {
                        var level = Math.Clamp(element.LocalName[1] - '0', 2, 4);
                        output.Append(new string('#', level)).Append(' ').Append(heading).Append("\n\n");
                    }

                    break;
                case "ul" or "ol":
                    var index = 1;
                    foreach (var item in element.Children.Where(c => c.LocalName == "li"))
                    {
                        CollectImages(item, images);
                        var line = Inline(item).Trim();
                        if (line.Length > 0)
                        {
                            output.Append(element.LocalName == "ol" ? $"{index++}. " : "- ").Append(line).Append('\n');
                        }
                    }

                    output.Append('\n');
                    break;
                case "blockquote":
                    var quote = Inline(element).Trim();
                    if (quote.Length > 0)
                    {
                        output.Append("> ").Append(quote.Replace("\n", "\n> ", StringComparison.Ordinal)).Append("\n\n");
                    }

                    break;
                case "table":
                    Table(element, output);
                    break;
                case "hr":
                    output.Append("---\n\n");
                    break;
                default:
                    Blocks(element, output, images);
                    break;
            }
        }
    }

    private static string Inline(INode parent)
    {
        var result = new StringBuilder();
        foreach (var node in parent.ChildNodes)
        {
            if (node is not IElement element)
            {
                result.Append(Clean(node.TextContent));
                continue;
            }

            var inner = Inline(element);
            switch (element.LocalName)
            {
                case "strong" or "b":
                    result.Append(Wrap(inner, "**"));
                    break;
                case "em" or "i":
                    result.Append(Wrap(inner, "*"));
                    break;
                case "br":
                    result.Append('\n');
                    break;
                case "a":
                    var href = element.GetAttribute("href");
                    var label = inner.Trim();
                    result.Append(href is not null && Uri.TryCreate(href, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto" && label.Length > 0
                        ? $"[{label}]({href})"
                        : label);
                    break;
                case "img" or "script" or "style" or "svg":
                    break;
                default:
                    result.Append(inner);
                    break;
            }
        }

        return result.ToString();
    }

    private static void Table(IElement table, StringBuilder output)
    {
        var rows = table.QuerySelectorAll("tr")
            .Select(r => r.Children.Where(c => c.LocalName is "td" or "th").Select(c => Inline(c).Replace("|", "/", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim()).ToList())
            .Where(r => r.Count > 0 && r.Any(c => c.Length > 0))
            .ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var columns = rows.Max(r => r.Count);
        foreach (var (row, i) in rows.Select((r, i) => (r, i)))
        {
            output.Append("| ").Append(string.Join(" | ", Enumerable.Range(0, columns).Select(c => c < row.Count ? row[c] : ""))).Append(" |\n");
            if (i == 0)
            {
                output.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", columns))).Append('\n');
            }
        }

        output.Append('\n');
    }

    private static void CollectImages(IElement element, List<string> images)
    {
        foreach (var img in element.QuerySelectorAll("img"))
        {
            AddImage(img, images);
        }
    }

    private static void AddImage(IElement img, List<string> images)
    {
        // Galerijen (FooGallery) linken naar het origineel en zetten in src alleen een plaatshouder; lazy-loading thema's
        // zetten het echte adres soms in data-src.
        var link = img.Closest("a")?.GetAttribute("href");
        var src = link is not null && ImageFile().IsMatch(link)
            ? link
            : img.GetAttribute("data-src") ?? img.GetAttribute("data-lazy-src") ?? img.GetAttribute("data-src-fg") ?? img.GetAttribute("src");
        if (src is not null && Uri.TryCreate(src, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && !src.Contains("logo", StringComparison.OrdinalIgnoreCase))
        {
            images.Add(OriginalImageUrl(src));
        }
    }

    private static string Wrap(string text, string marker)
    {
        var trimmed = text.Trim();
        return trimmed.Length == 0 ? text : $"{(text.StartsWith(' ') ? " " : "")}{marker}{trimmed}{marker}{(text.EndsWith(' ') ? " " : "")}";
    }

    private static string Clean(string text) => Whitespace().Replace(WebUtility.HtmlDecode(text).Replace(' ', ' '), " ");

    [GeneratedRegex(@"[ \t\r\n]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex MultipleBlankLines();

    [GeneratedRegex(@"\.(jpe?g|png|webp|gif)$", RegexOptions.IgnoreCase)]
    private static partial Regex ImageFile();

    [GeneratedRegex(@"-\d{2,5}x\d{2,5}(\.[A-Za-z]{3,4})$")]
    private static partial Regex SizeSuffix();
}
