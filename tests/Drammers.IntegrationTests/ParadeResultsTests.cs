using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Parade.Parades;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Identifiers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 22c: de uitslag — rekenregel, alleen voor de uitslagcommissie, Excel en publiceren na de prijsuitreiking.</summary>
[Collection(SqlServerCollection.Name)]
public class ParadeResultsTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;
    private HttpClient _uitslag = null!;
    private Guid _paradeId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), await sql.CreateBlobStorageAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
        _uitslag = _api.ClientFor((await _api.CreateUserAsync("uitslag@example.com", DefaultRoles.Uitslagcommissie)).ObjectId);
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

    private static object Scores(Guid registration, int pass, int originality, int carnivalesque, int quality, int overall, DateTimeOffset at) => new[]
    {
        new { registrationId = registration, pass, criterion = "Originality", value = originality, scoredAt = at.UtcDateTime },
        new { registrationId = registration, pass, criterion = "Carnivalesque", value = carnivalesque, scoredAt = at.UtcDateTime },
        new { registrationId = registration, pass, criterion = "Quality", value = quality, scoredAt = at.UtcDateTime },
        new { registrationId = registration, pass, criterion = "Overall", value = overall, scoredAt = at.UtcDateTime },
    };

    [Fact]
    public async Task Uitslag_volgens_de_rekenregel_pas_compleet_na_indienen_en_publiceren_na_de_prijsuitreiking()
    {
        Guid a, b, c;
        int wagons, groups;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            var categories = await db.ParadeCategories.OrderBy(x => x.SortOrder).ToListAsync();
            (wagons, groups) = (categories.First(x => x.HasVehicle).Id, categories.First(x => !x.HasVehicle).Id);
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
            var entries = new[] { Entry(1, 5, wagons, "De Snotapen"), Entry(2, 6, wagons, "De Beunhazen"), Entry(3, 7, groups, "DwarZ") };
            db.ParadeRegistrations.AddRange(entries);
            await db.SaveChangesAsync();
            (a, b, c) = (entries[0].Id, entries[1].Id, entries[2].Id);
        }

        // Standaardweging wagens: kwaliteit 2x. J1 en J2 jureren wagens, J3 loopgroepen; J3 jureert ook wagen A (buiten categorie).
        await _bestuur.GetAsync("/api/v1/admin/jury");
        var jurors = new List<(Guid Id, HttpClient Client)>();
        foreach (var (email, category) in new[] { ("j1@example.com", wagons), ("j2@example.com", wagons), ("j3@example.com", groups) })
        {
            var (id, oid) = await _api.CreateUserAsync(email, DefaultRoles.Jury);
            await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/jurors/{id}/categories", new { categoryIds = new[] { category } });
            jurors.Add((id, _api.ClientFor(oid)));
        }

        var t = _api.Clock.UtcNow;
        async Task Save(int juror, object scores) =>
            Assert.True((await jurors[juror].Client.PutAsJsonAsync($"/api/v1/jury/parades/{_paradeId}/scores", new { scores })).IsSuccessStatusCode);
        // J1: wagen A passage 1 = 80, passage 2 = 70 (gemiddeld 75); B = 60. J2: A = 90, B = 70. J3: C = 50 en A = 50 (buiten categorie).
        await Save(0, ((object[])Scores(a, 1, 80, 80, 80, 80, t)).Concat((object[])Scores(a, 2, 70, 70, 70, 70, t)).Concat((object[])Scores(b, 1, 60, 60, 60, 60, t)).ToArray());
        await Save(1, ((object[])Scores(a, 1, 90, 90, 90, 90, t)).Concat((object[])Scores(b, 1, 70, 70, 70, 70, t)).ToArray());
        await Save(2, ((object[])Scores(c, 1, 50, 50, 50, 50, t)).Concat((object[])Scores(a, 1, 50, 50, 50, 50, t)).ToArray());

        // Alleen de uitslagcommissie; vóór indienen nog geen uitslag en niets openbaar.
        Assert.Equal(HttpStatusCode.Forbidden, (await _bestuur.GetAsync("/api/v1/admin/results")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.CreateClient().GetAsync("/api/v1/parade/results")).StatusCode);
        var overview = await _uitslag.GetFromJsonAsync<JsonElement>("/api/v1/admin/results");
        var wagonResult = overview.GetProperty("categories").EnumerateArray().Single(x => x.GetProperty("categoryId").GetInt32() == wagons);
        Assert.False(wagonResult.GetProperty("ready").GetBoolean());
        Assert.Empty(wagonResult.GetProperty("rows").EnumerateArray());

        foreach (var (_, client) in jurors)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/jury/parades/{_paradeId}/submit", null)).StatusCode);
        }

        // A: per criterium 75 + 90 = 165 (buiten categorie telt nog niet), kwaliteit 2x → 165·4 + 165 = 825. B: 130·5 = 650.
        overview = await _uitslag.GetFromJsonAsync<JsonElement>("/api/v1/admin/results");
        wagonResult = overview.GetProperty("categories").EnumerateArray().Single(x => x.GetProperty("categoryId").GetInt32() == wagons);
        Assert.Equal((2, 1000), (wagonResult.GetProperty("jurors").GetInt32(), wagonResult.GetProperty("maxPoints").GetInt32()));
        var rows = wagonResult.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal([("De Snotapen", 1, 825m), ("De Beunhazen", 2, 650m)],
            rows.Select(r => (r.GetProperty("groupName").GetString(), r.GetProperty("place").GetInt32(), r.GetProperty("total").GetDecimal())));
        Assert.Equal(330m, rows[0].GetProperty("quality").GetDecimal());

        // Akkoord op de beoordeling buiten categorie: J3 telt mee voor A (50 per criterium erbij → 825 + 250 = 1075).
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/outside",
            new { decisions = new[] { new { userId = jurors[2].Id, registrationId = a, decision = "Approved" } } });
        overview = await _uitslag.GetFromJsonAsync<JsonElement>("/api/v1/admin/results");
        Assert.Equal(1075m, overview.GetProperty("categories").EnumerateArray().Single(x => x.GetProperty("categoryId").GetInt32() == wagons)
            .GetProperty("rows")[0].GetProperty("total").GetDecimal());

        // Excel: uitslag per categorie en de zaallijst (laatste plaats eerst).
        foreach (var kind in new[] { "Uitslag", "Zaallijst" })
        {
            var export = await _uitslag.GetAsync($"/api/v1/admin/results/export?kind={kind}");
            Assert.Equal(HttpStatusCode.OK, export.StatusCode);
            using var workbook = new XLWorkbook(await export.Content.ReadAsStreamAsync());
            var sheet = workbook.Worksheets.First();
            Assert.Equal(kind == "Uitslag" ? "De Snotapen" : "De Beunhazen", sheet.Cell(5, kind == "Uitslag" ? 3 : 4).GetString());
        }

        // Publiceren: alleen na de prijsuitreiking; daarna openbaar, optocht afgerond en een mail aan secretaris en voorzitter.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await _uitslag.PostAsJsonAsync("/api/v1/admin/results/publish", new { paradeId = _paradeId, prizeCeremonyHeld = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _api.CreateClient().GetAsync("/optocht/uitslag")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _uitslag.PostAsJsonAsync("/api/v1/admin/results/publish", new { paradeId = _paradeId, prizeCeremonyHeld = true })).StatusCode);
        Assert.Contains(_api.Emails.Sent, m => m.To == "secretaris@vrolijkedrammers.nl" && m.Subject.Contains("gepubliceerd", StringComparison.Ordinal));
        Assert.Contains(_api.Emails.Sent, m => m.To == "voorzitter@vrolijkedrammers.nl");

        var published = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/parade/results");
        var publicWagons = published.GetProperty("categories").EnumerateArray().First();
        Assert.Equal("De Snotapen", publicWagons.GetProperty("rows")[0].GetProperty("groupName").GetString());
        Assert.False(publicWagons.GetProperty("rows")[0].TryGetProperty("quality", out _));
        Assert.Contains("De Snotapen", await (await _api.CreateClient().GetAsync("/optocht/uitslag")).Content.ReadAsStringAsync());
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            Assert.Equal(ParadeStatus.Completed, await db.Parades.Where(p => p.Id == _paradeId).Select(p => p.Status).SingleAsync());
        }
    }

    [Fact]
    public async Task Publiceren_kan_niet_zolang_een_categorie_niet_compleet_is()
    {
        int wagons;
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            wagons = (await db.ParadeCategories.OrderBy(x => x.SortOrder).FirstAsync(x => x.HasVehicle)).Id;
            db.ParadeRegistrations.Add(new ParadeRegistration
            {
                Id = IdGenerator.NewId(),
                ParadeId = _paradeId,
                CarnivalYearId = 1,
                RegistrationNumber = 1,
                StartNumber = 5,
                CategoryId = wagons,
                GroupName = "De Snotapen",
                Status = RegistrationStatus.Approved,
                Source = RegistrationSource.App,
            });
            await db.SaveChangesAsync();
        }

        var (id, _) = await _api.CreateUserAsync("j1@example.com", DefaultRoles.Jury);
        await _bestuur.PutAsJsonAsync($"/api/v1/admin/jury/parades/{_paradeId}/jurors/{id}/categories", new { categoryIds = new[] { wagons } });
        var refused = await _uitslag.PostAsJsonAsync("/api/v1/admin/results/publish", new { paradeId = _paradeId, prizeCeremonyHeld = true });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("Nog niet alle juryleden", await refused.Content.ReadAsStringAsync());
    }
}
