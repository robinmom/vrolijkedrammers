using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 21a: websitebeheer (hero, pagina's, kader, prinsen, onderscheidingen) en nieuws op de website.</summary>
[Collection(SqlServerCollection.Name)]
public class WebsiteTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<string> UploadAsync(string url)
    {
        var response = await _bestuur.PostAsync(url, ContentTests.Multipart("file", "foto.jpg", TestImages.JpegWithGps()));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.StartsWith("http", body.GetProperty("url").GetString());
        return body.GetProperty("path").GetString()!;
    }

    private static object News(string title, string visibility = "Public", bool website = true, string? image = null, string? websiteBody = null) => new
    {
        title,
        summary = "Korte tekst",
        body = "Tekst in de app.",
        category = (string?)null,
        expireAt = (DateTime?)null,
        publication = new { visibility, audienceRoles = visibility == "Restricted" ? new[] { "bestuur" } : Array.Empty<string>(), status = "Published" },
        showOnWebsite = website,
        websiteBody,
        image,
    };

    [Fact]
    public async Task Nieuws_met_afbeelding_van_voor_het_opslaan_en_een_webadres_uit_de_titel()
    {
        var image = await UploadAsync("/api/v1/admin/news/images");
        Assert.Matches("^uploads/[0-9a-f]{32}\\.jpg$", image);

        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", News("Drammertje 2026 is voor Raymond Raben", image: image, websiteBody: "Lange **tekst**."));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var news = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/news/{id}");
        Assert.True(news.GetProperty("showOnWebsite").GetBoolean());
        Assert.Equal("drammertje-2026-is-voor-raymond-raben", news.GetProperty("slug").GetString());
        Assert.Equal("Lange **tekst**.", news.GetProperty("websiteBody").GetString());
        Assert.Contains(image, news.GetProperty("imageUrl").GetString());

        // Dezelfde titel krijgt een eigen webadres.
        var second = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", News("Drammertje 2026 is voor Raymond Raben"));
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal("drammertje-2026-is-voor-raymond-raben-2",
            (await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/news/{secondId}")).GetProperty("slug").GetString());
    }

    [Fact]
    public async Task Alleen_openbaar_nieuws_mag_op_de_website_en_een_vreemd_afbeeldingspad_wordt_geweigerd()
    {
        var restricted = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", News("Alleen voor het bestuur", visibility: "Restricted"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, restricted.StatusCode);

        var foreign = await _bestuur.PostAsJsonAsync("/api/v1/admin/news", News("Met vreemde afbeelding", image: "events/abc/geheim.jpg"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, foreign.StatusCode);
    }

    [Fact]
    public async Task Hero_aanpassen_is_openbaar_te_lezen_voor_website_en_app()
    {
        var hero = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/website/hero");
        Assert.Equal("Alaaf! Het feest komt eraan.", hero.GetProperty("title").GetString());

        var image = await UploadAsync("/api/v1/admin/website/images");
        var update = await _bestuur.PutAsJsonAsync("/api/v1/admin/website/settings", new
        {
            heroEyebrow = "CARNAVAL 2027 · LOIL",
            heroTitle = "Loil geet los!",
            heroSubtitle = "Van 7 t/m 9 februari 2027.",
            heroPrimaryLabel = "Bekijk de agenda",
            heroPrimaryLink = "Agenda",
            heroSecondaryLabel = (string?)null,
            heroSecondaryLink = (string?)null,
            heroImage = image,
            facebookPageUrl = "https://www.facebook.com/vrolijkedrammers",
            instagramUrl = (string?)null,
            showYouthPrinces = false,
        });
        Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);

        hero = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/website/hero");
        Assert.Equal("Loil geet los!", hero.GetProperty("title").GetString());
        Assert.Equal("Agenda", hero.GetProperty("primary").GetProperty("link").GetString());
        Assert.Equal(JsonValueKind.Null, hero.GetProperty("secondary").ValueKind);
        Assert.Contains(image, hero.GetProperty("imageUrl").GetString());

        var noLink = await _bestuur.PutAsJsonAsync("/api/v1/admin/website/settings", new
        {
            heroTitle = "Titel",
            heroPrimaryLabel = "Knop zonder bestemming",
            showYouthPrinces = false,
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, noLink.StatusCode);
    }

    [Fact]
    public async Task Kaderlid_uit_de_ledenlijst_met_functie_volgorde_en_zichtbaar_bij_het_lid()
    {
        Guid memberId;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            var member = new Member { Id = IdGenerator.NewId(), MemberNumber = "0412", FullName = "Rick Hoogveld", MembershipStatus = MembershipStatus.Active };
            db.Members.Add(member);
            await db.SaveChangesAsync();
            memberId = member.Id;
        }

        var found = Assert.Single(await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/member-search?q=hoogv") ?? []);
        Assert.Equal(memberId, found.GetProperty("id").GetGuid());
        Assert.False(found.TryGetProperty("email", out _));

        var first = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/kader",
            new { committeeId = 1, memberId, name = "Rick Hoogveld", function = "Bestuurslid", photo = await UploadAsync("/api/v1/admin/website/images") });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var second = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/kader",
            new { committeeId = 1, memberId = (Guid?)null, name = "Iemand buiten de ledenlijst", function = "Voorzitter", photo = (string?)null });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent,
            (await _bestuur.PutAsJsonAsync("/api/v1/admin/website/committees/1/order", new { ids = new[] { secondId, firstId } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _bestuur.PutAsJsonAsync("/api/v1/admin/website/committees/1/order", new { ids = new[] { secondId } })).StatusCode);

        var committees = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/committees") ?? [];
        Assert.Equal(["Bestuur", "Raad van Elf", "Convent", "Leiding dansgarde"], committees.Select(c => c.GetProperty("name").GetString()));
        var bestuur = committees[0].GetProperty("members").EnumerateArray().ToList();
        Assert.Equal(["Iemand buiten de ledenlijst", "Rick Hoogveld"], bestuur.Select(m => m.GetProperty("name").GetString()));
        Assert.Equal("0412", bestuur[1].GetProperty("memberNumber").GetString());
        Assert.StartsWith("http", bestuur[1].GetProperty("photoUrl").GetString());

        var detail = await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/members/{memberId}");
        var kader = Assert.Single(detail.GetProperty("kader").EnumerateArray());
        Assert.Equal("Bestuur", kader.GetProperty("committee").GetString());
        Assert.Equal("Bestuurslid", kader.GetProperty("function").GetString());

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.DeleteAsync("/api/v1/admin/website/committees/1")).StatusCode);
    }

    [Fact]
    public async Task Prinsen_en_jeugdprinsen_en_onderscheidingen_beheren()
    {
        var prince = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/princes",
            new { kind = "Prince", year = 2025, princeName = "Prins Ronnie I", name = "Ronnie Loeters", motto = "Mee lache!", photo = (string?)null });
        Assert.Equal(HttpStatusCode.Created, prince.StatusCode);
        await _bestuur.PostAsJsonAsync("/api/v1/admin/website/princes",
            new { kind = "YouthPrince", year = 2025, princeName = "Jeugdprinses Eva I", name = "Eva", motto = (string?)null, photo = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, (await _bestuur.PostAsJsonAsync("/api/v1/admin/website/princes",
            new { kind = "Prince", year = 1900, princeName = "Te vroeg", name = (string?)null, motto = (string?)null, photo = (string?)null })).StatusCode);

        var youth = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/princes?kind=YouthPrince") ?? [];
        Assert.Equal("Jeugdprinses Eva I", Assert.Single(youth).GetProperty("princeName").GetString());
        var settings = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/website/settings");
        Assert.False(settings.GetProperty("showYouthPrinces").GetBoolean());
        Assert.Equal(1, settings.GetProperty("youthPrinceCount").GetInt32());

        var award = new { type = "Drammertje", year = 2025, recipient = "Harrie Sloot", body = "Al twintig jaar chauffeur.", photo = (string?)null, isPublished = true };
        var a1 = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/awards", award);
        var a2 = await _bestuur.PostAsJsonAsync("/api/v1/admin/website/awards", award with { });
        Assert.Equal(HttpStatusCode.Created, a2.StatusCode);
        var awards = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/awards?type=Drammertje") ?? [];
        Assert.Equal(["harrie-sloot-2025", "harrie-sloot-2025-2"], awards.Select(a => a.GetProperty("slug").GetString()).Order());

        var id = (await a1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/website/awards/{id}")).StatusCode);
        Assert.Single(await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/website/awards") ?? []);
    }

    [Fact]
    public async Task Paginas_met_eigen_webadres_en_alleen_met_het_recht_website_beheren()
    {
        var page = new { slug = "over-ons", title = "Over ons", intro = (string?)null, body = "# Over ons", image = (string?)null, isPublished = true, sortOrder = 10 };
        Assert.Equal(HttpStatusCode.Created, (await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages", page)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages", page)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await _bestuur.PostAsJsonAsync("/api/v1/admin/website/pages", page with { slug = "Over Ons!" })).StatusCode);

        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);
        Assert.Equal(HttpStatusCode.OK, (await redactie.GetAsync("/api/v1/admin/website/pages")).StatusCode);
        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync("/api/v1/admin/website/pages")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.PostAsync("/api/v1/admin/website/images", ContentTests.Multipart("file", "x.jpg", TestImages.JpegWithGps()))).StatusCode);
    }
}
