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
/// Hapert Microsoft 365 zelf, dan volgt een tweede poging; daarna gaat een bericht met
/// <see cref="EmailMessage.MailboxOptional"/> alsnog via ACS en blijft bulkmail in de outbox wachten.
/// </summary>
public sealed partial class MailboxEmailSender(
    IEmailSender fallback, HttpClient http, TokenCredential credential, IOptions<EmailOptions> options, ILogger<MailboxEmailSender> logger) : IEmailSender
{
    private static readonly string[] GraphScope = ["https://graph.microsoft.com/.default"];

    /// <summary>
    /// Twee pogingen, en per poging niet langer wachten dan dit. Op 12-11-2026 liet Exchange Online ongeveer een derde
    /// van de aanroepen vallen met een 500 of een 504 na dertig seconden; met twee pogingen komt vrijwel alles er alsnog
    /// doorheen, en blijft een bezoeker die op zijn aanmeldcode wacht nooit langer dan veertig seconden hangen.
    /// </summary>
    private const int Attempts = 2;

    private static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(20);

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
        var url = $"{settings.GraphBaseUrl.TrimEnd('/')}/v1.0/users/{Uri.EscapeDataString(mailbox)}/sendMail";
        // Graph verwacht de MIME-tekst base64-gecodeerd met Content-Type text/plain (geen HTML naar een browser).
        var body = Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(mime)));

        HttpStatusCode? status = null;
        for (var attempt = 1; attempt <= Attempts; attempt++)
        {
            var (code, detail) = await TrySendAsync(url, body, token.Token, cancellationToken);
            status = code;
            if (code is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // Nog geen rechten op deze mailbox (of geen mailbox): niet blijven proberen, via ACS versturen.
                LogMailboxUnavailable((int)code.Value);
                await fallback.SendAsync(message, cancellationToken);
                return;
            }

            if (code is not null && (int)code.Value is >= 200 and < 300)
            {
                return;
            }

            LogMailboxAttemptFailed(attempt, code is null ? 0 : (int)code.Value, detail);
        }

        if (message.MailboxOptional)
        {
            // Iemand wacht op dit bericht en de mailbox komt er niet doorheen; dan is ACS beter dan niets. De aanmelding
            // liep hier op 12-11-2026 op stuk: de bezoeker kreeg een foutmelding terwijl ACS gewoon werkte.
            LogMailboxFallback(status is null ? 0 : (int)status.Value);
            await fallback.SendAsync(message, cancellationToken);
            return;
        }

        // Een mailing blijft wachten op de mailbox: de outbox biedt hem later opnieuw aan, en bulkmail hoort niet alsnog
        // uit de ACS-pot van honderd per uur te komen die het aanmeldformulier deelt.
        throw new HttpRequestException($"Versturen via de mailbox mislukte ({Describe(status)}).", null, status);
    }

    /// <summary>Eén poging. Geeft de statuscode terug, of <c>null</c> als er binnen de tijd geen antwoord kwam.</summary>
    private async Task<(HttpStatusCode? Status, string? Detail)> TrySendAsync(
        string url, byte[] body, string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attempt.CancelAfter(AttemptTimeout);
        try
        {
            using var response = await http.SendAsync(request, attempt.Token);
            if (response.IsSuccessStatusCode)
            {
                return (response.StatusCode, null);
            }

            // Het antwoord van Graph bevat de foutcode en het request-id dat Microsoft bij een melding vraagt, en geen
            // persoonsgegevens (docs/06 §9). Zonder dit wisten we op 12-11-2026 alleen de statuscode.
            var detail = await response.Content.ReadAsStringAsync(attempt.Token);
            return (response.StatusCode, detail.Length > 500 ? detail[..500] : detail);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "geen antwoord binnen de tijd");
        }
        catch (HttpRequestException ex)
        {
            return (null, ex.Message);
        }
    }

    private static string Describe(HttpStatusCode? status) => status is null ? "geen antwoord" : $"{(int)status.Value}";

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Poging {Attempt} om via de mailbox te versturen mislukte ({Status}): {Detail}")]
    private partial void LogMailboxAttemptFailed(int attempt, int status, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "De mailbox kwam er niet doorheen ({Status}); dit bericht kan niet wachten en gaat via ACS.")]
    private partial void LogMailboxFallback(int status);
}

/// <summary>
/// Een e-mail als MIME-tekst (RFC 5322/2045): platte tekst en HTML als alternatieven, bijlagen erbij, kopteksten met
/// UTF-8 (RFC 2047) en de afmeldkoppen. Message-ID en de afzendernaam vult Exchange zelf in. Een kopregel met CR, LF of NUL
/// wordt geweigerd: daarmee zou een ingevuld adres extra kopregels (bijv. Bcc) kunnen toevoegen.
/// </summary>
public static class MimeMessage
{
    public static string Build(string from, EmailMessage message, DateTimeOffset date)
    {
        var mime = new StringBuilder();
        void Header(string name, string value) => mime.Append(name).Append(": ").Append(HeaderValue(value)).Append("\r\n");

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
                var name = QuotedString(EncodeWord(file.Name));
                Part(mime, mixed, $"{file.ContentType}; name=\"{name}\"", file.Content, $"attachment; filename=\"{name}\"");
            }

            mime.Append("--").Append(mixed).Append("--\r\n");
        }

        return mime.ToString();
    }

    private static void Part(StringBuilder mime, string boundary, string contentType, byte[] content, string? disposition = null)
    {
        mime.Append("--").Append(boundary).Append("\r\n")
            .Append("Content-Type: ").Append(HeaderValue(contentType)).Append("\r\n")
            .Append("Content-Transfer-Encoding: base64\r\n");
        if (disposition is not null)
        {
            mime.Append("Content-Disposition: ").Append(HeaderValue(disposition)).Append("\r\n");
        }

        mime.Append("\r\n").Append(Convert.ToBase64String(content, Base64FormattingOptions.InsertLineBreaks)).Append("\r\n");
    }

    /// <summary>Een kopregel mag geen regeleinde of NUL bevatten (header injection); dan liever niet versturen.</summary>
    private static string HeaderValue(string value) =>
        value.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0
            ? throw new ArgumentException("Een kopregel van de e-mail bevat een regeleinde of NUL-teken.", nameof(value))
            : value;

    /// <summary>Inhoud van een quoted-string (RFC 5322 3.2.4): <c>\</c> en <c>"</c> krijgen een backslash ervoor.</summary>
    private static string QuotedString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    /// <summary>Alleen ASCII blijft leesbaar; anders een UTF-8 encoded-word (RFC 2047).</summary>
    private static string EncodeWord(string value) =>
        value.All(c => c is >= ' ' and <= '~') ? value : $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(value))}?=";
}
