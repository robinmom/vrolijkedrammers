using System.Text.RegularExpressions;
using Drammers.Infrastructure.Contact;
using Microsoft.Extensions.Options;

namespace Drammers.Website.Content;

/// <summary>
/// Geen e-mailadressen van de vereniging op de website (fase 21i): links naar het contactformulier, met de juiste ontvanger
/// al gekozen. Ook in inhoud uit het portal of de WordPress-import worden <c>mailto:</c>-links en losse adressen van het
/// eigen domein vervangen.
/// </summary>
public sealed partial class ContactLinks(IOptions<ContactOptions> options)
{
    private const string Domain = "@vrolijkedrammers.nl";

    public static string Href(string? recipient) => recipient is null ? "/contact#formulier" : $"/contact?aan={recipient}#formulier";

    /// <summary>De ontvanger bij een adres (op het deel vóór de @), of <c>null</c> als die niet in de lijst staat.</summary>
    public string? RecipientFor(string localPart) =>
        options.Value.Recipients
            .Where(r => r.Value.Address.Split('@')[0].Equals(localPart, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Key.ToLowerInvariant())
            .FirstOrDefault();

    public string? Rewrite(string? html)
    {
        if (string.IsNullOrEmpty(html) || !html.Contains(Domain, StringComparison.OrdinalIgnoreCase))
        {
            return html;
        }

        html = MailtoLink().Replace(html, m =>
        {
            var text = Address().Replace(m.Groups["text"].Value, "het contactformulier");
            return $"<a href=\"{Href(RecipientFor(m.Groups["local"].Value))}\">{text}</a>";
        });
        return TextBetweenTags().Replace(html, m => Address().Replace(m.Value, a =>
            $"<a href=\"{Href(RecipientFor(a.Groups["local"].Value))}\">het contactformulier</a>"));
    }

    [GeneratedRegex("""<a\b[^>]*\bhref\s*=\s*["']mailto:(?<local>[^"'@?]+)@vrolijkedrammers\.nl[^"']*["'][^>]*>(?<text>.*?)</a>""",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MailtoLink();

    [GeneratedRegex(@"(?<local>[A-Za-z0-9._%+-]+)@vrolijkedrammers\.nl", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex("(?<=^|>)[^<]+")]
    private static partial Regex TextBetweenTags();
}
