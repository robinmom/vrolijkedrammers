using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using Azure.Storage.Blobs;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SixLabors.ImageSharp;

namespace Drammers.IntegrationTests;

/// <summary>Fase 5: upload-pijplijn (ADR-008) tegen Azurite: type-controle, scan, derivaten zonder GPS, SAS.</summary>
[Collection(SqlServerCollection.Name)]
public class FileUploadTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _admin = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> CreateAlbumAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/admin/photo-albums", new
        {
            title = "Optocht 2027",
            albumDate = "2027-02-07",
            description = (string?)null,
            eventId = (Guid?)null,
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Published" },
        });
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task ProcessOutboxAsync(AuthenticatedApiFactory api)
    {
        var processor = new OutboxProcessor(api.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<OutboxProcessor>.Instance);
        while (await processor.ProcessBatchAsync(default) > 0)
        {
        }
    }

    [Fact]
    public async Task Foto_krijgt_derivaten_zonder_GPS_en_is_via_een_kortlevende_SAS_te_bekijken()
    {
        var album = await CreateAlbumAsync(_admin);
        var upload = await _admin.PostAsync($"/api/v1/admin/photo-albums/{album}/photos", ContentTests.Multipart("files", "IMG_0001.jpg", TestImages.JpegWithGps()));
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        Assert.Empty(await _api.CreateClient().GetFromJsonAsync<List<JsonElement>>($"/api/v1/photo-albums/{album}/photos") ?? []);

        await ProcessOutboxAsync(_api);

        var photo = Assert.Single(await _api.CreateClient().GetFromJsonAsync<List<JsonElement>>($"/api/v1/photo-albums/{album}/photos") ?? []);
        Assert.Equal(1600, photo.GetProperty("width").GetInt32());
        Assert.Equal(new DateTime(2027, 2, 6), photo.GetProperty("takenAt").GetDateTime().ToLocalTime().Date);

        using var http = new HttpClient();
        var displayUrl = new Uri(photo.GetProperty("displayUrl").GetString()!);
        using var display = Image.Load(await http.GetByteArrayAsync(displayUrl));
        Assert.Null(display.Metadata.ExifProfile);
        Assert.Equal(1600, Math.Max(display.Width, display.Height));
        using var thumbnail = Image.Load(await http.GetByteArrayAsync(photo.GetProperty("thumbnailUrl").GetString()));
        Assert.Equal(400, Math.Max(thumbnail.Width, thumbnail.Height));

        var expiry = DateTimeOffset.Parse(HttpUtility.ParseQueryString(displayUrl.Query)["se"]!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(expiry <= _api.Clock.UtcNow.AddMinutes(15).AddSeconds(5));
        var anonymous = await http.GetAsync(displayUrl.GetLeftPart(UriPartial.Path));
        Assert.NotEqual(HttpStatusCode.OK, anonymous.StatusCode);
    }

    [Fact]
    public async Task Verborgen_foto_is_direct_onzichtbaar()
    {
        var album = await CreateAlbumAsync(_admin);
        var upload = await _admin.PostAsync($"/api/v1/admin/photo-albums/{album}/photos", ContentTests.Multipart("files", "a.jpg", TestImages.JpegWithGps()));
        var photoId = (await upload.Content.ReadFromJsonAsync<List<JsonElement>>())![0].GetProperty("id").GetGuid();
        await ProcessOutboxAsync(_api);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PutAsJsonAsync($"/api/v1/admin/photos/{photoId}", new { hidden = true, caption = (string?)null, photographer = (string?)null })).StatusCode);

        Assert.Empty(await _api.CreateClient().GetFromJsonAsync<List<JsonElement>>($"/api/v1/photo-albums/{album}/photos") ?? []);
        using var scope = _api.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().AuditLog.AnyAsync(a => a.Action == "photo.hidden"));
    }

    [Fact]
    public async Task Uitvoerbaar_bestand_met_jpg_extensie_wordt_geweigerd()
    {
        var album = await CreateAlbumAsync(_admin);

        var response = await _admin.PostAsync($"/api/v1/admin/photo-albums/{album}/photos", ContentTests.Multipart("files", "vakantie.jpg", TestImages.ExecutableDisguisedAsJpeg()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("FILE_TYPE_NOT_ALLOWED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Besmet_bestand_wordt_verwijderd_en_geaudit()
    {
        await using var api = new AuthenticatedApiFactory(
            await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync(), s => s.AddSingleton<IMalwareScanner, AlwaysInfected>());
        var admin = api.ClientFor((await api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var created = await admin.PostAsJsonAsync("/api/v1/admin/events", new
        {
            categoryId = 1,
            title = "Met afbeelding",
            startAt = DateTimeOffset.UtcNow.AddDays(3),
            allDay = false,
            isHighlight = false,
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Draft" },
        });
        var eventId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var quarantineBefore = await CountBlobsAsync(FileContainers.Quarantine);

        var response = await admin.PutAsync($"/api/v1/admin/events/{eventId}/image", ContentTests.Multipart("file", "poster.jpg", TestImages.JpegWithGps()));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("FILE_INFECTED", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(quarantineBefore, await CountBlobsAsync(FileContainers.Quarantine));
        using var scope = api.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().AuditLog.AnyAsync(a => a.Action == "file.rejected-malware"));
    }

    [Fact]
    public async Task Eventafbeelding_wordt_zonder_metadata_opgeslagen()
    {
        var created = await _admin.PostAsJsonAsync("/api/v1/admin/events", new
        {
            categoryId = 1,
            title = "Poster",
            startAt = DateTimeOffset.UtcNow.AddDays(3),
            allDay = false,
            isHighlight = false,
            publication = new { visibility = "Public", audienceRoles = Array.Empty<string>(), status = "Published" },
        });
        var eventId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PutAsync($"/api/v1/admin/events/{eventId}/image", ContentTests.Multipart("file", "poster.jpg", TestImages.JpegWithGps()))).StatusCode);

        var detail = await _api.CreateClient().GetFromJsonAsync<JsonElement>($"/api/v1/events/{eventId}");
        using var http = new HttpClient();
        using var image = Image.Load(await http.GetByteArrayAsync(detail.GetProperty("imageUrl").GetString()));
        Assert.Null(image.Metadata.ExifProfile);
    }

    private async Task<int> CountBlobsAsync(string container)
    {
        var client = new BlobServiceClient(sql.BlobConnectionString).GetBlobContainerClient(container);
        var count = 0;
        await foreach (var _ in client.GetBlobsAsync())
        {
            count++;
        }

        return count;
    }

    private sealed class AlwaysInfected : IMalwareScanner
    {
        public Task<ScanResult> ScanAsync(string container, string path, CancellationToken cancellationToken) => Task.FromResult(ScanResult.Infected);
    }
}
