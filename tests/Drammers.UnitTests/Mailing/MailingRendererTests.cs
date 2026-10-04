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
        Assert.Contains("background:#D4000F", mail.Html);
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
    public void Afsluiting_staat_onderaan_samen_met_de_voettekst()
    {
        var mail = Render([new("closing", "Met carnavaleske groet,\n{naam}"), new("text", "Tot dan!")], new("Piet", "Piet Lid"));

        var text = mail.PlainText;
        Assert.True(text.IndexOf("Tot dan!", StringComparison.Ordinal) < text.IndexOf("Met carnavaleske groet,", StringComparison.Ordinal));
        Assert.True(text.IndexOf("Piet Lid", StringComparison.Ordinal) < text.IndexOf("Carnavalsvereniging De Vrolijke Drammers Loil", StringComparison.Ordinal));
        var html = mail.Html;
        Assert.True(html.IndexOf("Met carnavaleske groet,", StringComparison.Ordinal) < html.IndexOf("Afmelden", StringComparison.Ordinal));

        // Zonder blok Afsluiting de standaardgroet.
        Assert.Contains("Groeten,\nDe Vrolijke Drammers", Render([new("text", "Hoi")], new("Piet", null)).PlainText);
        Assert.Throws<DomainException>(() => MailingRenderer.Validate([new("closing", "a"), new("closing", "b")]));
    }

    [Fact]
    public void Huisstijl_blauwe_kop_ronde_blokken_en_pilknop()
    {
        var html = Render([new("button", Label: "Bestel", Url: "https://dvd.test"), new("highlight", "6 feb")], new("Piet", null)).Html;
        Assert.Contains("background:#087BC1;border-radius:15px 15px 28px 28px", html);
        Assert.Contains("border-radius:999px", html);
        Assert.Contains("border-radius:16px", html);
        Assert.Contains("De Vrolijke Drammers</div>", html);
    }

    [Fact]
    public void Koppen_in_drammers_rood_en_poppins_van_de_eigen_website()
    {
        var html = MailingRenderer.Render("Onderwerp", null, [new("heading", "Kop"), new("text", "## Tussenkop")], new("Piet", null), null, _ => null, null,
            "https://dvd.test/").Html;
        Assert.Contains("font-size:26px;line-height:33px;color:#ED0012", html);
        Assert.Contains("font-size:20px;line-height:27px;color:#ED0012", html);
        Assert.Contains("@font-face{font-family:Poppins;font-weight:700;src:url('https://dvd.test/_content/Drammers.Website/fonts/poppins-latin-700-normal.woff2')", html);
        Assert.DoesNotContain("fonts.googleapis.com", html);
    }

    [Fact]
    public void Lege_mailing_mag_niet() =>
        Assert.Throws<DomainException>(() => MailingRenderer.Validate([]));
}
