using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 5: audience-filter (docs/07 §4), publicatiemoment en cache-headers.</summary>
[Collection(SqlServerCollection.Name)]
public class ContentTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        var (_, oid) = await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur);
        _admin = _api.ClientFor(oid);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> CreateEventAsync(string title, string visibility, string status = "Published", string[]? roles = null, DateTimeOffset? publishAt = null)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/admin/events", new
        {
            categoryId = 1,
            title,
            summary = (string?)null,
            description = "**Alaaf!** <script>alert(1)</script>",
            startAt = _api.Clock.UtcNow.AddDays(10),
            endAt = (DateTimeOffset?)null,
            allDay = false,
            locationName = "Zaal De Drammer",
            locationAddress = (string?)null,
            latitude = (decimal?)null,
            longitude = (decimal?)null,
            isHighlight = false,
            badgeText = (string?)null,
            publication = new { visibility, audienceRoles = roles ?? [], status, publishAt },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<List<string>> VisibleEventTitlesAsync(HttpClient client)
    {
        var page = await client.GetFromJsonAsync<JsonElement>("/api/v1/events");
        return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!).Order()];
    }

    [Fact]
    public async Task Audience_matrix_voor_gast_lid_ouder_en_rol()
    {
        await CreateEventAsync("Openbaar", "Public");
        var members = await CreateEventAsync("Leden", "Members");
        await CreateEventAsync("Redactie", "Restricted", roles: [DefaultRoles.Redactie]);
        await CreateEventAsync("Concept", "Public", status: "Draft");
        await CreateEventAsync("Later", "Public", status: "Scheduled", publishAt: _api.Clock.UtcNow.AddDays(1));

        var guest = _api.CreateClient();
        var lid = _api.ClientFor((await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid)).ObjectId);
        var ouder = _api.ClientFor((await _api.CreateUserAsync("ouder@example.com", DefaultRoles.Ouder)).ObjectId);
        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie)).ObjectId);

        Assert.Equal(["Openbaar"], await VisibleEventTitlesAsync(guest));
        Assert.Equal(["Leden", "Openbaar"], await VisibleEventTitlesAsync(lid));
        Assert.Equal(["Openbaar"], await VisibleEventTitlesAsync(ouder));
        Assert.Equal(["Openbaar", "Redactie"], await VisibleEventTitlesAsync(redactie));

        Assert.Equal(HttpStatusCode.NotFound, (await guest.GetAsync($"/api/v1/events/{members}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await ouder.GetAsync($"/api/v1/events/{members}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await lid.GetAsync($"/api/v1/events/{members}")).StatusCode);
    }

    [Fact]
    public async Task Gepubliceerd_event_staat_direct_in_de_publieke_agenda_met_veilige_html()
    {
        var id = await CreateEventAsync("Pronkzitting", "Public");

        Assert.Contains("Pronkzitting", await VisibleEventTitlesAsync(_api.CreateClient()));
        var detail = await _api.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/events/{id}");
        var html = detail.GetProperty("descriptionHtml").GetString()!;
        Assert.Contains("<strong>Alaaf!</strong>", html);
        Assert.DoesNotContain("<script", html);

        var ics = await _api.CreateClient().GetStringAsync($"/api/v1/events/{id}/ical");
        Assert.Contains("SUMMARY:Pronkzitting", ics);
        Assert.Contains("LOCATION:Zaal De Drammer", ics);
    }

    [Fact]
    public async Task Gepland_nieuws_verschijnt_op_het_publicatiemoment_en_de_job_legt_het_vast()
    {
        var created = await _admin.PostAsJsonAsync("/api/v1/admin/news", new
        {
            title = "Prins bekend",
            summary = (string?)null,
            body = "De nieuwe prins is …",
            category = (string?)null,
            expireAt = (DateTimeOffset?)null,
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Scheduled", publishAt = _api.Clock.UtcNow.AddMinutes(10) },
        });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var guest = _api.CreateClient();
        Assert.Equal(0, (await guest.GetFromJsonAsync<JsonElement>("/api/v1/news")).GetProperty("totalCount").GetInt32());

        _api.Clock.Advance(TimeSpan.FromMinutes(11));

        Assert.Equal(1, (await guest.GetFromJsonAsync<JsonElement>("/api/v1/news")).GetProperty("totalCount").GetInt32());
        using var scope = _api.Services.CreateScope();
        await ActivatorUtilities.CreateInstance<ContentPublisherJob>(scope.ServiceProvider).ExecuteAsync(default);
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        Assert.Equal(PublicationStatus.Published, (await db.News.SingleAsync(n => n.Id == id)).Status);
        Assert.True(await db.AuditLog.AnyAsync(a => a.Action == "news.published" && a.EntityId == id.ToString()));
    }

    [Fact]
    public async Task Verlopen_nieuws_verdwijnt()
    {
        await _admin.PostAsJsonAsync("/api/v1/admin/news", new
        {
            title = "Kort",
            summary = (string?)null,
            body = "…",
            category = (string?)null,
            expireAt = _api.Clock.UtcNow.AddHours(1),
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Published", publishAt = (DateTimeOffset?)null },
        });
        Assert.Equal(1, (await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/news")).GetProperty("totalCount").GetInt32());

        _api.Clock.Advance(TimeSpan.FromHours(2));

        Assert.Equal(0, (await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/news")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Publieke_lijst_heeft_cacheheaders_en_etag_304()
    {
        await CreateEventAsync("Openbaar", "Public");
        var guest = _api.CreateClient();

        var first = await guest.GetAsync("/api/v1/events");
        Assert.Equal("public, no-cache", first.Headers.CacheControl!.ToString());
        var etag = first.Headers.ETag!;

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/events");
        request.Headers.IfNoneMatch.Add(etag);
        Assert.Equal(HttpStatusCode.NotModified, (await guest.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Beperkt_zonder_doelgroep_wordt_geweigerd()
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/admin/events", new
        {
            categoryId = 1,
            title = "Fout",
            startAt = _api.Clock.UtcNow.AddDays(1),
            allDay = false,
            isHighlight = false,
            publication = new { visibility = "Restricted", audienceRoles = Array.Empty<string>(), status = "Published" },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    internal static MultipartFormDataContent Multipart(string field, string fileName, byte[] bytes, string contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { content, field, fileName } };
    }
}
