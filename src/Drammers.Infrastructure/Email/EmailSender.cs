using Azure;
using Azure.Communication.Email;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Email;

/// <summary>
/// Een e-mail met platte tekst en HTML (docs/06: geen persoonsgegevens in logs). <paramref name="ReplyTo"/>: antwoorden
/// gaan naar dit adres. <paramref name="From"/>: afzendernaam vóór de @ (bijv. <c>secretaris</c>), alleen gebruikt als er
/// een eigen domein is ingesteld (<see cref="EmailOptions.CustomSenderDomain"/>); anders blijft het DoNotReply.
/// <paramref name="UnsubscribeUrl"/>: afmelden met één klik (koppen List-Unsubscribe en List-Unsubscribe-Post, RFC 8058),
/// alleen bij nieuwsbrieven en uitnodigingen; mailprogramma's tonen dan een eigen afmeldknop. <paramref name="ViaMailbox"/>:
/// versturen via de echte mailbox in Microsoft 365 als die afzender daarvoor is ingesteld (<see cref="MailboxEmailSender"/>).
/// </summary>
public sealed record EmailMessage(
    string To, string Subject, string PlainText, string Html, string? ReplyTo = null, string? From = null,
    IReadOnlyList<EmailFile>? Attachments = null, string? UnsubscribeUrl = null, bool ViaMailbox = false);

/// <summary>Een bijlage, bijvoorbeeld de factuur als PDF (fase 27e).</summary>
public sealed record EmailFile(string Name, string ContentType, byte[] Content);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>Instellingen <c>Email__*</c>, gezet door Bicep (Azure Communication Services, domein van Azure).</summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public Uri? Endpoint { get; set; }

    /// <summary>Bijv. <c>xxxx.azurecomm.net</c>; de afzender wordt <c>DoNotReply@</c> dit domein.</summary>
    public string? SenderDomain { get; set; }

    /// <summary>Eigen, in ACS geverifieerd domein (bijv. <c>vrolijkedrammers.nl</c>); dan kan een bericht een eigen afzender hebben.</summary>
    public string? CustomSenderDomain { get; set; }

    /// <summary>Afzenders die in ACS op het eigen domein bestaan, kommagescheiden (bijv. <c>secretaris,optocht</c>).</summary>
    public string? CustomSenders { get; set; }

    /// <summary>
    /// Openbare URL van het logo (in een witte cirkel) bovenaan elke e-mail; standaard het logo van de website op
    /// <c>Sales:PublicBaseUrl</c>. Leeg = geen logo.
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Alleen Dev (besluit 2026-10-07): elke e-mail gaat naar dit adres in plaats van naar de ontvanger, omdat Dev een
    /// kopie van de echte ledenadministratie heeft. Leeg (Prod) = gewoon versturen.
    /// </summary>
    public string? RedirectAllTo { get; set; }

    /// <summary>
    /// Afzenders (kommagescheiden, bijv. <c>voorzitter,secretaris</c>) waarvan mailings via de echte mailbox in Microsoft
    /// 365 gaan in plaats van via ACS; leeg = alles via ACS. Pas zetten na infra/m365/mailbox-verzenden.ps1.
    /// </summary>
    public string? MailboxSenders { get; set; }

    public string GraphBaseUrl { get; set; } = "https://graph.microsoft.com";

    public bool IsConfigured => Endpoint is not null && !string.IsNullOrWhiteSpace(SenderDomain);
}

/// <summary>Verstuurt via Azure Communication Services Email met de managed identity van de API.</summary>
internal sealed partial class AcsEmailSender(IOptions<EmailOptions> options, TokenCredential credential, ILogger<AcsEmailSender> logger) : IEmailSender
{
    private readonly EmailClient _client = new(options.Value.Endpoint!, credential);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        message = EmailRedirect.Apply(message, options.Value.RedirectAllTo);
        var content = new EmailContent(message.Subject) { PlainText = message.PlainText, Html = EmailBranding.WithLogo(message.Html, options.Value.LogoUrl) };
        var sender = EmailSenderAddress.For(message, options.Value);
        var email = new Azure.Communication.Email.EmailMessage(sender, message.To, content);
        if (message.ReplyTo is { Length: > 0 } replyTo)
        {
            email.ReplyTo.Add(new EmailAddress(replyTo));
        }

