using System.Net;
using System.Text;
using Azure.Core;
using Drammers.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.UnitTests.Email;

/// <summary>Mailings via de mailbox in Microsoft 365 (Graph sendMail met MIME), met ACS als terugval.</summary>
public class MailboxEmailSenderTests
{
    private static readonly EmailOptions Options = new()
    {
        CustomSenderDomain = "vrolijkedrammers.nl",
        MailboxSenders = "voorzitter, secretaris",
        GraphBaseUrl = "https://graph.test",
    };

    private static EmailMessage Mailing(string from = "voorzitter", bool viaMailbox = true) => new(
        "piet@example.com", "Ondernemersbrief 2027 – €35", "Beste Piet,", "<p>Beste Piet,</p>", $"{from}@vrolijkedrammers.nl", from,
        UnsubscribeUrl: "https://dvd.test/afmelden/een-klik?t=abc", ViaMailbox: viaMailbox);

    [Fact]
    public void Alleen_mailings_van_ingestelde_afzenders_via_de_mailbox()
    {
        Assert.Equal("voorzitter@vrolijkedrammers.nl", MailboxEmailSender.Mailbox(Mailing(), Options));
        Assert.Equal("secretaris@vrolijkedrammers.nl", MailboxEmailSender.Mailbox(Mailing("Secretaris"), Options));
        Assert.Null(MailboxEmailSender.Mailbox(Mailing("penningmeester"), Options));
        Assert.Null(MailboxEmailSender.Mailbox(Mailing(viaMailbox: false), Options));
        Assert.Null(MailboxEmailSender.Mailbox(Mailing(), new EmailOptions { MailboxSenders = "voorzitter" }));
        // M1: net als bij ACS bepaalt het antwoordadres nooit de mailbox, alleen een expliciete afzender.
        Assert.Null(MailboxEmailSender.Mailbox(Mailing() with { From = null }, Options));
    }

    [Theory]
    [InlineData("piet@example.com\r\nBcc: iedereen@example.com", null)]
    [InlineData("piet@example.com", "jan@example.com\nBcc: iedereen@example.com")]
    [InlineData("piet@example.com\0", null)]
    public void Kopregels_met_regeleinde_of_nul_worden_geweigerd(string to, string? replyTo)
    {
        var message = Mailing() with { To = to, ReplyTo = replyTo ?? "voorzitter@vrolijkedrammers.nl" };
        Assert.Throws<ArgumentException>(() => MimeMessage.Build("voorzitter@vrolijkedrammers.nl", message, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Bijlagenaam_met_aanhalingsteken_of_backslash_wordt_ge_escaped_en_regeleinde_in_onderwerp_gecodeerd()
    {
        var message = Mailing() with
        {
            Subject = "Hallo\r\nBcc: iedereen@example.com",
            Attachments = [new EmailFile("fac\"tuur\\1.pdf", "application/pdf", [1])],
        };
        var mime = MimeMessage.Build("voorzitter@vrolijkedrammers.nl", message, DateTimeOffset.UnixEpoch);

        Assert.Contains("Content-Disposition: attachment; filename=\"fac\\\"tuur\\\\1.pdf\"\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf; name=\"fac\\\"tuur\\\\1.pdf\"\r\n", mime, StringComparison.Ordinal);
        Assert.DoesNotContain("\r\nBcc:", mime, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => MimeMessage.Build("voorzitter@vrolijkedrammers.nl",
            Mailing() with { Attachments = [new EmailFile("a.pdf", "application/pdf\r\nX-Extra: 1", [1])] }, DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void Mime_met_afmeldkoppen_utf8_onderwerp_en_bijlage()
    {
        var message = Mailing() with { Attachments = [new EmailFile("factuur.pdf", "application/pdf", [1, 2, 3])] };
        var mime = MimeMessage.Build("voorzitter@vrolijkedrammers.nl", message, new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        Assert.Contains("From: voorzitter@vrolijkedrammers.nl\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("To: piet@example.com\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("Reply-To: voorzitter@vrolijkedrammers.nl\r\n", mime, StringComparison.Ordinal);
        Assert.Contains($"Subject: =?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes("Ondernemersbrief 2027 – €35"))}?=\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("List-Unsubscribe: <https://dvd.test/afmelden/een-klik?t=abc>\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("List-Unsubscribe-Post: List-Unsubscribe=One-Click\r\n", mime, StringComparison.Ordinal);
        Assert.Contains("multipart/mixed", mime, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(Encoding.UTF8.GetBytes("<p>Beste Piet,</p>")), mime, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"factuur.pdf\"", mime, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versturen_via_Graph_als_base64_mime()
    {
        var (sender, handler, fallback) = Create(HttpStatusCode.Accepted);
        await sender.SendAsync(Mailing(), CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://graph.test/v1.0/users/voorzitter%40vrolijkedrammers.nl/sendMail", request.Url);
        Assert.Equal("Bearer token", request.Authorization);
        Assert.Contains("List-Unsubscribe:", Encoding.UTF8.GetString(Convert.FromBase64String(request.Body)), StringComparison.Ordinal);
        Assert.Empty(fallback.Sent);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Zonder_rechten_op_de_mailbox_via_ACS(HttpStatusCode status)
    {
        var (sender, _, fallback) = Create(status);
        await sender.SendAsync(Mailing(), CancellationToken.None);
        Assert.Single(fallback.Sent);
    }

    [Fact]
    public async Task Andere_fouten_gooien_zodat_de_outbox_het_opnieuw_probeert_en_gewone_mail_gaat_via_ACS()
    {
        var (sender, handler, fallback) = Create(HttpStatusCode.TooManyRequests);
        await Assert.ThrowsAsync<HttpRequestException>(() => sender.SendAsync(Mailing(), CancellationToken.None));
        Assert.Empty(fallback.Sent);

        await sender.SendAsync(Mailing(viaMailbox: false), CancellationToken.None);
        Assert.Single(fallback.Sent);
        Assert.Single(handler.Requests);
    }

    private static (MailboxEmailSender Sender, StubHandler Handler, Recorder Fallback) Create(HttpStatusCode status)
    {
        var handler = new StubHandler(status);
        var fallback = new Recorder();
        var sender = new MailboxEmailSender(fallback, new HttpClient(handler), new StaticToken(), Microsoft.Extensions.Options.Options.Create(Options),
            NullLogger<MailboxEmailSender>.Instance);
        return (sender, handler, fallback);
    }

    private sealed class Recorder : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class StaticToken : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<(string Url, string? Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(status);
        }
    }
}
