using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;

namespace Drammers.IntegrationTests;

/// <summary>De agenda-categorieën zelf beheren: toevoegen (met eigen code), hernoemen, verwijderen en de rechtencontrole.</summary>
[Collection(SqlServerCollection.Name)]
public class EventCategoryTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _redactie = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _redactie = _api.ClientFor((await _api.CreateUserAsync("redactie-agenda@example.com", DefaultRoles.Redactie)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static JsonElement Category(JsonElement list, string name) =>
        list.EnumerateArray().Single(c => c.GetProperty("name").GetString() == name);

    [Fact]
    public async Task Toevoegen_hernoemen_en_verwijderen()
    {
        // Toevoegen: de code wordt URL-vriendelijk uit de naam afgeleid.
        var created = await _redactie.PostAsJsonAsync("/api/v1/admin/events/categories", new { name = "Pronkzitting 2027", sortOrder = 50 });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        var afterCreate = await _redactie.GetFromJsonAsync<JsonElement>("/api/v1/admin/events/categories");
        var row = Category(afterCreate, "Pronkzitting 2027");
        Assert.Equal("pronkzitting-2027", row.GetProperty("code").GetString());
        Assert.Equal(0, row.GetProperty("eventCount").GetInt32());

        // Hernoemen houdt dezelfde code (de website/agenda filteren erop).
        var renamed = await _redactie.PutAsJsonAsync($"/api/v1/admin/events/categories/{id}", new { name = "Pronkzitting", sortOrder = 55 });
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        var afterRename = await _redactie.GetFromJsonAsync<JsonElement>("/api/v1/admin/events/categories");
        Assert.Equal("pronkzitting-2027", Category(afterRename, "Pronkzitting").GetProperty("code").GetString());

        // Verwijderen kan omdat er geen activiteiten op staan.
        var deleted = await _redactie.DeleteAsync($"/api/v1/admin/events/categories/{id}");
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var afterDelete = await _redactie.GetFromJsonAsync<JsonElement>("/api/v1/admin/events/categories");
        Assert.DoesNotContain(afterDelete.EnumerateArray(), c => c.GetProperty("name").GetString() == "Pronkzitting");
    }

    [Fact]
    public async Task Dubbele_naam_geeft_een_nette_fout()
    {
        var response = await _redactie.PostAsJsonAsync("/api/v1/admin/events/categories", new { name = "Carnaval", sortOrder = 99 });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Zonder_recht_geen_toegang()
    {
        var penningmeester = _api.ClientFor((await _api.CreateUserAsync("penningmeester-agenda@example.com", DefaultRoles.Penningmeester)).ObjectId);

        var response = await penningmeester.GetAsync("/api/v1/admin/events/categories");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
