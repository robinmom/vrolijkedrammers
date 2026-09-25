using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Configuration;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary><c>GET /app-config</c> en <c>GET /carnival-years/current</c> tegen een echte database.</summary>
[Collection(SqlServerCollection.Name)]
public class PublicEndpointTests(SqlServerFixture sql) : IAsyncLifetime
{
    private string _connectionString = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _connectionString = await sql.CreateMigratedDatabaseAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Drammers", _connectionString);
            builder.UseSetting("Worker:Enabled", "false");
        });
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AppConfig_geeft_standaardwaarden()
    {
        using var body = JsonDocument.Parse(await _factory.CreateClient().GetStringAsync("/api/v1/app-config"));
        var root = body.RootElement;

        Assert.Equal("1.0.0", root.GetProperty("minAppVersion").GetProperty("ios").GetString());
        Assert.False(root.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Wijziging_in_de_database_is_zichtbaar_na_de_cacheduur_zonder_deploy()
    {
        Assert.True(AppConfigReader.CacheDuration <= TimeSpan.FromSeconds(60));
        var client = _factory.CreateClient();
        await client.GetStringAsync("/api/v1/app-config");

        await using (var connection = new SqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var update = new SqlCommand("UPDATE config.AppConfiguration SET value = 'true' WHERE [key] = 'maintenance_mode'", connection);
            await update.ExecuteNonQueryAsync();
        }

        // Cache verlopen laten (in productie na CacheDuration).
        ((MemoryCache)_factory.Services.GetRequiredService<IMemoryCache>()).Clear();

        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/app-config"));
        Assert.True(body.RootElement.GetProperty("maintenance").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task Huidig_carnavalsjaar()
    {
        var year = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/api/v1/carnival-years/current");

        Assert.Equal("2026/2027", year.GetProperty("name").GetString());
        Assert.Equal("2027-02-06", year.GetProperty("carnivalStartDate").GetString());
    }

    [Fact]
    public async Task Ready_controleert_de_database()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
