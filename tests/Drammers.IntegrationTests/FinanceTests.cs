using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Identifiers;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 4c: Financieel — het totaaloverzicht en de statistieken per carnavalsjaar over alle inkomstenbronnen.</summary>
[Collection(SqlServerCollection.Name)]
public class FinanceTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur-financieel@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private static decimal SourceTotal(JsonElement report, string source) =>
        report.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("source").GetString() == source).GetProperty("total").GetDecimal();

    private static string[] SourceNames(JsonElement report) =>
        [.. report.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("source").GetString()!)];

    [Fact]
    public async Task Totaaloverzicht_splitst_de_bronnen_zonder_contributie()
    {
        var overview = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/overview");

        Assert.Equal("2026/2027", overview.GetProperty("year").GetString());
        Assert.Equal(["Adverteerders", "Dansgardefestival", "Pronkzitting", "Dagkaarten", "Activiteiten", "Munten"], SourceNames(overview));
        // Zonder gegevens is alles nul, maar belangrijker: alle onderliggende queries draaien echt tegen de database.
        Assert.Equal(0m, overview.GetProperty("total").GetDecimal());
    }

    [Fact]
    public async Task Statistieken_tellen_de_contributie_van_het_seizoen_mee()
    {
        // Een geïnde contributie-run binnen het seizoen (2026-11-11 t/m 2027-02-10) telt mee voor carnavalsjaar 2026/2027.
        using (var scope = _api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
            db.CollectionRuns.Add(new CollectionRun
            {
                Id = IdGenerator.NewId(),
                Kind = CollectionKind.Contribution,
                CollectionDate = new DateOnly(2026, 12, 15),
                Description = "Contributie-incasso december",
                MessageId = "DVD-TEST-CONTRIB-1",
                LineCount = 3,
                Total = 250m,
            });
            await db.SaveChangesAsync();
        }

        var stats = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/statistics");

        Assert.Equal("2026/2027", stats.GetProperty("year").GetString());
        Assert.Equal(["Adverteerders", "Dansgardefestival", "Pronkzitting", "Dagkaarten", "Activiteiten", "Munten", "Contributie"], SourceNames(stats));
        Assert.Equal(250m, SourceTotal(stats, "Contributie"));
        Assert.Equal(250m, stats.GetProperty("total").GetDecimal());
        Assert.NotEmpty(stats.GetProperty("perYear").EnumerateArray());
    }

    [Fact]
    public async Task Festivalfacturen_zijn_leeg_zonder_betaalverzoeken()
    {
        var invoices = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/finance/festival-invoices");

        Assert.Equal(JsonValueKind.Array, invoices.ValueKind);
        Assert.Empty(invoices.EnumerateArray());
    }

    [Fact]
    public async Task Zonder_recht_geen_toegang()
    {
        var redactie = _api.ClientFor((await _api.CreateUserAsync("redactie-financieel@example.com", DefaultRoles.Redactie)).ObjectId);

        var response = await redactie.GetAsync("/api/v1/admin/finance/overview");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
