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

/// <summary>Programma van het Dansgardefestival (2026-10-15): categorieën uit de goedgekeurde dansen, tijden, blokken, schuiven en loten.</summary>
[Collection(SqlServerCollection.Name)]
public class FestivalProgramTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _org = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _org = _api.ClientFor((await _api.CreateUserAsync("chantal@example.com", DefaultRoles.Dansgardefestival)).ObjectId);
        using var scope = _api.Services.CreateScope();
        await SeedAsync(scope.ServiceProvider.GetRequiredService<DrammersDbContext>(),
            new("De Deurdouwers", DanceCategory.Senioren, DanceType.Garde, DanceFormat.Solo, "Lynn Knuman"),
            new("De Deurdouwers", DanceCategory.Junioren, DanceType.Garde, DanceFormat.Group, "ShowStars"),
            new("De Vrolijke Drammers", DanceCategory.Junioren, DanceType.Garde, DanceFormat.Group, "Junioren Drammers"),
            new("DG Dynamite", DanceCategory.Junioren, DanceType.Show, DanceFormat.Duo, "Naomy & Fenna", ShowStyle.Character, "Pettycoat"),
            new("De Vrolijke Drammers", DanceCategory.Minioren, DanceType.Garde, DanceFormat.Group, "Mini Drammers"));
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<JsonElement> ProgramAsync() => await _org.GetFromJsonAsync<JsonElement>("/api/v1/admin/dance-festival/program");

    private static string[] Titles(JsonElement program) => [.. program.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()!)];

    private static JsonElement Item(JsonElement program, string title) => program.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("title").GetString() == title);

    [Fact]
    public async Task Categorieen_in_standaardvolgorde_met_aansluitende_tijden()
    {
        var program = await ProgramAsync();
        Assert.Equal(["Opening", "Minioren Garde", "Junioren Garde", "Senioren Solo Garde", "Junioren Duo Karakter", "Prijsuitreiking"], Titles(program));
        Assert.Equal(("10:30", "10:35"), (Item(program, "Opening").GetProperty("start").GetString(), Item(program, "Opening").GetProperty("end").GetString()));
        var junioren = Item(program, "Junioren Garde");
        Assert.Equal(("10:39", "10:47"), (junioren.GetProperty("start").GetString(), junioren.GetProperty("end").GetString()));
        Assert.Equal(["10:39", "10:43"], junioren.GetProperty("dances").EnumerateArray().Select(d => d.GetProperty("time").GetString()!).ToArray());
        Assert.True(Item(program, "Senioren Solo Garde").GetProperty("solo").GetBoolean());
        Assert.Equal(5, Item(program, "Senioren Solo Garde").GetProperty("minutes").GetInt32());

        // Pauze na Junioren Garde, starttijd met de hand, minuten per dans aanpassen.
        var juniorenId = junioren.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await _org.PostAsJsonAsync("/api/v1/admin/dance-festival/program/blocks", new { title = "Pauze", minutes = 15, afterItemId = juniorenId })).StatusCode);
        var solo = Item(await ProgramAsync(), "Senioren Solo Garde").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PutAsJsonAsync($"/api/v1/admin/dance-festival/program/items/{solo}", new { start = "11.30", minutes = 6 })).StatusCode);
        program = await ProgramAsync();
        Assert.Equal(("10:47", "11:02"), (Item(program, "Pauze").GetProperty("start").GetString(), Item(program, "Pauze").GetProperty("end").GetString()));
        Assert.Equal(("11:30", "11:36", true), (Item(program, "Senioren Solo Garde").GetProperty("start").GetString(),
            Item(program, "Senioren Solo Garde").GetProperty("end").GetString(), Item(program, "Senioren Solo Garde").GetProperty("startOverridden").GetBoolean()));
        Assert.Equal("11:36", Item(program, "Junioren Duo Karakter").GetProperty("start").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _org.PutAsJsonAsync($"/api/v1/admin/dance-festival/program/items/{solo}", new { start = "25:99", minutes = 6 })).StatusCode);
    }

    [Fact]
    public async Task Schuiven_loten_en_afdrukken()
    {
        var program = await ProgramAsync();
        var duo = Item(program, "Junioren Duo Karakter").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PostAsJsonAsync($"/api/v1/admin/dance-festival/program/items/{duo}/move", new { direction = -1 })).StatusCode);
        Assert.Equal("Junioren Duo Karakter", Titles(await ProgramAsync())[3]);

        var junioren = Item(program, "Junioren Garde").GetProperty("dances").EnumerateArray().ToList();
        var second = junioren[1].GetProperty("entryId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await _org.PostAsJsonAsync($"/api/v1/admin/dance-festival/program/dances/{second}/move", new { direction = -1 })).StatusCode);
        Assert.Equal(second, Item(await ProgramAsync(), "Junioren Garde").GetProperty("dances")[0].GetProperty("entryId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await _org.PostAsync("/api/v1/admin/dance-festival/program/draw", null)).StatusCode);
        program = await ProgramAsync();
        Assert.NotEqual(JsonValueKind.Null, program.GetProperty("drawnAt").ValueKind);
        Assert.Equal(2, Item(program, "Junioren Garde").GetProperty("dances").GetArrayLength());

        // Een categorie kan niet weg; een blok wel.
        Assert.Equal(HttpStatusCode.Conflict, (await _org.DeleteAsync($"/api/v1/admin/dance-festival/program/items/{duo}")).StatusCode);
        var pdf = await _org.GetAsync("/api/v1/admin/dance-festival/program/pdf");
        Assert.True(pdf.Content.Headers.ContentType?.MediaType == "application/pdf", await pdf.Content.ReadAsStringAsync());
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..4]));

        var (_, lid) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        Assert.Equal(HttpStatusCode.Forbidden, (await _api.ClientFor(lid).GetAsync("/api/v1/admin/dance-festival/program")).StatusCode);
    }
}
