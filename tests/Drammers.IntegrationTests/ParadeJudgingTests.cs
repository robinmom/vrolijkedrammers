using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 22b: jureren in de app — inzendingen in startvolgorde, scores (nieuwste wint), buiten categorie en indienen.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeJudgingTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        var now = _api.Clock.UtcNow;
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/parades", new
        {
            carnivalYearId = 1,
            name = "Optocht Loil 2027",
            paradeDate = "2027-02-07",
            startTime = "13:30:00",
            startLocation = (string?)null,
            routeDescription = (string?)null,
            routeLengthKm = (decimal?)null,
            registrationOpensAt = now.AddDays(-10),
            registrationClosesAt = now.AddDays(-1),
            editDeadlineAt = (DateTimeOffset?)null,
            subjectRequired = true,
            defaultSpacingMeters = 5m,
            maxDocumentsPerRegistration = 2,
            maxDocumentSizeMb = 5,
            status = "Final",
        });
        _paradeId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<(int Wagons, int Groups, Guid A, Guid B, Guid C)> SeedAsync()
    {
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        var categories = await db.ParadeCategories.OrderBy(c => c.SortOrder).ToListAsync();
        var (wagons, groups) = (categories.First(c => c.HasVehicle).Id, categories.First(c => !c.HasVehicle).Id);
        ParadeRegistration Entry(int number, int start, int category, string name) => new()
        {
            Id = IdGenerator.NewId(),
            ParadeId = _paradeId,
            CarnivalYearId = 1,
            RegistrationNumber = number,
            StartNumber = start,
            CategoryId = category,
            GroupName = name,
            Subject = $"Motto van {name}",
            Status = RegistrationStatus.Approved,
            Source = RegistrationSource.App,
        };
        var (a, b, c) = (Entry(1, 7, wagons, "De Snotapen"), Entry(2, 5, wagons, "De Beunhazen"), Entry(3, 6, groups, "DwarZ"));
        db.ParadeRegistrations.AddRange(a, b, c);
        await db.SaveChangesAsync();
        return (wagons, groups, a.Id, b.Id, c.Id);
    }

    private static object Score(Guid registration, int pass, string criterion, int value, DateTimeOffset at) =>
        new { registrationId = registration, pass, criterion, value, scoredAt = at.UtcDateTime };

    [Fact]
    public async Task Jurylid_beoordeelt_buiten_categorie_wacht_op_akkoord_en_na_indienen_is_alles_vast()
    {
        var (wagons, _, a, b, c) = await SeedAsync();
        var (jurorId, jurorOid) = await _api.CreateUserAsync("jurylid@example.com", DefaultRoles.Jury);
        var juror = _api.ClientFor(jurorOid);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/jurors/{jurorId}/categories", new { categoryIds = new[] { wagons } });

        var session = await juror.GetFromJsonAsync<JsonElement>("/api/v1/jury/current");
        var entries = session.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal([5, 6, 7], entries.Select(e => e.GetProperty("startNumber").GetInt32()));
        Assert.Equal([true, false, true], entries.Select(e => e.GetProperty("assigned").GetBoolean()));
        Assert.Equal("Motto van DwarZ", entries[1].GetProperty("motto").GetString());

        var t = _api.Clock.UtcNow;
        var saved = await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new
        {
            scores = new[]
            {
                Score(a, 1, "Originality", 72, t), Score(a, 1, "Carnivalesque", 65, t), Score(a, 1, "Quality", 80, t), Score(a, 1, "Overall", 70, t),
                Score(c, 1, "Originality", 60, t),
            },
        });
        Assert.Equal(5, (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("changed").GetInt32());

        // Later verstuurd vanaf een toestel zonder netwerk: een oudere invulling wint niet, een nieuwere wel.
        await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores = new[] { Score(a, 1, "Originality", 10, t.AddMinutes(-5)) } });
        await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores = new[] { Score(a, 1, "Quality", 82, t.AddMinutes(1)) } });
        var mine = (await juror.GetFromJsonAsync<JsonElement>("/api/v1/jury/current")).GetProperty("scores").EnumerateArray()
            .Where(s => s.GetProperty("registrationId").GetGuid() == a).ToDictionary(s => s.GetProperty("criterion").GetString()!, s => s.GetProperty("value").GetInt32());
        Assert.Equal((72, 82), (mine["Originality"], mine["Quality"]));

        Assert.False((await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores = new[] { Score(a, 4, "Overall", 50, t) } })).IsSuccessStatusCode);
        Assert.False((await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores = new[] { Score(a, 1, "Overall", 101, t) } })).IsSuccessStatusCode);

        // Portal: voortgang en de beoordeling buiten categorie (zonder scores).
        var overview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jury");
        var row = overview.GetProperty("jurors").EnumerateArray().Single(j => j.GetProperty("userId").GetGuid() == jurorId);
        Assert.Equal((1, 2), (row.GetProperty("scored").GetInt32(), row.GetProperty("assigned").GetInt32()));
        var outside = Assert.Single(overview.GetProperty("outside").EnumerateArray());
        Assert.Equal((c, 1, JsonValueKind.Null), (outside.GetProperty("registrationId").GetGuid(), outside.GetProperty("passes").GetInt32(), outside.GetProperty("decision").ValueKind));
        Assert.DoesNotContain("value", outside.GetRawText(), StringComparison.Ordinal);

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/outside",
            new { decisions = new[] { new { userId = jurorId, registrationId = c, decision = "Approved" } } })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/outside",
            new { decisions = new[] { new { userId = jurorId, registrationId = b, decision = "Approved" } } })).StatusCode);
        overview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jury");
        Assert.Equal("Approved", overview.GetProperty("outside")[0].GetProperty("decision").GetString());

        // Indienen: daarna kan het jurylid niets meer wijzigen.
        var submitted = await juror.PostAsync($"/api/v1/jury/parades/{_paradeId}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await juror.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores = new[] { Score(b, 1, "Overall", 50, t) } })).StatusCode);
        Assert.NotEqual(JsonValueKind.Null, (await juror.GetFromJsonAsync<JsonElement>("/api/v1/jury/current")).GetProperty("submittedAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, (await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/jury")).GetProperty("jurors")
            .EnumerateArray().Single(j => j.GetProperty("userId").GetGuid() == jurorId).GetProperty("submittedAt").ValueKind);
    }

    [Fact]
    public async Task Alleen_juryleden_jureren()
    {
        await SeedAsync();
        var (_, lidOid) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lidOid).GetAsync("/api/v1/jury/current")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _bestuur.GetAsync("/api/v1/jury/current")).StatusCode);
    }
}
