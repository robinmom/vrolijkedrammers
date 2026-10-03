using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.IntegrationTests;

/// <summary>Fase 21c: de openbare website (Razor Pages) toont alleen openbare inhoud; afbeeldingen via een eigen media-adres.</summary>
[Collection(SqlServerCollection.Name)]
public class WebsitePagesTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private HttpClient _guest = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _guest = _api.CreateClient();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> NewsAsync(string title, string visibility = "Public", bool website = true)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", new
        {
            title,
            summary = "Samenvatting van " + title,
            body = "Tekst in de app.",
            category = (string?)null,
            expireAt = (DateTime?)null,
            publication = new { visibility, audienceRoles = visibility == "Restricted" ? new[] { "bestuur" } : Array.Empty<string>(), status = "Published" },
            showOnWebsite = website,
            websiteBody = website ? "Het **hele** verhaal." : null,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<string> HtmlAsync(string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await _guest.GetAsync(path);
        Assert.Equal(expected, response.StatusCode);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType?.MediaType);
        return await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task Homepage_toont_hero_en_alleen_nieuws_dat_op_de_website_mag()
    {
        await NewsAsync("Carnaval komt eraan");
        await NewsAsync("Alleen in de app", website: false);
        await _bestuur.PostAsJsonAsync("/api/v1/admin/news", new
        {
            title = "Geheim bestuursnieuws",
            summary = (string?)null,
            body = "x",
            category = (string?)null,
            expireAt = (DateTime?)null,
            publication = new { visibility = "Restricted", audienceRoles = new[] { "bestuur" }, status = "Published" },
        });

        var response = await _guest.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Alaaf! Het feest komt eraan.", html);
        Assert.Contains("Carnaval komt eraan", html);
        Assert.DoesNotContain("Alleen in de app", html);
        Assert.DoesNotContain("Geheim bestuursnieuws", html);
        Assert.Contains("powered by", html);
        Assert.Contains("default-src 'self'", response.Headers.GetValues("Content-Security-Policy").Single());

        var article = await HtmlAsync("/nieuws/carnaval-komt-eraan");
        Assert.Contains("<strong>hele</strong>", article);
        await HtmlAsync("/nieuws/alleen-in-de-app", HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Nieuws_van_het_actieve_carnavalsjaar_en_oudere_jaren_onder_een_knop()
    {
        await NewsAsync("Nieuw seizoen");
        var old = await NewsAsync("Optocht van vorig jaar");
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            await db.News.Where(n => n.Id == old).ExecuteUpdateAsync(u => u.SetProperty(n => n.PublishAt, new DateTime(2025, 12, 1, 10, 0, 0, DateTimeKind.Utc)));
        }

        // Actief jaar 2026/2027 (seed); december 2025 hoort bij 2025-2026. Er staan altijd minstens 5 berichten: zolang het
        // actieve jaar er minder heeft, vult het nieuwste nieuws van vorig jaar aan.
        var current = await HtmlAsync("/nieuws");
        Assert.Contains("Nieuw seizoen", current);
        Assert.Contains("Optocht van vorig jaar", current);
        Assert.Contains("href=\"/nieuws?seizoen=2025-2026\"", current);
        Assert.Contains("Optocht van vorig jaar", await HtmlAsync("/"));
        var filled = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/news");
        Assert.Equal(["Nieuw seizoen", "Optocht van vorig jaar"], filled.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));

        // Vijf berichten in het actieve jaar: het bericht van vorig jaar valt weg (alleen nog onder de knop 2025-2026).
        foreach (var n in new[] { "Twee", "Drie", "Vier", "Vijf" })
        {
            await NewsAsync($"Nieuws {n}");
        }

        current = await HtmlAsync("/nieuws");
        Assert.DoesNotContain("Optocht van vorig jaar", current);
        Assert.DoesNotContain("Optocht van vorig jaar", await HtmlAsync("/"));

        var archive = await HtmlAsync("/nieuws?seizoen=2025-2026");
        Assert.Contains("Optocht van vorig jaar", archive);
        Assert.DoesNotContain("Nieuw seizoen", archive);
        Assert.Contains("Actueel (2026-2027)", archive);
        await HtmlAsync("/nieuws?seizoen=2025-2027", HttpStatusCode.NotFound);

        // De app: zelfde verdeling via de API.
        var news = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/news");
        Assert.Equal(5, news.GetProperty("totalCount").GetInt32());
        Assert.DoesNotContain("Optocht van vorig jaar", news.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        var seasons = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/news/seasons");
        Assert.Equal("2026-2027", seasons.GetProperty("current").GetProperty("slug").GetString());
        Assert.Equal(["2025-2026"], seasons.GetProperty("archive").EnumerateArray().Select(i => i.GetProperty("slug").GetString()));
        var older = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/news?season=2025-2026");
        Assert.Equal(["Optocht van vorig jaar"], older.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync("/api/v1/news?season=onzin")).StatusCode);
    }

    [Fact]
    public async Task Fotoalbums_per_carnavalsjaar()
    {
        var album = await AlbumAsync("Pronkzitting 2027", "Public");
        await UploadPhotoAsync(album);
        var old = await AlbumAsync("Pronkzitting 2024", "Public");
        await UploadPhotoAsync(old);
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            await db.PhotoAlbums.Where(a => a.Id == old).ExecuteUpdateAsync(u => u.SetProperty(a => a.AlbumDate, new DateOnly(2024, 1, 20)));
        }

        var current = await HtmlAsync("/fotos?soort=pronkzitting");
        Assert.Contains("Pronkzitting 2027", current);
        Assert.DoesNotContain("Pronkzitting 2024", current);
        Assert.Contains("href=\"/fotos?soort=pronkzitting&amp;seizoen=2023-2024\"", current);
        Assert.Contains("Pronkzitting 2024", await HtmlAsync("/fotos?seizoen=2023-2024"));

        var seasons = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/photo-albums/seasons");
        Assert.Equal(["2023-2024"], seasons.GetProperty("archive").EnumerateArray().Select(i => i.GetProperty("slug").GetString()));
        var older = await _guest.GetFromJsonAsync<JsonElement>("/api/v1/photo-albums?season=2023-2024");
        Assert.Equal(["Pronkzitting 2024"], older.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()));
    }

    [Theory]
    [InlineData("/lid-worden/", "Word ook een Drammer!", "js/forms/lid-worden.js")]
    [InlineData("/lid-worden", "Aanmelden als lid", "js/forms/lid-worden.js")]
    [InlineData("/optocht-inschrijven/?status=abc", "Inloggen en inschrijven", "js/login.js")]
    [InlineData("/aanrijtijden/", "Aanrijtijden optocht", "js/forms/aanrijtijden.js")]
    [InlineData("/kaarten/", "Te koop", "js/forms/kaarten.js")]
    [InlineData("/kaarten/bestelling/?id=abc&t=def", "Je bestelling", "js/forms/kaarten-bestelling.js")]
    public async Task Losse_onderdelen_zijn_pagina_s_van_de_website(string path, string text, string script)
    {
        // Fase 21d: lid worden, optocht, aanrijtijden en kaarten met kop, menu en voet van de website, op hetzelfde adres.
        var response = await _guest.GetAsync(path);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(text, html);
        Assert.Contains($"/_content/Drammers.Website/{script}", html);
        Assert.Contains("class=\"site-header\"", html);
        Assert.Contains("powered by", html);
        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("connect-src 'self' https://*.ciamlogin.com", csp);
        Assert.Contains("frame-ancestors 'none'", csp);
        Assert.DoesNotContain("unsafe-inline", csp);
        Assert.Equal(path.StartsWith("/kaarten/bestelling", StringComparison.Ordinal), html.Contains("noindex", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Jeugdprinsen_pas_zichtbaar_als_het_bestuur_de_pagina_aanzet()
    {
        await _bestuur.PostAsJsonAsync("/api/v1/admin/website/princes",
            new { kind = "YouthPrince", year = 2025, princeName = "Jeugdprinses Eva I", name = "Eva", motto = "Alaaf!", photo = (string?)null });
        await HtmlAsync("/jeugdprinsen", HttpStatusCode.NotFound);
        Assert.DoesNotContain("href=\"/jeugdprinsen\"", await HtmlAsync("/prinsengalerie"));

        var settings = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/website/settings");
        var update = new Dictionary<string, object?>
        {
            ["heroTitle"] = settings.GetProperty("heroTitle").GetString(),
            ["showYouthPrinces"] = true,
        };
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/website/settings", update)).StatusCode);

        var html = await HtmlAsync("/jeugdprinsen");
        Assert.Contains("Jeugdprinses Eva I", html);
        Assert.Contains("href=\"/jeugdprinsen\"", html);
    }

    [Fact]
    public async Task Prins_aanklikken_toont_jaar_naam_en_motto()
    {
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/princes",
            new { kind = "Prince", year = 2025, princeName = "Prins Ronnie I", name = "Ronnie Loeters", motto = "Mee lache, mee zinge!", photo = (string?)null });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.DoesNotContain("Mee lache", await HtmlAsync("/prinsengalerie"));
        var html = await HtmlAsync($"/prinsengalerie?prins={id:N}");
        Assert.Contains("role=\"dialog\"", html);
        Assert.Contains("Carnaval 2025", html);
        Assert.Contains("Mee lache, mee zinge!", html);
    }

    [Fact]
    public async Task Media_alleen_voor_openbare_fotos_en_te_cachen()
    {
        var album = await AlbumAsync("Pronkzitting 2027", "Public");
        var photo = await UploadPhotoAsync(album);
        var members = await AlbumAsync("Alleen voor leden", "Members");
        var memberPhoto = await UploadPhotoAsync(members);

        var html = await HtmlAsync($"/fotos/{album:N}");
        var src = System.Text.RegularExpressions.Regex.Match(html, "src=\"(/media/photo-thumb/[^\"]+)\"").Groups[1].Value.Replace("&amp;", "&");
        Assert.NotEmpty(src);

        var image = await _guest.GetAsync(src);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/jpeg", image.Content.Headers.ContentType?.MediaType);
        Assert.Contains("public", image.Headers.CacheControl!.ToString());
        using (var again = new HttpRequestMessage(HttpMethod.Get, src))
        {
            again.Headers.IfNoneMatch.Add(image.Headers.ETag!);
            Assert.Equal(HttpStatusCode.NotModified, (await _guest.SendAsync(again)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/media/photo/{memberPhoto:N}")).StatusCode);
        await HtmlAsync($"/fotos/{members:N}", HttpStatusCode.NotFound);

        await _bestuur.PostAsJsonAsync($"/api/v1/admin/photo-albums/{album}/photos/bulk", new { photoIds = new[] { photo }, action = "Hide" });
        Assert.Equal(HttpStatusCode.NotFound, (await _guest.GetAsync($"/media/photo/{photo:N}")).StatusCode);
    }

    [Fact]
    public async Task Onbekend_adres_geeft_een_nette_404_en_de_api_blijft_problemdetails_geven()
    {
        var html = await HtmlAsync("/deze/pagina/bestaat/niet", HttpStatusCode.NotFound);
        Assert.Contains("Pagina niet gevonden", html);
        Assert.Contains("noindex", html);

        var api = await _guest.GetAsync("/api/v1/bestaat-niet");
        Assert.Equal(HttpStatusCode.NotFound, api.StatusCode);
        Assert.Equal("application/problem+json", api.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Vaste_pagina_en_sitemap()
    {
        await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages",
            new { slug = "loillands", title = "Loillands", intro = "Festival", body = "## Twee jaarlijks\nIn september.", image = (string?)null, isPublished = true, sortOrder = 40 });
        await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages",
            new { slug = "concept", title = "Nog niet af", intro = (string?)null, body = "x", image = (string?)null, isPublished = false, sortOrder = 50 });
        await NewsAsync("Loillands 2027");

        Assert.Contains("<h2>Twee jaarlijks</h2>", await HtmlAsync("/loillands"));
        await HtmlAsync("/concept", HttpStatusCode.NotFound);

        var sitemap = await _guest.GetStringAsync("/sitemap.xml");
        Assert.Contains("/loillands</loc>", sitemap);
        Assert.Contains("/nieuws/loillands-2027</loc>", sitemap);
        Assert.DoesNotContain("/concept</loc>", sitemap);
    }

    [Fact]
    public async Task Menus_Vereniging_en_Carnaval_uit_de_paginas_en_een_album_onder_de_pagina()
    {
        var album = await AlbumAsync("Optocht 2026", "Public");
        await UploadPhotoAsync(album);
        var restricted = await AlbumAsync("Alleen voor leden", "Members");
        await UploadPhotoAsync(restricted);
        async Task PageAsync(string slug, string title, string menu, int sortOrder, Guid? photoAlbumId = null, string body = "Tekst.")
        {
            var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages",
                new { slug, title, intro = (string?)null, body, image = (string?)null, isPublished = true, sortOrder, menu, photoAlbumId });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        await PageAsync("optocht-2026", "Optocht 2026", "Carnival", 20, album, body: "");
        await PageAsync("pronkzitting-2026", "Pronkzitting 2026", "Carnival", 10, restricted);
        await PageAsync("loillands", "Loillands", "Association", 70);
        await PageAsync("over-ons", "Over ons", "Association", 0);
        await PageAsync("los", "Losse pagina", "None", 0);

        var home = await HtmlAsync("/");
        var nav = home[home.IndexOf("class=\"main-nav\"", StringComparison.Ordinal)..];
        Assert.Contains(">Carnaval</summary>", nav);
        int At(string href) => nav.IndexOf($"href=\"{href}\"", StringComparison.Ordinal);
        Assert.True(At("/over-ons") < At("/kader") && At("/onderscheidingen") < At("/loillands"), "Vereniging in de verkeerde volgorde");
        Assert.True(At("/pronkzitting-2026") > 0 && At("/pronkzitting-2026") < At("/optocht-2026"), "Carnaval in de verkeerde volgorde");
        Assert.Equal(-1, At("/los"));

        var optocht = await HtmlAsync("/optocht-2026");
        Assert.Contains("aria-current=\"page\">Carnaval</summary>", optocht);
        Assert.Contains("class=\"photo-grid\"", optocht);
        Assert.Contains("1 foto. Tik", optocht);

        // Een album dat niet openbaar is, staat niet onder de pagina.
        Assert.DoesNotContain("class=\"photo-grid\"", await HtmlAsync("/pronkzitting-2026"));

        var wrong = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages",
            new { slug = "fout", title = "Fout album", intro = (string?)null, body = "x", image = (string?)null, isPublished = true, sortOrder = 0, menu = "Carnival", photoAlbumId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, wrong.StatusCode);

        var options = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/albums");
        Assert.Contains(options!, o => o.GetProperty("title").GetString() == "Optocht 2026" && o.GetProperty("photoCount").GetInt32() == 1);
    }

    private async Task<Guid> AlbumAsync(string title, string visibility)
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/photo-albums", new
        {
            title,
            albumDate = "2027-01-08",
            description = (string?)null,
            eventId = (Guid?)null,
            category = "Pronkzitting",
            publication = new { visibility, audienceRoles = Array.Empty<string>(), status = "Published" },
        });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<Guid> UploadPhotoAsync(Guid album)
    {
        var response = await _bestuur.PostAsync($"/api/v1/admin/photo-albums/{album}/photos", ContentTests.Multipart("files", "IMG.jpg", TestImages.JpegWithGps()));
        var id = (await response.Content.ReadFromJsonAsync<List<JsonElement>>())![0].GetProperty("id").GetGuid();
        var processor = new OutboxProcessor(_api.Services.GetRequiredService<IServiceScopeFactory>(), new OutboxSignal(), TimeProvider.System, NullLogger<OutboxProcessor>.Instance);
        while (await processor.ProcessBatchAsync(default) > 0)
        {
        }

        return id;
    }
}
