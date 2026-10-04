using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Drammers.Infrastructure.Content;
using Drammers.SharedKernel.Errors;

namespace Drammers.Infrastructure.Mailings;

/// <summary>
/// Eén blok van een mailing. Per soort: <c>heading</c> (Text), <c>text</c> (Text, Markdown), <c>image</c> (Image = pad uit
/// de upload-map, Text = omschrijving, Url = link), <c>button</c> (Label, Url), <c>highlight</c> (Label, Text = waarde,
/// Note), <c>divider</c> en <c>closing</c> (Text = de groet, Markdown; samen met de voettekst in één blok onderaan).
/// </summary>
public sealed record MailingBlock(string Type, string? Text = null, string? Label = null, string? Url = null, string? Image = null, string? Note = null);

/// <summary>Voor wie de mail is: vult {voornaam} en {naam}.</summary>
public sealed record MailingPerson(string? FirstName, string? Name);

public sealed record RenderedMailing(string Subject, string Html, string PlainText);

/// <summary>
/// Zet de blokken van een mailing om naar e-mail in de huisstijl van de website en de app (fase 27a): een blauwe kop met
/// het ronde logo, titels in Poppins, tekst in Inter, ronde blokken (16 px), een pilvormige rode knop en onderaan de
/// afsluiting met groet, contactgegevens en afmeldlink. Alles met inline stijlen en tabellen, zodat het ook in Outlook
/// en Gmail goed staat.
/// </summary>
public static partial class MailingRenderer
{
    public const int MaxBlocks = 60;

    public static readonly IReadOnlyList<string> BlockTypes = ["heading", "text", "image", "button", "highlight", "divider", "closing"];

    public static readonly IReadOnlyList<string> Placeholders = ["{voornaam}", "{naam}"];

    /// <summary>De groet als er geen blok Afsluiting is.</summary>
    public const string DefaultClosing = "Groeten,\nDe Vrolijke Drammers";

    // Kleuren en vormen uit de huisstijl (branding basis; website site.css).
    private const string Navy = "#123047";
    private const string Blue = "#087BC1";
    private const string BlueText = "#066AA6";
    private const string Red = "#D4000F";
    private const string Muted = "#4A5B69";
    private const string Grey = "#F0F1F2";
    private const string Line = "#E2E6EA";
    private const string WarmWhite = "#FAFAF7";
    private const string Radius = "16px";
    private const string Body = "font-family:Inter,'Segoe UI',Arial,sans-serif;";
    private const string Display = "font-family:Poppins,'Segoe UI',Arial,sans-serif;";

    /// <summary>Controleert de blokken; gooit een validatiefout met een begrijpelijke melding.</summary>
    public static void Validate(IReadOnlyList<MailingBlock> blocks)
    {
        if (blocks.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Voeg minstens één blok toe.");
        }

        if (blocks.Count > MaxBlocks)
        {
            throw new DomainException(ErrorCodes.Validation, $"Een mailing heeft hooguit {MaxBlocks} blokken.");
        }

        if (blocks.Count(b => b.Type == "closing") > 1)
        {
            throw new DomainException(ErrorCodes.Validation, "Een mailing heeft hooguit één blok Afsluiting.");
        }

        foreach (var (block, i) in blocks.Select((b, i) => (b, i + 1)))
        {
            string Fail(string message) => throw new DomainException(ErrorCodes.Validation, $"Blok {i}: {message}");
            switch (block.Type)
            {
                case "heading" when string.IsNullOrWhiteSpace(block.Text) || block.Text.Length > 200:
                    Fail("vul een kop in van hooguit 200 tekens.");
                    break;
                case "text" when string.IsNullOrWhiteSpace(block.Text) || block.Text.Length > 10000:
                    Fail("vul een tekst in van hooguit 10.000 tekens.");
                    break;
                case "closing" when block.Text?.Length > 2000:
                    Fail("de afsluiting is hooguit 2.000 tekens.");
                    break;
                case "image" when block.Image is null || !UploadedImages.IsUploadPath(block.Image):
                    Fail("kies een foto.");
                    break;
                case "image" when block.Url is not null && !IsWebLink(block.Url):
                    Fail("de link bij de foto moet met https:// beginnen.");
                    break;
                case "button" when string.IsNullOrWhiteSpace(block.Label) || block.Label.Length > 60:
                    Fail("vul een knoptekst in van hooguit 60 tekens.");
                    break;
                case "button" when block.Url is null || !IsWebLink(block.Url):
                    Fail("de link van de knop moet met https:// beginnen.");
                    break;
                case "highlight" when string.IsNullOrWhiteSpace(block.Text) || block.Text.Length > 100 || block.Label?.Length > 100 || block.Note?.Length > 300:
                    Fail("vul de waarde in (hooguit 100 tekens).");
                    break;
                case "heading" or "text" or "image" or "button" or "highlight" or "divider" or "closing":
                    break;
                default:
                    Fail("onbekend soort blok.");
                    break;
            }
        }
    }

