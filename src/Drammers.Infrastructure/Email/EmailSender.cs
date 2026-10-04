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
/// </summary>
public sealed record EmailMessage(
    string To, string Subject, string PlainText, string Html, string? ReplyTo = null, string? From = null,
    IReadOnlyList<EmailFile>? Attachments = null);

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

    /// <summary>
    /// Openbare URL van het logo (in een witte cirkel) bovenaan elke e-mail; standaard het logo van de website op
    /// <c>Sales:PublicBaseUrl</c>. Leeg = geen logo.
    /// </summary>
    public string? LogoUrl { get; set; }

    public bool IsConfigured => Endpoint is not null && !string.IsNullOrWhiteSpace(SenderDomain);
}

/// <summary>Verstuurt via Azure Communication Services Email met de managed identity van de API.</summary>
internal sealed class AcsEmailSender(IOptions<EmailOptions> options, TokenCredential credential) : IEmailSender
{
    private readonly EmailClient _client = new(options.Value.Endpoint!, credential);

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var content = new EmailContent(message.Subject) { PlainText = message.PlainText, Html = EmailBranding.WithLogo(message.Html, options.Value.LogoUrl) };
        var sender = message.From is { Length: > 0 } from && options.Value.CustomSenderDomain is { Length: > 0 } custom
            ? $"{from}@{custom}"
            : $"DoNotReply@{options.Value.SenderDomain}";
        var email = new Azure.Communication.Email.EmailMessage(sender, message.To, content);
        if (message.ReplyTo is { Length: > 0 } replyTo)
        {
            email.ReplyTo.Add(new EmailAddress(replyTo));
        }

        foreach (var file in message.Attachments ?? [])
        {
            email.Attachments.Add(new EmailAttachment(file.Name, file.ContentType, BinaryData.FromBytes(file.Content)));
        }

        // WaitUntil.Started: ACS neemt het bericht aan en bezorgt het zelf; een fout bij aannemen gooit hier.
        await _client.SendAsync(WaitUntil.Started, email, cancellationToken);
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
