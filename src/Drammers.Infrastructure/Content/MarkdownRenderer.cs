using Ganss.Xss;
using Markdig;

namespace Drammers.Infrastructure.Content;

/// <summary>Markdown → veilige HTML (fase 5): ruwe HTML wordt niet doorgelaten en het resultaat wordt gesanitized.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UseAutoLinks().UseEmphasisExtras().UsePipeTables().Build();

    /// <summary>Voor e-mail: elke Enter is een nieuwe regel (zoals in een brief), niet alleen een lege regel.</summary>
    private static readonly MarkdownPipeline LetterPipeline =
        new MarkdownPipelineBuilder().DisableHtml().UseAutoLinks().UseEmphasisExtras().UseSoftlineBreakAsHardlineBreak().Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? ToSafeHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : Sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));

    /// <summary>Als <see cref="ToSafeHtml"/>, maar met regeleinden zoals in een brief (mailings).</summary>
    public static string? ToSafeLetterHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : Sanitizer.Sanitize(Markdown.ToHtml(markdown, LetterPipeline));

    /// <summary>Eigen site: een pad (<c>/fotos/…</c>), een anker, mail/telefoon of een adres op vrolijkedrammers.nl.</summary>
    public static bool IsOwnSite(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            return true;
        }

        if ((href.StartsWith('/') && !href.StartsWith("//", StringComparison.Ordinal)) || href.StartsWith('#')
            || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) || href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(href, UriKind.Absolute, out var uri)
            && (uri.Host.Equals("vrolijkedrammers.nl", StringComparison.OrdinalIgnoreCase)
                || uri.Host.EndsWith(".vrolijkedrammers.nl", StringComparison.OrdinalIgnoreCase));
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.UnionWith(["https", "mailto", "tel"]);
        sanitizer.AllowedAttributes.Remove("style");
        sanitizer.AllowedTags.Remove("img");
        sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Dom.IElement { TagName: "A" } link)
            {
                // Links naar onze eigen site (en mail/telefoon) openen in hetzelfde venster; andere sites in een nieuw tabblad.
                if (IsOwnSite(link.GetAttribute("href")))
                {
                    link.RemoveAttribute("target");
                    link.RemoveAttribute("rel");
                }
                else
                {
                    link.SetAttribute("rel", "noopener noreferrer nofollow");
                    link.SetAttribute("target", "_blank");
                }
            }
        };
        return sanitizer;
    }
}
