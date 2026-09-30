using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Worker.Outbox;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Drammers.IntegrationTests;

/// <summary>Fase 21b: galerijen per soort en bulkacties op foto's (verbergen, verplaatsen, fotograaf, verwijderen).</summary>
[Collection(SqlServerCollection.Name)]
public class PhotoGalleryTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _admin = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> AlbumAsync(string title, string category)
    {
        var response = await _admin.PostAsJsonAsync("/api/v1/admin/photo-albums", new
        {
            title,
            albumDate = "2027-01-08",
            description = (string?)null,
            eventId = (Guid?)null,
            category,
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Published" },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<List<Guid>> UploadAsync(Guid album, int count)
    {
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            // Zoals het portal: één foto per verzoek (voortgang en opnieuw proberen per foto).
            var response = await _admin.PostAsync($"/api/v1/admin/photo-albums/{album}/photos", ContentTests.Multipart("files", $"IMG_{i}.jpg", TestImages.JpegWithGps()));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            ids.Add((await response.Content.ReadFromJsonAsync<List<JsonElement>>())![0].GetProperty("id").GetGuid());
        }

        var processor = new OutboxProcessor(_api.Services.GetRequiredService<IServiceScopeFactory>(), new OutboxSignal(), TimeProvider.System, NullLogger<OutboxProcessor>.Instance);
        while (await processor.ProcessBatchAsync(default) > 0)
        {
        }

        return ids;
    }

    private Task<HttpResponseMessage> BulkAsync(Guid album, IEnumerable<Guid> ids, string action, Guid? target = null, string? photographer = null) =>
        _admin.PostAsJsonAsync($"/api/v1/admin/photo-albums/{album}/photos/bulk", new { photoIds = ids, action, targetAlbumId = target, photographer });

    private async Task<int> PublicCountAsync(Guid album) =>
        (await _api.CreateClient().GetFromJsonAsync<List<JsonElement>>($"/api/v1/photo-albums/{album}/photos"))!.Count;

    [Fact]
    public async Task Galerijen_per_soort_met_omslag_in_het_portal_en_filter_op_de_website()
    {
        var pronk = await AlbumAsync("Pronkzitting 2027", "Pronkzitting");
        await AlbumAsync("Optocht 2027", "Parade");
        await UploadAsync(pronk, 1);

        var admin = await _admin.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/photo-albums") ?? [];
        var row = admin.Single(a => a.GetProperty("id").GetGuid() == pronk);
        Assert.Equal("Pronkzitting", row.GetProperty("category").GetString());
        Assert.StartsWith("http", row.GetProperty("coverUrl").GetString());

        var filtered = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/photo-albums?category=Pronkzitting");
        var only = Assert.Single(filtered.GetProperty("items").EnumerateArray());
        Assert.Equal("Pronkzitting 2027", only.GetProperty("title").GetString());
        Assert.Equal("Pronkzitting", only.GetProperty("category").GetString());
    }

    [Fact]
    public async Task Bulk_verbergen_verplaatsen_fotograaf_en_verwijderen()
    {
        var a = await AlbumAsync("Carnaval zaterdag", "Carnival");
        var b = await AlbumAsync("Carnaval zondag", "Carnival");
        var photos = await UploadAsync(a, 4);
        Assert.Equal(4, await PublicCountAsync(a));

        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(a, photos.Take(2), "Hide")).StatusCode);
        Assert.Equal(2, await PublicCountAsync(a));
        await BulkAsync(a, photos.Take(1), "Show");
        Assert.Equal(3, await PublicCountAsync(a));

        var photographer = await BulkAsync(a, photos, "SetPhotographer", photographer: "Fotoclub Loil");
        Assert.Equal(4, (await photographer.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("count").GetInt32());
        var shown = await _api.CreateClient().GetFromJsonAsync<List<JsonElement>>($"/api/v1/photo-albums/{a}/photos") ?? [];
        Assert.All(shown, p => Assert.Equal("Fotoclub Loil", p.GetProperty("photographer").GetString()));

        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(a, [photos[2]], "Move", target: b)).StatusCode);
        Assert.Equal(1, await PublicCountAsync(b));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BulkAsync(a, [photos[2]], "Hide")).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await BulkAsync(a, [photos[3]], "Move", target: a)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await BulkAsync(a, [photos[0], photos[3]], "Delete")).StatusCode);
        var album = await _admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/photo-albums/{a}");
        Assert.Equal([photos[1]], album.GetProperty("photos").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()));

        var audit = await _admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-log?entityType=PhotoAlbum");
        Assert.Contains(audit.GetProperty("items").EnumerateArray(), e => e.GetProperty("action").GetString() == "photo.bulk-delete");
    }
}