    /// <summary>Opent de mail; <paramref name="imageUrl"/> geeft het adres van een geüploade foto, <paramref name="unsubscribeUrl"/> de afmeldlink.</summary>
    public static RenderedMailing Render(
        string subject, string? preheader, IReadOnlyList<MailingBlock> blocks, MailingPerson person, string? logoUrl, Func<string, string?> imageUrl,
        string? unsubscribeUrl)
    {
        var html = new StringBuilder();
        var text = new StringBuilder();
        foreach (var block in blocks.Where(b => b.Type != "closing"))
        {
            AppendBlock(block, person, imageUrl, html, text);
        }

        // De afsluiting staat altijd onderaan, samen met de voettekst; zonder blok de standaardgroet.
        var closing = Fill(blocks.FirstOrDefault(b => b.Type == "closing")?.Text ?? DefaultClosing, person);
        AppendClosing(closing, unsubscribeUrl, html, text);

        var filledSubject = Fill(subject, person);
        return new RenderedMailing(filledSubject, Wrap(filledSubject, preheader is null ? null : Fill(preheader, person), html.ToString(), logoUrl), text.ToString());
    }

    /// <summary>Vult {voornaam} en {naam}; zonder voornaam "Drammer".</summary>
    public static string Fill(string template, MailingPerson person) =>
        template.Replace("{voornaam}", person.FirstName?.Trim() is { Length: > 0 } first ? first : FirstWord(person.Name) ?? "Drammer", StringComparison.OrdinalIgnoreCase)
            .Replace("{naam}", person.Name ?? person.FirstName ?? "", StringComparison.OrdinalIgnoreCase);

