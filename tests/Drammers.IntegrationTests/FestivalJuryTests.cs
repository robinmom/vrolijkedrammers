using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Content.Festival;
using Microsoft.Extensions.DependencyInjection;
using static Drammers.IntegrationTests.Infrastructure.FestivalSeed;

namespace Drammers.IntegrationTests;

/// <summary>Jury van het Dansgardefestival (2026-10-15): per categorie jureren en indienen, uitslag, hoofdjury en prijsuitreiking.</summary>
[Collection(SqlServerCollection.Name)]
public class FestivalJuryTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _org = null!;
    private Dictionary<string, Guid> _entries = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _org = _api.ClientFor((await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival)).ObjectId);
        using var scope = _api.Services.CreateScope();
        (_, _entries) = await SeedAsync(scope.ServiceProvider.GetRequiredService<DrammersDbContext>(),
            new("De Vrolijke Drammers", DanceCategory.Minioren, DanceType.Garde, DanceFormat.Group, "Mini Drammers"),
            new("DG Dynamite", DanceCategory.Minioren, DanceType.Garde, DanceFormat.Group, "Flames"),
            new("De Deurdouwers", DanceCategory.Minioren, DanceType.Garde, DanceFormat.Group, "ShowSparks"),
            new("De Deurdouwers", DanceCategory.Senioren, DanceType.Garde, DanceFormat.Solo, "Lynn Knuman"));
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<HttpClient> JurorAsync(string email, bool head = false)
    {
        var (_, oid) = await _api.CreateUserAsync(email, DefaultRoles.JuryDansgardefestival);
        if (head)
        {
            using var scope = _api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            var user = db.Users.Single(u => u.Email == email);
            var role = db.Roles.Single(r => r.Code == DefaultRoles.HoofdjuryDansgardefestival);
            db.UserRoles.Add(new Drammers.Modules.Identity.Users.UserRole { UserId = user.Id, RoleId = role.Id });
            await db.SaveChangesAsync();
        }

        return _api.ClientFor(oid);
    }

    /// <summary>Alle vier onderdelen van één dans; de waarden voor synchroniteit, choreografie, uitvoering en moeilijkheid.</summary>
    private Task<HttpResponseMessage> ScoreAsync(HttpClient juror, string dance, int sync, int choreo, int exec, int diff, string? comment = null) =>
        juror.PutAsJsonAsync("/api/v1/festival-jury/scores", new
        {
            scores = new[] { ("Synchronicity", sync), ("Choreography", choreo), ("Execution", exec), ("Difficulty", diff) }
                .Select(s => new { entryId = _entries[dance], criterion = s.Item1, value = s.Item2, scoredAt = DateTime.UtcNow }).ToArray(),
            comments = comment is null ? Array.Empty<object>() : [new { entryId = _entries[dance], text = comment }],
        });

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient juror, string key) =>
        juror.PostAsJsonAsync("/api/v1/festival-jury/submit", new { categoryKey = key });

    private const string Mini = "Minioren|Garde|Group|";
    private const string Solo = "Senioren|Garde|Solo|";

    [Fact]
    public async Task Per_categorie_jureren_indienen_uitslag_hoofdjury_en_prijsuitreiking()
    {
        var j1 = await JurorAsync("jurylid1@example.com", head: true);
        var j2 = await JurorAsync("jurylid2@example.com");

        // Alleen de categorie die aan de beurt is.
        var session = await j1.GetFromJsonAsync<JsonElement>("/api/v1/festival-jury");
        Assert.Equal(("Minioren Garde", 3), (session.GetProperty("current").GetProperty("name").GetString(), session.GetProperty("dances").GetArrayLength()));
        Assert.True(session.GetProperty("headJury").GetBoolean());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await ScoreAsync(j1, "Mini Drammers", 11, 8, 8, 8)).StatusCode);

        // Indienen pas als alle dansen vier onderdelen hebben.
        Assert.Equal(HttpStatusCode.NoContent, (await ScoreAsync(j1, "Mini Drammers", 8, 8, 9, 8, "Mooie lijnen")).StatusCode);
        var incomplete = await SubmitAsync(j1, Mini);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, incomplete.StatusCode);
        Assert.Contains("Flames", await incomplete.Content.ReadAsStringAsync());
        await ScoreAsync(j1, "Flames", 9, 8, 8, 8);
        await ScoreAsync(j1, "ShowSparks", 7, 7, 7, 7);
        Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(j1, Mini)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ScoreAsync(j1, "Flames", 1, 1, 1, 1)).StatusCode);
        session = await j1.GetFromJsonAsync<JsonElement>("/api/v1/festival-jury");
        Assert.Equal("Senioren Solo Garde", session.GetProperty("current").GetProperty("name").GetString());
        Assert.True(session.GetProperty("current").GetProperty("solo").GetBoolean());

        // Uitslag: Mini Drammers en Flames gelijk op totaal; Mini Drammers heeft de hoogste uitvoering.
        await ScoreAsync(j2, "Mini Drammers", 7, 7, 8, 7);
        await ScoreAsync(j2, "Flames", 7, 7, 7, 8);
        await ScoreAsync(j2, "ShowSparks", 6, 6, 6, 6);
        await SubmitAsync(j2, Mini);
        var results = await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/results");
        var mini = results.GetProperty("categories").EnumerateArray().Single(c => c.GetProperty("key").GetString() == Mini);
        Assert.True(mini.GetProperty("complete").GetBoolean());
        var rows = mini.GetProperty("rows").EnumerateArray().Select(r => (r.GetProperty("names").GetString(), r.GetProperty("place").GetInt32(), r.GetProperty("total").GetInt32())).ToArray();
        Assert.Equal([("Mini Drammers", 1, 62), ("Flames", 2, 62), ("ShowSparks", 3, 52)], rows);
        Assert.False(results.GetProperty("complete").GetBoolean());

        // Naar de hoofdjury: zonder force niet zolang de solo nog open is.
        Assert.Equal(HttpStatusCode.Conflict, (await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/results/head-jury", new { force = false })).StatusCode);
        Assert.False((await j1.GetFromJsonAsync<JsonElement>("/api/v1/festival-jury/head")).GetProperty("published").GetBoolean());
        foreach (var j in new[] { j1, j2 })
        {
            await ScoreAsync(j, "Lynn Knuman", 9, 9, 9, 9);
            Assert.Equal(HttpStatusCode.NoContent, (await SubmitAsync(j, Solo)).StatusCode);
        }

        Assert.Equal(JsonValueKind.Null, (await j1.GetFromJsonAsync<JsonElement>("/api/v1/festival-jury")).GetProperty("current").ValueKind);
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/results/head-jury", new { force = false })).StatusCode);

        // Hoofdjury: per deel, van de laatste plaats naar de winnaar.
        var head = await j1.GetFromJsonAsync<JsonElement>("/api/v1/festival-jury/head");
        var part1 = head.GetProperty("parts")[0];
        Assert.Equal("Deel 1 · Minioren", part1.GetProperty("name").GetString());
        Assert.Equal(["ShowSparks", "Flames", "Mini Drammers"], part1.GetProperty("categories")[0].GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("names").GetString()!).ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await j2.GetAsync("/api/v1/festival-jury/head")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await j1.PostAsJsonAsync("/api/v1/festival-jury/head/awards", new { completed = true })).StatusCode);
        results = await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/results");
        Assert.Equal("hoofdjury", results.GetProperty("awardsCompletedBy").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/results/awards", new { completed = false })).StatusCode);
        Assert.Equal(JsonValueKind.Null, (await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/results")).GetProperty("awardsCompletedAt").ValueKind);

        var pdf = await _org.GetAsync("/api/v1/admin/dance-festival/results/pdf");
        Assert.True(pdf.Content.Headers.ContentType?.MediaType == "application/pdf", await pdf.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Juryleden_uitnodigen_met_vast_nummer_en_hoofdjury()
    {
        await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur); // er blijft iemand die rollen beheert
        var merel = await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/jury", new { name = "Merel", email = "merel@example.com", headJury = true });
        Assert.True(merel.StatusCode == HttpStatusCode.Created, await merel.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Created, (await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/jury", new { name = "Demi", email = "demi@example.com" })).StatusCode);
        var jurors = await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/jury");
        Assert.Equal([("Merel", 1, true), ("Demi", 2, false)], jurors.EnumerateArray()
            .Select(j => (j.GetProperty("name").GetString()!, j.GetProperty("number").GetInt32(), j.GetProperty("headJury").GetBoolean())).ToArray());
        Assert.Contains(_api.Emails.Sent, m => m.To == "demi@example.com" && m.Subject.Contains("Dansgardefestival", StringComparison.Ordinal));

        var demi = jurors[1].GetProperty("userId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PutAsJsonAsync($"/api/v1/admin/dance-festival/jury/{demi}/head", new { headJury = true })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _org.DeleteAsync($"/api/v1/admin/dance-festival/jury/{demi}")).StatusCode);
        jurors = await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/jury");
        Assert.False(jurors[1].GetProperty("active").GetBoolean());

        var (_, lid) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lid).GetAsync("/api/v1/festival-jury")).StatusCode);
    }
}
