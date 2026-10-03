using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Content.Import;
using Drammers.Modules.Content.Photos;

namespace Drammers.UnitTests.Content;

/// <summary>Fase 21e: WordPress-HTML naar Markdown en het uitlezen van de oude pagina's (nagemaakte HTML, geen echte namen).</summary>
public class WordPressImportTests
{
    [Fact]
    public void Html_wordt_markdown_met_koppen_vet_links_lijsten_en_tabellen()
    {
        const string html = """
            <h2 class="wp-block-heading">Uitslag &amp; prijzen</h2>
            <p>De <strong>wagen</strong> van <em>De Testers</em> won.&nbsp;Zie <a href="https://example.org/reglement">het reglement</a>.<br>Volgende regel.</p>
            <ul><li>Eerste</li><li><strong>Tweede</strong></li></ul>
            <table><tr><th>Prijs</th><th>Naam</th></tr><tr><td>1</td><td>De Testers | groep</td></tr></table>
            <script>alert(1)</script>
            """;

        var md = HtmlToMarkdown.Convert(html).Markdown;

        Assert.Contains("## Uitslag & prijzen", md);
        Assert.Contains("De **wagen** van *De Testers* won. Zie [het reglement](https://example.org/reglement).\nVolgende regel.", md);
        Assert.Contains("- Eerste\n- **Tweede**", md);
        Assert.Contains("| Prijs | Naam |\n| --- | --- |\n| 1 | De Testers / groep |", md);
        Assert.DoesNotContain("alert", md);
        Assert.Contains("<table>", MarkdownRenderer.ToSafeHtml(md));
    }

    [Fact]
    public void Afbeeldingen_en_galerijen_komen_apart_als_origineel_adres()
    {
        const string html = """
            <p>Tekst bij de foto's.</p>
            <figure class="wp-block-gallery"><figure><img src="https://wp.test/uploads/2026/01/a-1024x768.jpg"></figure>
            <figure><img data-src="https://wp.test/uploads/2026/01/b.jpg" src="data:image/gif;base64,R0lG"></figure></figure>
            <p><img src="https://wp.test/uploads/2025/10/vrdr-logo-300x290.png"></p>
            """;

        var converted = HtmlToMarkdown.Convert(html);

        Assert.Equal("Tekst bij de foto's.", converted.Markdown);
        Assert.Equal(["https://wp.test/uploads/2026/01/a.jpg", "https://wp.test/uploads/2026/01/b.jpg"], converted.Images);
    }

    [Fact]
    public void FooGallery_levert_de_originelen_uit_de_links_en_geen_plaatshouders()
    {
        const string html = """
            <style type="text/css">#foogallery-gallery-1 .fg-image { width: 270px; }</style>
            <div class="foogallery" id="foogallery-gallery-1">
              <div class="fg-item"><figure class="fg-item-inner"><a href="https://wp.test/uploads/2026/02/img-1.jpg" class="fg-thumb"><span class="fg-image-wrap">
                <img class="skip-lazy fg-image" data-src-fg="https://wp.test/uploads/cache/2026/02/img-1/123.jpg" src="data:image/svg+xml,%3Csvg%3E"></span></a></figure></div>
              <div class="fg-item"><figure class="fg-item-inner"><a href="https://wp.test/uploads/2026/02/img-2.jpg" class="fg-thumb">
                <img class="fg-image" data-src-fg="https://wp.test/uploads/cache/2026/02/img-2/456.jpg" src="data:image/svg+xml,%3Csvg%3E"></a></figure></div>
            </div>
            <p><a href="https://wp.test/pronkzitting/"><img src="https://wp.test/uploads/2026/02/knop-300x200.jpg"></a></p>
            """;

        var converted = HtmlToMarkdown.Convert(html);

        Assert.Equal("", converted.Markdown);
        Assert.Equal(["https://wp.test/uploads/2026/02/img-1.jpg", "https://wp.test/uploads/2026/02/img-2.jpg", "https://wp.test/uploads/2026/02/knop.jpg"],
            converted.Images);
    }