    private static void AppendBlock(MailingBlock block, MailingPerson person, Func<string, string?> imageUrl, StringBuilder html, StringBuilder text)
    {
        switch (block.Type)
        {
            case "heading":
                var heading = Fill(block.Text!, person);
                html.Append($"<h1 style=\"margin:0 0 16px;{Display}font-weight:700;font-size:26px;line-height:33px;color:{Navy}\">")
                    .Append(Encode(heading)).Append("</h1>\n");
                text.Append(heading.ToUpperInvariant()).Append("\n\n");
                break;
            case "text":
                var markdown = Fill(block.Text!, person);
                html.Append(StyleText(MarkdownRenderer.ToSafeLetterHtml(markdown) ?? "")).Append('\n');
                text.Append(PlainMarkdown(markdown)).Append("\n\n");
                break;
            case "image" when imageUrl(block.Image!) is { } src:
                var img = $"<img src=\"{Encode(src)}\" width=\"552\" alt=\"{Encode(block.Text ?? "")}\" style=\"display:block;width:100%;max-width:552px;height:auto;border:0;border-radius:{Radius}\">";
                html.Append("<div style=\"margin:0 0 20px\">")
                    .Append(block.Url is null ? img : $"<a href=\"{Encode(block.Url)}\" target=\"_blank\">{img}</a>").Append("</div>\n");
                if (block.Url is not null)
                {
                    text.Append(block.Text is { Length: > 0 } alt ? $"{alt}: " : "").Append(block.Url).Append("\n\n");
                }

                break;
            case "button":
                // Pilvormige knop zoals op de website (Drammers Rood, witte tekst).
                html.Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"margin:4px 0 20px\"><tr>")
                    .Append($"<td style=\"background:{Red};border-radius:999px\">")
                    .Append($"<a href=\"{Encode(block.Url!)}\" target=\"_blank\" style=\"display:inline-block;padding:13px 28px;{Display}font-size:15px;line-height:20px;font-weight:600;color:#FFFFFF;text-decoration:none;border-radius:999px\">")
                    .Append(Encode(block.Label!)).Append("</a></td></tr></table>\n");
                text.Append(block.Label).Append(": ").Append(block.Url).Append("\n\n");
                break;
            case "highlight":
                html.Append($"<div style=\"margin:0 0 20px;padding:18px 20px;background:{Grey};border-radius:{Radius}\">");
                if (block.Label is { Length: > 0 } label)
                {
                    html.Append($"<div style=\"{Body}font-size:12px;line-height:18px;font-weight:700;letter-spacing:0.06em;text-transform:uppercase;color:{BlueText}\">{Encode(Fill(label, person))}</div>");
                }

                html.Append($"<div style=\"{Display}font-weight:700;font-size:22px;line-height:30px;color:{Navy}\">{Encode(Fill(block.Text!, person))}</div>");
                if (block.Note is { Length: > 0 } note)
                {
                    html.Append($"<div style=\"{Body}font-size:14px;line-height:21px;color:{Muted}\">{Encode(Fill(note, person))}</div>");
                }

                html.Append("</div>\n");
                text.Append(block.Label is { Length: > 0 } l ? $"{l}: " : "").Append(Fill(block.Text!, person))
                    .Append(block.Note is { Length: > 0 } n ? $"\n{n}" : "").Append("\n\n");
                break;
            case "divider":
                html.Append($"<hr style=\"margin:8px 0 24px;border:0;border-top:1px solid {Line}\">\n");
                text.Append("——————————\n\n");
                break;
        }
    }

    /// <summary>Het blok Afsluiting: groet, vereniging, contact en afmeldlink in één rond vlak.</summary>
    private static void AppendClosing(string greeting, string? unsubscribeUrl, StringBuilder html, StringBuilder text)
    {
        html.Append($"<div style=\"margin:8px 0 0;padding:20px 22px;background:{Grey};border-radius:{Radius}\">");
        if (!string.IsNullOrWhiteSpace(greeting))
        {
            html.Append(StyleText(MarkdownRenderer.ToSafeLetterHtml(greeting) ?? ""));
            text.Append(PlainMarkdown(greeting)).Append("\n\n");
        }

        html.Append($"<div style=\"padding-top:14px;border-top:1px solid {Line};{Body}font-size:13px;line-height:20px\">")
            .Append($"<div style=\"{Display}font-weight:700;font-size:14px;color:{Navy}\">Carnavalsvereniging De Vrolijke Drammers Loil</div>")
            .Append($"<div><a href=\"mailto:secretaris@vrolijkedrammers.nl\" style=\"color:{BlueText};text-decoration:none\">secretaris@vrolijkedrammers.nl</a>")
            .Append($"&nbsp;&nbsp;|&nbsp;&nbsp;<a href=\"https://www.vrolijkedrammers.nl\" style=\"color:{BlueText};text-decoration:none\">www.vrolijkedrammers.nl</a></div>");
        text.Append("——————————\nCarnavalsvereniging De Vrolijke Drammers Loil\nsecretaris@vrolijkedrammers.nl | www.vrolijkedrammers.nl\n");
        if (unsubscribeUrl is not null)
        {
            html.Append($"<div style=\"color:{Muted}\">Wil je geen nieuwsbrieven en uitnodigingen meer ontvangen? ")
                .Append($"<a href=\"{Encode(unsubscribeUrl)}\" style=\"color:{Muted};text-decoration:underline\">Afmelden</a>.</div>");
            text.Append("Afmelden voor nieuwsbrieven en uitnodigingen: ").Append(unsubscribeUrl).Append('\n');
        }

        html.Append("</div></div>\n");
    }

    /// <summary>Inline stijlen voor de HTML uit Markdown (mailprogramma's negeren stylesheets vaak).</summary>
    private static string StyleText(string html) => html
        .Replace("<p>", $"<p style=\"margin:0 0 16px;{Body}font-size:16px;line-height:26px;color:{Navy}\">", StringComparison.Ordinal)
        .Replace("<ul>", $"<ul style=\"margin:0 0 16px;padding-left:22px;{Body}font-size:16px;line-height:26px;color:{Navy}\">", StringComparison.Ordinal)
        .Replace("<ol>", $"<ol style=\"margin:0 0 16px;padding-left:22px;{Body}font-size:16px;line-height:26px;color:{Navy}\">", StringComparison.Ordinal)
        .Replace("<h2>", $"<h2 style=\"margin:8px 0 12px;{Display}font-weight:700;font-size:20px;line-height:27px;color:{Navy}\">", StringComparison.Ordinal)
        .Replace("<h3>", $"<h3 style=\"margin:8px 0 8px;{Display}font-weight:700;font-size:17px;line-height:24px;color:{Navy}\">", StringComparison.Ordinal)
        .Replace("<a ", $"<a style=\"color:{BlueText};text-decoration:underline\" ", StringComparison.Ordinal);

    /// <summary>
    /// De omlijsting: warm-witte achtergrond, een wit vlak met ronde hoeken en bovenaan de blauwe kop met het ronde logo,
    /// zoals de paginakop van de website en de hero van de app.
    /// </summary>
    private static string Wrap(string subject, string? preheader, string content, string? logoUrl)
    {
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"nl\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
            .Append("<meta name=\"color-scheme\" content=\"light\"><title>").Append(Encode(subject)).Append("</title>")
            .Append("<link href=\"https://fonts.googleapis.com/css2?family=Inter:wght@400;700&family=Poppins:wght@600;700&display=swap\" rel=\"stylesheet\">")
            .Append($"</head><body style=\"margin:0;padding:0;background:{WarmWhite}\">");
        if (preheader is { Length: > 0 })
        {
            html.Append("<div style=\"display:none;max-height:0;overflow:hidden;opacity:0\">").Append(Encode(preheader)).Append("</div>");
        }

        html.Append($"<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"background:{WarmWhite}\"><tr><td align=\"center\" style=\"padding:24px 12px\">")
            .Append($"<table role=\"presentation\" width=\"600\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" style=\"width:100%;max-width:600px;background:#FFFFFF;border:1px solid {Line};border-radius:{Radius};border-collapse:separate;overflow:hidden\">")
            // Kop: Loils Blauw met afgeronde onderkant, logo in een witte cirkel en de naam in het wit.
            .Append($"<tr><td style=\"background:{Blue};border-radius:15px 15px 28px 28px;padding:22px 24px\">")
            .Append("<table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\"><tr>");
        if (logoUrl is { Length: > 0 })
        {
            html.Append($"<td style=\"padding-right:14px;vertical-align:middle\"><img src=\"{Encode(logoUrl)}\" width=\"56\" height=\"56\" alt=\"De Vrolijke Drammers\" style=\"display:block;border:0;border-radius:50%;background:#FFFFFF\"></td>");
        }

        html.Append("<td style=\"vertical-align:middle\">")
            .Append($"<div style=\"{Display}font-weight:700;font-size:20px;line-height:26px;color:#FFFFFF\">De Vrolijke Drammers</div>")
            .Append($"<div style=\"{Body}font-size:13px;line-height:19px;color:rgba(255,255,255,0.85)\">Carnavalsvereniging Loil · sinds 1958</div>")
            .Append("</td></tr></table></td></tr>")
            .Append("<tr><td style=\"padding:28px 24px 24px\">")
            .Append(content)
            .Append("</td></tr></table></td></tr></table></body></html>");
        return html.ToString();
    }

    /// <summary>Markdown leesbaar als platte tekst: links als "tekst (adres)", zonder sterretjes.</summary>
    private static string PlainMarkdown(string markdown) =>
        MarkdownLink().Replace(markdown, "$1 ($2)").Replace("**", "", StringComparison.Ordinal).Replace("__", "", StringComparison.Ordinal).Trim();

    private static bool IsWebLink(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private static string? FirstWord(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim().Split(' ')[0];

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    [GeneratedRegex(@"\[([^\]]+)\]\(([^)\s]+)\)")]
    private static partial Regex MarkdownLink();
}
