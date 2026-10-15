using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Azure.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Drammers.Infrastructure.Email;

/// <summary>
/// Mailings via de echte mailbox in Microsoft 365 (2026-10-15): Outlook.com zette mailings via ACS in Ongewenst, terwijl
/// SPF, DKIM en DMARC slaagden; de gedeelde servers van ACS hebben weinig reputatie. Via Microsoft Graph
/// (<c>/users/{adres}/sendMail</c>, als MIME zodat de afmeldkoppen meegaan) gaat de mail via onze eigen Microsoft 365 en
/// staat hij ook bij Verzonden items. Alleen voor berichten met <see cref="EmailMessage.ViaMailbox"/> en een afzender uit
/// <see cref="EmailOptions.MailboxSenders"/>; de rest, en elke mail als de mailbox niet mag (nog geen rechten), gaat via ACS.
/// De managed identity mag alleen namens die mailboxen versturen (Exchange RBAC for Applications, infra/m365).
/// </summary>
public sealed partial class MailboxEmailSender(
    IEmailSender fallback, HttpClient http, TokenCredential credential, IOptions<EmailOptions> options, ILogger<MailboxEmailSender> logger) : IEmailSender
{
    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var mailbox = Mailbox(message, settings);
        if (mailbox is null)
        {
            await fallback.SendAsync(message, cancellationToken);
            return;
        }

        var prepared = EmailRedirect.Apply(message, settings.RedirectAllTo);
        prepared = prepared with { Html = EmailBranding.WithLogo(prepared.Html, settings.LogoUrl) };
        var mime = MimeMessage.Build(mailbox, prepared, DateTimeOffset.UtcNow);
        var token = await credential.GetTokenAsync(new TokenRequestContext(GraphScope), cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{settings.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(mailbox)}/sendMail")
        {
            // Graph verwacht de MIME-tekst base64-gecodeerd met Content-Type text/plain.
            Content = new StringContent(Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)), Encoding.ASCII, "text/plain"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            // Nog geen rechten op deze mailbox (of geen mailbox): niet blijven hangen, via ACS versturen.
            LogMailboxUnavailable((int)response.StatusCode);
            await fallback.SendAsync(message, cancellationToken);
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            // Andere fouten (bijv. 429 of 5xx): de outbox probeert het later opnieuw.
            throw new HttpRequestException($"Versturen via de mailbox mislukte ({(int)response.StatusCode}).", null, response.StatusCode);
        }
    }

    /// <summary>Het mailboxadres voor dit bericht, of <c>null</c> als het via ACS gaat.</summary>
    public static string? Mailbox(EmailMessage message, EmailOptions options)
    {
        if (!message.ViaMailbox || message.From is not { Length: > 0 } from || options.CustomSenderDomain is not { Length: > 0 } domain)
        {
            return null;
        }

        var allowed = (options.MailboxSenders ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return allowed.Contains(from, StringComparer.OrdinalIgnoreCase) ? $"{from.ToLowerInvariant()}@{domain}" : null;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Mailbox in Microsoft 365 niet beschikbaar ({Status}); verstuurd via ACS.")]
    private partial void LogMailboxUnavailable(int status);
}

/// <summary>
/// Een e-mail als MIME-tekst (RFC 5322/2045): platte tekst en HTML als alternatieven, bijlagen erbij, kopteksten met
/// UTF-8 (RFC 2047) en de afmeldkoppen. Message-ID en de afzendernaam vult Exchange zelf in.
/// </summary>
public static class MimeMessage
{
    public static string Build(string from, EmailMessage message, DateTimeOffset date)
    {
        var mime = new StringBuilder();
        void Header(string name, string value) => mime.Append(name).Append(": ").Append(value).Append("\r\n");

        Header("From", from);
        Header("To", message.To);
        if (message.ReplyTo is { Length: > 0 } replyTo)
        {
            Header("Reply-To", replyTo);
        }

        Header("Subject", EncodeWord(message.Subject));
        Header("Date", date.ToString("r", System.Globalization.CultureInfo.InvariantCulture));
        Header("MIME-Version", "1.0");
        if (message.UnsubscribeUrl is { Length: > 0 } unsubscribe)
        {
            Header("List-Unsubscribe", $"<{unsubscribe}>");
            Header("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        }

        var alternative = $"alt-{Guid.NewGuid():N}";
        var attachments = message.Attachments ?? [];
        string? mixed = null;
        if (attachments.Count > 0)
        {
            mixed = $"mix-{Guid.NewGuid():N}";
            Header("Content-Type", $"multipart/mixed; boundary=\"{mixed}\"");
            mime.Append("\r\n--").Append(mixed).Append("\r\n");
        }

        mime.Append("Content-Type: multipart/alternative; boundary=\"").Append(alternative).Append("\"\r\n\r\n");
        Part(mime, alternative, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(message.PlainText));
        Part(mime, alternative, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(message.Html));
        mime.Append("--").Append(alternative).Append("--\r\n");

        if (mixed is not null)
        {
            foreach (var file in attachments)
            {
                var name = EncodeWord(file.Name);
                Part(mime, mixed, $"{file.ContentType}; name=\"{name}\"", file.Content, $"attachment; filename=\"{name}\"");
            }

            mime.Append("--").Append(mixed).Append("--\r\n");
        }

        return mime.ToString();
    }

    private static void Part(StringBuilder mime, string boundary, string contentType, byte[] content, string? disposition = null)
    {
        mime.Append("--").Append(boundary).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Transfer-Encoding: base64\r\n");
        if (disposition is not null)
        {
            mime.Append("Content-Disposition: ").Append(disposition).Append("\r\n");
        }

        mime.Append("\r\n").Append(Convert.ToBase64String(content, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
    }

    /// <summary>Alleen ASCII blijft leesbaar; anders een UTF-8 encoded-word (RFC 2047).</summary>
    private static string EncodeWord(string value) =>
        value.All(c => c is >= ' ' and <= '~') ? value : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
}
