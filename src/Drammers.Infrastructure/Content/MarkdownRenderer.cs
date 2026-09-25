using Ganss.Xss;
using Markdig;

namespace Drammers.Infrastructure.Content;

/// <summary>Markdown → veilige HTML (fase 5): ruwe HTML wordt niet doorgelaten en het resultaat wordt gesanitized.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().DisableHtml().UseAutoLinks().UseEmphasisExtras().Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string? ToSafeHtml(string? markdown) =>
        string.IsNullOrWhiteSpace(markdown) ? null : Sanitizer.Sanitize(Markdown.ToHtml(markdown, Pipeline));

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
                link.SetAttribute("rel", "noopener noreferrer nofollow");
                link.SetAttribute("target", "_blank");
            }
        };
        return sanitizer;
    }
}