        // Outlook.com en Gmail verwachten deze koppen bij mail naar meerdere mensen; zonder komt die eerder in Ongewenst.
        if (message.UnsubscribeUrl is { Length: > 0 } unsubscribe)
        {
            email.Headers.Add("List-Unsubscribe", $"<{unsubscribe}>");
            email.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        foreach (var file in message.Attachments ?? [])
        {
            email.Attachments.Add(new EmailAttachment(file.Name, file.ContentType, BinaryData.FromBytes(file.Content)));
        }

        // WaitUntil.Started: ACS neemt het bericht aan en bezorgt het zelf; een fout bij aannemen gooit hier.
        try
        {
            await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
        }
        catch (RequestFailedException ex) when (ex.Status == 400 && email.Headers.ContainsKey("List-Unsubscribe"))
        {
            // Vangnet: weigert ACS de afmeldkoppen, dan gaat de mail zonder (de afmeldlink staat ook onderaan de mail).
            LogUnsubscribeHeadersRejected(ex.ErrorCode);
            email.Headers.Remove("List-Unsubscribe");
            email.Headers.Remove("List-Unsubscribe-Post");
            await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "ACS weigerde de afmeldkoppen ({ErrorCode}); verstuurd zonder List-Unsubscribe.")]
    private partial void LogUnsubscribeHeadersRejected(string? errorCode);
}

/// <summary>
/// Het afzenderadres (fase 7, eigen maildomein). Met een gekoppeld eigen domein: de gevraagde afzender
/// (<see cref="EmailMessage.From"/>), anders die van het antwoordadres als dat een eigen afzender is (een bericht met
/// Reply-To secretaris@vrolijkedrammers.nl komt dan ook van secretaris@), anders DoNotReply@ het eigen domein. Zonder
/// eigen domein altijd DoNotReply@ het Azure-domein. Alleen afzenders die in ACS bestaan, anders weigert ACS het bericht.
/// </summary>
public static class EmailSenderAddress
{
    public static string For(EmailMessage message, EmailOptions options)
    {
        if (options.CustomSenderDomain is not { Length: > 0 } domain)
        {
            return $"DoNotReply@{options.SenderDomain}";
        }

        var allowed = (options.CustomSenders ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (message.From is { Length: > 0 } from && allowed.Contains(from))
        {
            return $"{from.ToLowerInvariant()}@{domain}";
        }

        if (message.ReplyTo is { Length: > 0 } replyTo && replyTo.Split('@') is [var local, var replyDomain]
            && replyDomain.Equals(domain, StringComparison.OrdinalIgnoreCase) && allowed.Contains(local))
        {
            return $"{local.ToLowerInvariant()}@{domain}";
        }

        return $"DoNotReply@{domain}";
    }
}

/// <summary>Omleiden in Dev: naar één testadres, met de oorspronkelijke ontvanger in het onderwerp en bovenaan de tekst.</summary>
public static class EmailRedirect
{
    public static EmailMessage Apply(EmailMessage message, string? redirectTo)
    {
        if (string.IsNullOrWhiteSpace(redirectTo))
        {
            return message;
        }

        var notice = $"Omgeleid vanuit Dev: deze e-mail was bedoeld voor {message.To}.";
        return message with
        {
            To = redirectTo.Trim(),
            Subject = $"[Dev → {message.To}] {message.Subject}",
            PlainText = $"{notice}\n\n{message.PlainText}",
            Html = $"<p style=\"margin:0 0 16px;padding:8px 12px;background:#FFF4D6;border-radius:8px;font-family:sans-serif;font-size:13px\">{System.Net.WebUtility.HtmlEncode(notice)}</p>\n{message.Html}",
        };
    }
}

/// <summary>Het logo van de vereniging bovenaan elke e-mail (opmaak in de huisstijl volgt met de e-mailsjablonen).</summary>
public static class EmailBranding
{
    public static string WithLogo(string html, string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl) || html.Contains(logoUrl, StringComparison.Ordinal))
        {
            return html;
        }

        var src = System.Net.WebUtility.HtmlEncode(logoUrl);
        return $"<div style=\"text-align:center;margin:0 0 20px\"><img src=\"{src}\" width=\"80\" height=\"80\" alt=\"De Vrolijke Drammers\" style=\"display:inline-block;border:0\"></div>\n{html}";
    }
}

/// <summary>Zonder ACS (lokaal): schrijft alleen het onderwerp naar de log, niet het adres of de inhoud.</summary>
internal sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        LogNotSent(logger, message.Subject);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-mail niet verstuurd (geen Email__Endpoint): {Subject}")]
    private static partial void LogNotSent(ILogger logger, string subject);
}
