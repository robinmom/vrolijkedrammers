using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Knop "Toegang tot testomgeving" in het portal (B-02): groep Testers + environmentAccess, zonder set-tester.sh.</summary>
[Collection(SqlServerCollection.Name)]
public class TestAccessTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _bestuur = null!;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync(), configure: services => services.PostConfigure<GraphOptions>(o =>
        {
            o.TenantId = "tenant";
            o.ClientId = "client";
            o.IssuerDomain = "example.onmicrosoft.com";
            o.CertificateName = "graph-provisioning";
            o.TestersGroupId = "testers";
            o.EnvironmentAccessAttribute = "extension_abc_environmentAccess";
        }));
        _bestuur = _api.ClientFor((await _api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur)).ObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<JsonElement> StatusAsync(Guid userId) =>
        await _bestuur.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}/test-access");

    [Fact]
    public async Task Toegang_geven_na_het_aanmaken_van_een_inlog_en_weer_intrekken()
    {
        var created = await _bestuur.PostAsJsonAsync("/api/v1/admin/users", new { email = "tester@example.com", displayName = "Tes Ter", roles = new[] { DefaultRoles.Lid } });
        var userId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var before = await StatusAsync(userId);
        Assert.Equal((true, false, "dev"), (before.GetProperty("available").GetBoolean(), before.GetProperty("hasSignIn").GetBoolean(), before.GetProperty("environment").GetString()));
        var noSignIn = await _bestuur.PutAsJsonAsync($"/api/v1/admin/users/{userId}/test-access", new { granted = true });
        Assert.Equal(HttpStatusCode.Conflict, noSignIn.StatusCode);

        var objectId = _api.Entra.SignUp("tester@example.com");
        var granted = await (await _bestuur.PutAsJsonAsync($"/api/v1/admin/users/{userId}/test-access", new { granted = true })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(granted.GetProperty("hasAccessHere").GetBoolean());
        Assert.Equal("dev", _api.Entra.TestAccess[objectId]);

        var revoked = await (await _bestuur.PutAsJsonAsync($"/api/v1/admin/users/{userId}/test-access", new { granted = false })).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal((false, false), (revoked.GetProperty("hasAccessHere").GetBoolean(), revoked.GetProperty("inTestersGroup").GetBoolean()));
        Assert.Contains(objectId, _api.Entra.RevokedSessions);
    }

    [Fact]
    public async Task Alleen_met_role_manage()
    {
        var (userId, objectId) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);

        var response = await _api.ClientFor(objectId).PutAsJsonAsync($"/api/v1/admin/users/{userId}/test-access", new { granted = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(_api.Entra.TestAccess);
    }

    [Fact]
    public async Task Zonder_instellingen_is_de_knop_niet_beschikbaar()
    {
        await using var api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        var (userId, objectId) = await api.CreateUserAsync("bestuur@example.com", DefaultRoles.Bestuur);
        var client = api.ClientFor(objectId);

        var status = await client.GetFromJsonAsync<JsonElement>($"/api/v1/admin/users/{userId}/test-access");
        var set = await client.PutAsJsonAsync($"/api/v1/admin/users/{userId}/test-access", new { granted = true });

        Assert.False(status.GetProperty("available").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, set.StatusCode);
    }
}
