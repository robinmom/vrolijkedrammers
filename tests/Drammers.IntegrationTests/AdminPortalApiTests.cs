using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClosedXML.Excel;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 4: API-kant van het beheerportal.</summary>
[Collection(SqlServerCollection.Name)]
public class AdminPortalApiTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        var (_, objectId) = await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur);
        _bestuur = _api.ClientFor(objectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Carnavalsjaar_aanmaken_en_activeren_houdt_precies_één_actief_jaar()
    {
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/carnival-years", new
        {
            name = "2027/2028",
            startDate = "2027-11-11",
            endDate = "2028-03-01",
            carnivalStartDate = "2028-02-26",
            carnivalEndDate = "2028-02-29",
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = body.GetProperty("id").GetInt32();
        Assert.False(body.GetProperty("active").GetBoolean());

        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PostAsync($"/api/v1/admin/carnival-years/{id}/activate", null)).StatusCode);

        var years = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/carnival-years");
        Assert.Single(years!, y => y.GetProperty("active").GetBoolean());
        Assert.Equal("2027/2028", (await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/carnival-years/current")).GetProperty("name").GetString());
    }

    [Fact]
    public async Task Ongeldige_datums_geven_422()
    {
        var response = await _bestuur.PostAsJsonAsync("/api/v1/admin/carnival-years", new
        {
            name = "2029/2030",
            startDate = "2029-11-11",
            endDate = "2030-03-01",
            carnivalStartDate = "2030-03-10",
            carnivalEndDate = "2030-03-12",
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Onderhoudsmodus_aanzetten_is_direct_zichtbaar_in_de_publieke_app_config()
    {
        var response = await _bestuur.PutAsJsonAsync("/api/v1/admin/config/app-config", new
        {
            minAppVersionIos = "1.2.0",
            minAppVersionAndroid = "1.2.0",
            recommendedAppVersion = "1.3.0",
            maintenanceMode = true,
            maintenanceMessage = "Even geduld",
            supportEmail = (string?)null,
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var config = await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/app-config");
        Assert.True(config.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
        Assert.Equal("1.2.0", config.GetProperty("minAppVersion").GetProperty("ios").GetString());
    }

    [Fact]
    public async Task Feature_flag_zetten_en_bewaartermijn_wijzigen()
    {
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/config/feature-flags/nieuws.push", new { enabled = true, description = "Push bij nieuws" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _bestuur.PutAsJsonAsync("/api/v1/admin/config/retention/login_history", new { retentionDays = 180, action = "Delete" })).StatusCode);

        var flags = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/config/feature-flags");
        Assert.Contains(flags!, f => f.GetProperty("key").GetString() == "nieuws.push" && f.GetProperty("enabled").GetBoolean());
        var features = (await _api.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/app-config")).GetProperty("features");
        Assert.True(features.GetProperty("nieuws.push").GetBoolean());
        var retention = await _bestuur.GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/config/retention");
        Assert.Equal(180, retention!.Single(r => r.GetProperty("dataType").GetString() == "login_history").GetProperty("retentionDays").GetInt32());
    }

    [Fact]
    public async Task Auditlog_toont_wijzigingen_met_gemaskeerde_gevoelige_velden()
    {
        await _bestuur.PutAsJsonAsync("/api/v1/admin/config/app-config", new
        {
            minAppVersionIos = "1.0.0",
            minAppVersionAndroid = "1.0.0",
            recommendedAppVersion = "1.0.0",
            maintenanceMode = false,
            maintenanceMessage = (string?)null,
            supportEmail = "info@example.com",
        });

        var page = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/audit-log?action=config.&pageSize=10");

        var entry = page.GetProperty("items")[0];
        Assert.Equal("config.app-config.changed", entry.GetProperty("action").GetString());
        Assert.Equal("bestuur@example.com", entry.GetProperty("actorName").GetString());
        Assert.DoesNotContain("info@example.com", entry.GetProperty("newValues").GetString());
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task Export_van_gebruikers_is_een_Excel_met_Nederlandse_kolommen_en_wordt_geaudit()
    {
        var response = await _bestuur.GetAsync("/api/v1/admin/users/export");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var workbook = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var sheet = workbook.Worksheet("Gebruikers");
        Assert.Equal("Naam", sheet.Cell(1, 1).GetString());
        Assert.Equal("E-mailadres", sheet.Cell(1, 2).GetString());
        Assert.Equal("bestuur@example.com", sheet.Cell(2, 2).GetString());
        Assert.Equal("Actief", sheet.Cell(2, 3).GetString());
        using var scope = _api.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().AuditLog.AnyAsync(a => a.Action == "user.exported"));
    }

    [Fact]
    public async Task Gebruikerslijst_is_gepagineerd()
    {
        var page = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/users?page=1&pageSize=1");

        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task Dashboard_geeft_kerncijfers_en_systeemstatus()
    {
        var dashboard = await _bestuur.GetFromJsonAsync<JsonElement>("/api/v1/admin/dashboard");

        Assert.Equal(1, dashboard.GetProperty("activeUsers").GetInt32());
        Assert.Equal("2026/2027", dashboard.GetProperty("carnivalYear").GetString());
        Assert.Equal("Healthy", dashboard.GetProperty("systemStatus").GetString());
    }

    [Fact]
    public async Task Lid_zonder_beheerrechten_krijgt_403_op_beheer_api()
    {
        var (_, objectId) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);
        var lid = _api.ClientFor(objectId);

        foreach (var url in new[] { "/api/v1/admin/users", "/api/v1/admin/audit-log", "/api/v1/admin/config/app-config", "/api/v1/admin/carnival-years", "/api/v1/admin/dashboard" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await lid.GetAsync(url)).StatusCode);
        }
    }
}
