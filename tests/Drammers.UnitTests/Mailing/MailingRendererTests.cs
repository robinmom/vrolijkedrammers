using Drammers.Infrastructure.Mailings;
using Drammers.SharedKernel.Errors;

namespace Drammers.UnitTests.Mailing;

/// <summary>Fase 27a: blokken naar e-mail in de huisstijl.</summary>
public class MailingRendererTests
{
    private static RenderedMailing Render(IReadOnlyList<MailingBlock> blocks, MailingPerson person, string? unsubscribe = "https://dvd.test/afmelden?t=abc") =>
        MailingRenderer.Render("Hallo {voornaam}", "Voorbeeldregel", blocks, person, "https://dvd.test/logo.png", path => $"https://dvd.test/media/{path}", unsubscribe);

    [Fact]
    public void Namen_worden_ingevuld_en_veilig_in_de_html_gezet()
    {
        var mail = Render([new("heading", "Welkom {naam}"), new("text", "Beste {voornaam},\n\nTot **zaterdag**.")], new("<b>Jan</b>", "Jan & Co"));

        Assert.Equal("Hallo <b>Jan</b>", mail.Subject);
        Assert.Contains("Welkom Jan &amp; Co", mail.Html);
        Assert.Contains("&lt;b&gt;Jan&lt;/b&gt;", mail.Html);
        Assert.DoesNotContain("<b>Jan</b>", mail.Html);
        Assert.Contains("<strong>zaterdag</strong>", mail.Html);
        Assert.Contains("Groeten,<br", Render([new("text", "Groeten,\nDe Vrolijke Drammers")], new("Piet", null)).Html);
        Assert.Contains("Voorbeeldregel", mail.Html);
        Assert.Contains("https://dvd.test/logo.png", mail.Html);
        Assert.Contains("Afmelden", mail.Html);
    }

    [Fact]
    public void Zonder_voornaam_drammer_en_anders_het_eerste_woord_van_de_naam()
    {
        Assert.Equal("Beste Drammer", MailingRenderer.Fill("Beste {voornaam}", new(null, null)));
        Assert.Equal("Beste Gerda", MailingRenderer.Fill("Beste {voornaam}", new(null, "Gerda Gast")));
        Assert.Equal("Beste Jan Willem", MailingRenderer.Fill("Beste {voornaam}", new("Jan Willem", "Jan Willem de Vries")));
    }

    [Fact]
    public void Platte_tekst_met_links_knop_uitgelicht_en_afmeldlink()
    {
        var mail = Render(
            [
                new("text", "Kijk op [de website](https://www.vrolijkedrammers.nl)."),
                new("button", Label: "Bestel kaarten", Url: "https://dvd.test/kaarten"),
                new("highlight", "6 februari", "Datum", Note: "Zaal open 19.00"),
                new("image", "De zaal", Image: "uploads/0123456789abcdef0123456789abcdef.jpg"),
                new("divider"),
            ],
            new("Piet", "Piet Lid"));

        Assert.Contains("Kijk op de website (https://www.vrolijkedrammers.nl).", mail.PlainText);
        Assert.Contains("Bestel kaarten: https://dvd.test/kaarten", mail.PlainText);
        Assert.Contains("Datum: 6 februari\nZaal open 19.00", mail.PlainText);
        Assert.Contains("Afmelden voor nieuwsbrieven en uitnodigingen: https://dvd.test/afmelden?t=abc", mail.PlainText);
        Assert.Contains("https://dvd.test/media/uploads/0123456789abcdef0123456789abcdef.jpg", mail.Html);
        Assert.Contains("background:#ED0012", mail.Html);
    }

    [Theory]
    [InlineData("button", null, "Knop", "http://onveilig.test", null, "https://")]
    [InlineData("image", null, null, null, "elders/foto.jpg", "kies een foto")]
    [InlineData("heading", "", null, null, null, "kop")]
    [InlineData("video", null, null, null, null, "onbekend")]
    public void Ongeldige_blokken_geven_een_begrijpelijke_fout(string type, string? text, string? label, string? url, string? image, string fragment)
    {
        var error = Assert.Throws<DomainException>(() => MailingRenderer.Validate([new MailingBlock(type, text, label, url, image)]));
        Assert.Contains("Blok 1", error.Message);
        Assert.Contains(fragment, error.Message);
    }

    [Fact]
    public void Lege_mailing_mag_niet() =>
        Assert.Throws<DomainException>(() => MailingRenderer.Validate([]));
}
