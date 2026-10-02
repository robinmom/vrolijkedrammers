using Drammers.Infrastructure.Contact;
using Drammers.Infrastructure.Email;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.Website.Content;
using Microsoft.Extensions.Options;

namespace Drammers.UnitTests.Content;

/// <summary>Fase 21i: geen e-mailadressen van de vereniging op de website, wel links naar het contactformulier.</summary>
public class ContactLinksTests
{
    private static readonly ContactLinks Links = new(Options.Create(new ContactOptions()));

    [Fact]
    public void Mailto_links_worden_links_naar_het_formulier_met_de_juiste_ontvanger()
    {
        var html = """<p>Mail naar <a href="mailto:optocht@vrolijkedrammers.nl?subject=Vraag" class="x">optocht@vrolijkedrammers.nl</a>.</p>""";
        Assert.Equal("""<p>Mail naar <a href="/contact?aan=optocht#formulier">het contactformulier</a>.</p>""", Links.Rewrite(html));
    }

    [Fact]
    public void Losse_adressen_en_onbekende_ontvangers()
    {
        var html = "<p>Vragen? secretaris@vrolijkedrammers.nl of <a href=\"mailto:jeugd@vrolijkedrammers.nl\">de jeugdcommissie</a>.</p>";
        Assert.Equal(
            "<p>Vragen? <a href=\"/contact?aan=secretariaat#formulier\">het contactformulier</a> of <a href=\"/contact#formulier\">de jeugdcommissie</a>.</p>",
            Links.Rewrite(html));
    }

    [Fact]
    public void Andere_domeinen_en_attributen_blijven_staan()
    {
        var html = "<p><img src=\"/media/a.jpg\" alt=\"x\"> Mail jan@example.com</p>";
        Assert.Equal(html, Links.Rewrite(html));
        Assert.Null(Links.Rewrite(null));
    }

    private sealed class Recorder : IEmailSender, IAuditLogger, ITurnstileVerifier
    {
        public List<EmailMessage> Sent { get; } = [];

        public List<string> Audit { get; } = [];

        public bool TurnstileResult { get; set; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Audit.Add(entry.Action);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken cancellationToken) => Task.FromResult(TurnstileResult && token == "ok");
    }

    private static (ContactForm Form, Recorder Recorder) Form(bool turnstile)
    {
        var recorder = new Recorder { TurnstileResult = true };
        var options = Options.Create(turnstile ? new TurnstileOptions { SiteKey = "site", SecretKey = "secret" } : new TurnstileOptions());
        return (new ContactForm(Options.Create(new ContactOptions()), options, recorder, recorder, recorder), recorder);
    }

    private static ContactMessageInput Input(string? website = null, int? elapsed = 8000, string? token = null) =>
        new("Penningmeester", "Anna", "anna@example.com", null, "Kan ik munten kopen?", website, elapsed, token);

    [Fact]
    public async Task Bericht_naar_de_ontvanger_met_reply_to_en_spam_stil_weggooien()
    {
        var (form, recorder) = Form(turnstile: false);
        Assert.True(await form.SendAsync(Input(), "1.2.3.4", default));
        var mail = Assert.Single(recorder.Sent);
        Assert.Equal(("penningmeester@vrolijkedrammers.nl", "anna@example.com"), (mail.To, mail.ReplyTo));
        Assert.Contains("Kan ik munten kopen?", mail.PlainText);

        Assert.False(await form.SendAsync(Input(website: "http://spam"), null, default));
        Assert.False(await form.SendAsync(Input(elapsed: 800), null, default));
        Assert.False(await form.SendAsync(Input(elapsed: null), null, default));
        Assert.Single(recorder.Sent);
        Assert.Equal(["contact.message-sent", "contact.message-dropped", "contact.message-dropped", "contact.message-dropped"], recorder.Audit);

        await Assert.ThrowsAsync<DomainException>(() => form.SendAsync(Input() with { Recipient = "voorzitter" }, null, default));
    }

    [Fact]
    public async Task Met_turnstile_is_een_geldig_token_verplicht()
    {
        var (form, recorder) = Form(turnstile: true);
        await Assert.ThrowsAsync<DomainException>(() => form.SendAsync(Input(), null, default));
        Assert.True(await form.SendAsync(Input(token: "ok"), null, default));
        Assert.Single(recorder.Sent);
    }
}