    [Fact]
    public void Submenu_van_de_oude_site_in_menuvolgorde()
    {
        const string html = """
            <nav><ul>
              <li><div class="dropdown-container group"><a href="https://wp.test/over-ons/"><span>Vereniging</span></a>
                <div class="dropdown"><a href="https://wp.test/ontstaan/">Ontstaan</a></div></div></li>
              <li><div class="dropdown-container group"><a href="https://wp.test/carnaval/"><span>Carnaval</span><svg></svg></a>
                <div class="dropdown"><div class="py-1"><a href="https://wp.test/pronkzitting-2026/">Pronkzitting 2026</a>
                <a href="https://wp.test/optoch-2026/">Optocht 2026</a><a href="https://wp.test/tickets/">Tickets</a></div></div></div></li>
            </ul></nav>
            """;

        Assert.Equal(["https://wp.test/pronkzitting-2026/", "https://wp.test/optoch-2026/", "https://wp.test/tickets/"], WordPressSource.ParseSubmenu(html, "Carnaval"));
        Assert.Empty(WordPressSource.ParseSubmenu(html, "Bestaat niet"));
    }

    [Fact]
    public void Prinsen_uit_het_archief_met_prinsennaam_naam_jaar_en_motto()
    {
        const string html = """
            <div class="member-profile">
              <img class="member-profile-image" src="https://wp.test/uploads/2025/12/prins-358x360.jpg" alt="Piet Test"/>
              <h3 class="member-name prins-title">Prins Piet I</h3>
              <h4 class="member-name prins-name">Piet Test</h4>
              <p class="member-function">2025</p>
              <div class="member-committee">"Alaaf voor &#039;t hele dorp"</div>
            </div>
            <div class="member-profile">
              <img class="member-profile-image" src="https://wp.test/uploads/k.jpg" alt="Karin"/>
              <h3 class="member-name">Karin Voorbeeld</h3>
              <p class="member-function">Voorzitter</p>
            </div>
            """;

        var people = WordPressSource.ParsePeople(html);

        Assert.Equal(new WpPerson("Piet Test", "Prins Piet I", "2025", "Alaaf voor 't hele dorp", "https://wp.test/uploads/2025/12/prins-358x360.jpg"), people[0]);
        Assert.Equal(new WpPerson("Karin Voorbeeld", null, "Voorzitter", null, "https://wp.test/uploads/k.jpg"), people[1]);
    }

    [Fact]
    public void Onderscheiding_met_soort_jaar_foto_en_tekst()
    {
        const string html = """
            <article class="single-award-container">
              <header><h1>Jan Voorbeeld</h1><h2>&#039;t drammertje <i>(2024)</i></h2></header>
              <div class="award-winner-image"><img src="https://wp.test/uploads/jan-1024x768.jpg"></div>
              <div class="award-content"><p>Al <strong>twintig</strong> jaar actief.</p></div>
            </article>
            """;

        var award = WordPressSource.ParseAward(html);

        Assert.Equal("Jan Voorbeeld", award.Recipient);
        Assert.Equal("'t drammertje", award.TypeText);
        Assert.Equal(2024, award.Year);
        Assert.Equal("https://wp.test/uploads/jan-1024x768.jpg", award.ImageUrl);
        Assert.Equal("Al **twintig** jaar actief.", HtmlToMarkdown.Convert(award.ContentHtml).Markdown);
    }

    [Theory]
    [InlineData("Pronkzitting 2026", PhotoCategory.Pronkzitting)]
    [InlineData("Jeugdpronkzitting 2025", PhotoCategory.Youth)]
    [InlineData("Optoch 2026", PhotoCategory.Parade)]
    [InlineData("Kindercarnaval 2026", PhotoCategory.Youth)]
    [InlineData("Prinsenreceptie 2026", PhotoCategory.Carnival)]
    [InlineData("Dansgarde festival", PhotoCategory.Dansgarde)]
    [InlineData("Drammerskrant 2026", PhotoCategory.Other)]
    public void Soort_galerij_uit_de_titel(string title, PhotoCategory expected) =>
        Assert.Equal(expected, WebsiteImporter.CategoryOf(title));

    [Theory]
    [InlineData("https://vrolijkedrammers.nl/Optoch-2026/", "/optoch-2026")]
    [InlineData("/prins/piet-test", "/prins/piet-test")]
    [InlineData("https://vrolijkedrammers.nl/", "/")]
    [InlineData("/nieuws?pagina=2", "/nieuws")]
    public void Oude_adressen_worden_genormaliseerd(string input, string expected) =>
        Assert.Equal(expected, WebsiteImporter.NormalizePath(input));
}
