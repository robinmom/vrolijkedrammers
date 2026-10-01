using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Drammers.Infrastructure.Content.Import;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Website;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 21e: de oude WordPress-site overzetten (nep-WordPress met nagemaakte inhoud), hervatbaar en zonder dubbelingen.</summary>
[Collection(SqlServerCollection.Name)]
public class WordPressImportTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;

    public async Task InitializeAsync() =>
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync(), services =>
        {
            services.Configure<WordPressImportOptions>(o => o.WordPressUrl = new Uri("https://wp.test/"));
            services.AddHttpClient<WordPressSource>().ConfigurePrimaryHttpMessageHandler(() => new FakeWordPress());
        });

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task RunImportAsync()
    {
        using var scope = _api.Services.CreateScope();
        var importer = scope.ServiceProvider.GetRequiredService<WebsiteImporter>();
        await importer.PlanAsync(default);
        while (await importer.ProcessBatchAsync(TimeSpan.FromMinutes(1), default) > 0)
        {
        }
    }

    [Fact]
    public async Task Oude_site_wordt_overgezet_met_doorverwijzingen_en_zonder_dubbelingen()
    {
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            db.Members.Add(new Member { Id = IdGenerator.NewId(), MemberNumber = "0099", FullName = "Karin Voorbeeld", MembershipStatus = MembershipStatus.Active });
            await db.SaveChangesAsync();
        }

        await RunImportAsync();
        await RunImportAsync();

        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();

            var news = await db.News.AsNoTracking().OrderBy(n => n.PublishAt).ToListAsync();
            Assert.Equal(["uitslag-optocht-test", "fotos-van-de-optocht"], news.Select(n => n.Slug));
            Assert.All(news, n => Assert.True(n.ShowOnWebsite));
            Assert.Equal(new DateTime(2026, 2, 16, 10, 0, 0, DateTimeKind.Utc), news[0].PublishAt);
            Assert.Contains("| Prijs | Naam |", news[0].Body);
            Assert.NotNull(news[0].ImageBlobPath);
            Assert.Equal("Uitslag", news[0].Category);

            var album = await db.PhotoAlbums.AsNoTracking().SingleAsync(a => a.Title == "Foto’s van de optocht");
            Assert.Equal(Modules.Content.Photos.PhotoCategory.Parade, album.Category);
            Assert.Contains($"[Bekijk de foto's](/fotos/{album.Id:N})", news[1].Body);
            Assert.Equal(4, await db.Photos.CountAsync(p => p.AlbumId == album.Id));

            var gallery = await db.PhotoAlbums.AsNoTracking().SingleAsync(a => a.Title == "Pronkzitting 2027");
            Assert.Equal(5, await db.Photos.CountAsync(p => p.AlbumId == gallery.Id));

            var page = await db.WebsitePages.AsNoTracking().SingleAsync();
            Assert.Equal(("over-ons", true), (page.Slug, page.IsPublished));
            Assert.StartsWith("Sinds 1958", page.Body);

            var princes = await db.Princes.AsNoTracking().OrderBy(p => p.Kind).ThenByDescending(p => p.Year).ToListAsync();
            Assert.Equal(["Prins Piet I", "Prins Klaas II", "Jeugdprinses Anna I"], princes.Select(p => p.PrinceName));
            Assert.Equal(("Piet Test", 2025, "Alaaf!"), (princes[0].Name, princes[0].Year, princes[0].Motto));
            Assert.NotNull(princes[0].PhotoBlobPath);
            Assert.Equal(PrinceKind.YouthPrince, princes[2].Kind);

            var award = await db.Awards.AsNoTracking().SingleAsync();
            Assert.Equal((AwardType.Drammertje, 2024, "Jan Voorbeeld", "jan-voorbeeld-2024"), (award.Type, award.Year, award.Recipient, award.Slug));
            Assert.Equal("Al **twintig** jaar actief.", award.Body);

            var kader = await db.CommitteeMembers.AsNoTracking().SingleAsync();
            Assert.Equal(("Karin Voorbeeld", "Voorzitter", 1), (kader.Name, kader.Function, kader.CommitteeId));
            Assert.NotNull(kader.MemberId);

            var items = await db.WebsiteImportItems.AsNoTracking().ToListAsync();
            Assert.Contains(items, i => i is { Kind: WebsiteImportKind.Page, Status: WebsiteImportStatus.Skipped, Title: "Home" });
            Assert.DoesNotContain(items, i => i.Status is WebsiteImportStatus.Failed or WebsiteImportStatus.Pending);
        }

        // Oude adressen sturen door naar de nieuwe pagina's.
        var client = _api.CreateClient(new() { AllowAutoRedirect = false });
        foreach (var (from, to) in new[]
        {
            ("/uitslag-optocht-test/", "/nieuws/uitslag-optocht-test"),
            ("/jan-voorbeeld/", "/onderscheidingen/jan-voorbeeld-2024"),
            ("/prins/piet-test/", "/prinsengalerie"),
            ("/aanmelden-lid/", "/lid-worden/"),
            ("/commissies/bestuur/", "/kader?commissie=bestuur"),
        })
        {
            var response = await client.GetAsync(from);
            Assert.True(response.StatusCode == HttpStatusCode.MovedPermanently, $"{from} gaf {response.StatusCode}");
            Assert.Equal(to, response.Headers.Location?.OriginalString);
        }

        var pronk = await client.GetAsync("/pronkzitting-2027/");
        Assert.Equal(HttpStatusCode.MovedPermanently, pronk.StatusCode);
        Assert.StartsWith("/fotos/", pronk.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Portal_start_de_import_en_toont_de_voortgang()
    {
        var bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        Assert.Equal(HttpStatusCode.Accepted, (await bestuur.PostAsync("/api/v1/admin/website/import", null)).StatusCode);
        var status = await bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/website/import");
        Assert.True(status.GetProperty("running").GetBoolean());

        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        Assert.Equal(HttpStatusCode.Forbidden, (await lid.PostAsync("/api/v1/admin/website/import", null)).StatusCode);
    }

    /// <summary>Nagemaakte WordPress-site: REST-API, archiefpagina's en afbeeldingen.</summary>
    private sealed class FakeWordPress : HttpMessageHandler
    {
        private static object Item(long id, string slug, string title, string html, string date = "2026-02-16T10:00:00", string? featured = null,
            string[]? classes = null, string[]? categories = null) => new Dictionary<string, object?>
            {
                ["id"] = id,
                ["date_gmt"] = date,
                ["slug"] = slug,
                ["link"] = $"https://wp.test/{slug}/",
                ["parent"] = 0,
                ["title"] = new { rendered = title },
                ["content"] = new { rendered = html },
                ["excerpt"] = new { rendered = "<p>Korte tekst […]</p>" },
                ["class_list"] = classes ?? [],
                ["_embedded"] = new Dictionary<string, object?>
                {
                    ["wp:featuredmedia"] = featured is null ? null : new[] { new { source_url = featured } },
                    ["wp:term"] = new[] { (categories ?? []).Select(c => new { name = c, taxonomy = "category" }).ToArray() },
                },
            };

        private static string Gallery(int count, string name) =>
            string.Concat(Enumerable.Range(1, count).Select(i => $"<figure><img src=\"https://wp.test/uploads/{name}-{i}-1024x768.jpg\"></figure>"));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/uploads/", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(TestImages.JpegWithGps()) });
            }

            object? json = path switch
            {
                "/wp-json/wp/v2/posts" => new[]
                {
                    Item(11, "uitslag-optocht-test", "Uitslag optocht test", "<p>Uitslag:</p><table><tr><td>Prijs</td><td>Naam</td></tr><tr><td>1</td><td>De Testers</td></tr></table>",
                        featured: "https://wp.test/uploads/uitslag.jpg", categories: ["Uitslag"]),
                    Item(12, "fotos-van-de-optocht", "Foto&#8217;s van de optocht", Gallery(4, "optocht"), date: "2026-02-17T10:00:00"),
                },
                "/wp-json/wp/v2/pages" => new[]
                {
                    Item(21, "home", "Home", "<p>Welkom</p>"),
                    Item(22, "over-ons", "Over ons", "<p>Sinds 1958 vieren wij carnaval in Loil.</p>"),
                    Item(23, "pronkzitting-2027", "Pronkzitting 2027", "<p>De foto's.</p>" + Gallery(5, "pronk")),
                },
                "/wp-json/wp/v2/award" => new[] { Item(31, "jan-voorbeeld", "Jan Voorbeeld", "", classes: ["award", "award_type-drammertje"]) },
                _ => null,
            };
            if (json is not null)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(json) };
                response.Headers.Add("X-WP-TotalPages", "1");
                return Task.FromResult(response);
            }

            var html = path switch
            {
                "/prins/" => Profile("Prins Piet I", "Piet Test", "2025", "\"Alaaf!\"") + Profile("Prins Klaas II", "Klaas Test", "1999", null) + Profile("Prins Zonder Jaar", "Niemand", "", null),
                "/jeugdprins/" => Profile("Jeugdprinses Anna I", "Anna", "2024", null),
                "/commissies/bestuur/" => """<div class="member-profile"><img src="https://wp.test/uploads/karin.jpg"><h3 class="member-name">Karin Voorbeeld</h3><p class="member-function">Voorzitter</p></div>""",
                "/jan-voorbeeld/" => """
                    <article class="single-award-container"><header><h1>Jan Voorbeeld</h1><h2>'t drammertje <i>(2024)</i></h2></header>
                    <div class="award-content"><p>Al <strong>twintig</strong> jaar actief.</p></div></article>
                    """,
                _ => null,
            };
            return Task.FromResult(html is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") });
        }

        private static string Profile(string title, string name, string year, string? motto) =>
            $"""<div class="member-profile"><img src="https://wp.test/uploads/{name.Replace(' ', '-')}-358x360.jpg"><h3 class="member-name prins-title">{title}</h3><h4 class="member-name prins-name">{name}</h4><p class="member-function">{year}</p>{(motto is null ? "" : $"<div class=\"member-committee\">{motto}</div>")}</div>""";
    }
}
