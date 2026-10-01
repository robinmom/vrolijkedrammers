using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Parade.Parades;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 22a: jury van de optocht — uitnodigen, per optocht indelen, hoofdjury, weging en kopiëren naar een nieuwe optocht.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeJuryTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _paradeId = await CreateParadeAsync("Optocht Loil 2027", "2027-02-07");
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<Guid> CreateParadeAsync(string name, string date)
    {
        var now = _api.Clock.UtcNow;
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name,
            paradeDate = date,
            startTime = "13:30:00",
            startLocation = (string?)null,
            routeDescription = (string?)null,
            routeLengthKm = (decimal?)null,
            registrationOpensAt = now.AddDays(-1),
            registrationClosesAt = now.AddDays(20),
            editDeadlineAt = (DateTimeOffset?)null,
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 2,
            maxDocumentSizeMb = 5,
            status = "RegistrationOpen",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> OverviewAsync(HttpClient client, Guid? paradeId = null) =>
        await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/jury{(paradeId is { } id ? $"?paradeId={id}" : "")}");

    private static JsonElement Juror(JsonElement overview, string email) =>
        overview.GetProperty("jurors").EnumerateArray().Single(j => j.GetProperty("email").GetString() == email);

    [Fact]
    public async Task Bestuur_nodigt_uit_deelt_in_en_maakt_hoofdjury_een_nieuwe_optocht_neemt_alles_over()
    {
        var invited = await _bestuur.PostAsJsonAsync("/api/v1/admin/jury/jurors", new { name = "Carla Smit", email = "Carla@Example.com" });
        Assert.Equal(HttpStatusCode.Created, invited.StatusCode);
        var carlaId = (await invited.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Contains(_api.Emails.Sent, m => m.To == "carla@example.com" && m.Subject.Contains("jurylid", StringComparison.Ordinal));

        var overview = await OverviewAsync(_bestuur);
        Assert.Equal(_paradeId, overview.GetProperty("paradeId").GetGuid());
        var carla = Juror(overview, "carla@example.com");
        Assert.True(carla.GetProperty("invited").GetBoolean());
        Assert.Empty(carla.GetProperty("categoryIds").EnumerateArray());

        // Standaardweging zoals de Excel van 2026: wagens kwaliteit 2x, loopgroepen algemene indruk 2x.
        var categories = overview.GetProperty("categories").EnumerateArray().ToList();
        var wagon = categories.First(c => c.GetProperty("quality").GetInt32() == 2);
        var group = categories.First(c => c.GetProperty("overall").GetInt32() == 2);
        var (wagonId, groupId) = (wagon.GetProperty("categoryId").GetInt32(), group.GetProperty("categoryId").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync(
            $"/api/v1/admin/jury/parades/{_paradeId}/jurors/{carlaId}/categories", new { categoryIds = new[] { wagonId, groupId } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/jurors/{carlaId}/head-jury", new { headJury = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/categories/{wagonId}",
            new { judged = true, originality = 1, carnivalesque = 1, quality = 3, overall = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/categories/{wagonId}",
            new { judged = true, originality = 0, carnivalesque = 0, quality = 0, overall = 0 })).StatusCode);

        overview = await OverviewAsync(_bestuur);
        carla = Juror(overview, "carla@example.com");
        Assert.True(carla.GetProperty("headJury").GetBoolean());
        Assert.Equal([wagonId, groupId], carla.GetProperty("categoryIds").EnumerateArray().Select(c => c.GetInt32()).Order());

        // Tweede optocht in hetzelfde jaar: weging en indeling gaan mee; de eerste blijft de huidige.
        var second = await CreateParadeAsync("Optocht Loil 2027 (inhaal)", "2027-02-14");
        var copy = await OverviewAsync(_bestuur, second);
        Assert.Equal("Optocht Loil 2027 (inhaal)", copy.GetProperty("paradeName").GetString());
        Assert.Equal(3, copy.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("categoryId").GetInt32() == wagonId).GetProperty("quality").GetInt32());
        Assert.Equal(2, Juror(copy, "carla@example.com").GetProperty("categoryIds").GetArrayLength());
        Assert.Equal(_paradeId, (await OverviewAsync(_bestuur)).GetProperty("paradeId").GetGuid());

        // Is de eerste afgerond, dan wordt de tweede de huidige optocht.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            await db.Parades.Where(p => p.Id == _paradeId).ExecuteUpdateAsync(u => u.SetProperty(p => p.Status, ParadeStatus.Completed));
        }

        Assert.Equal(second, (await OverviewAsync(_bestuur)).GetProperty("paradeId").GetGuid());

        // Uit de jury halen: rol eraf, nergens meer ingedeeld.
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.DeleteAsync($"/api/v1/admin/jury/jurors/{carlaId}")).StatusCode);
        Assert.DoesNotContain((await OverviewAsync(_bestuur)).GetProperty("jurors").EnumerateArray(), j => j.GetProperty("email").GetString() == "carla@example.com");
    }

    [Fact]
    public async Task Hoofdjury_mag_indelen_maar_niet_uitnodigen_of_wegen_en_een_lid_mag_niets()
    {
        var (_, headOid) = await _api.CreateUserAsync("hoofd@example.com", DefaultRoles.Jury, DefaultRoles.Hoofdjury);
        var (jurorId, _) = await _api.CreateUserAsync("jurylid@example.com", DefaultRoles.Jury);
        var head = _api.ClientFor(headOid);

        var overview = await OverviewAsync(head);
        Assert.Equal(2, overview.GetProperty("jurors").GetArrayLength());
        var categoryId = overview.GetProperty("categories")[0].GetProperty("categoryId").GetInt32();
        Assert.Equal(HttpStatusCode.NoContent, (await head.PutAsJsonAsync(
            $"/api/v1/admin/jury/parades/{_paradeId}/jurors/{jurorId}/categories", new { categoryIds = new[] { categoryId } })).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await head.PostAsJsonAsync("/api/v1/admin/jury/jurors", new { name = "X", email = "x@example.com" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await head.PutAsJsonAsync($"/api/v1/admin/jury/jurors/{jurorId}/head-jury", new { headJury = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await head.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/categories/{categoryId}",
            new { judged = true, originality = 1, carnivalesque = 1, quality = 1, overall = 1 })).StatusCode);

        // Een gewoon lid of een jurylid zonder hoofdjury komt niet in het jurybeheer.
        var (_, jurorOid) = await _api.CreateUserAsync("ander@example.com", DefaultRoles.Jury);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(jurorOid).GetAsync("/api/v1/admin/jury")).StatusCode);
        var (_, lidOid) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).GetAsync("/api/v1/admin/jury")).StatusCode);

        // Iemand die geen jurylid is, kun je niet indelen.
        var (lidId, _) = await _api.CreateUserAsync("lid2@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.NotFound, (await head.PutAsJsonAsync(
            $"/api/v1/admin/jury/parades/{_paradeId}/jurors/{lidId}/categories", new { categoryIds = new[] { categoryId } })).StatusCode);
    }
}
