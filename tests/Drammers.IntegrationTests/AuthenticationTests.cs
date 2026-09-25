using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 3: tokenvalidatie (401) versus accountcontrole (403), <c>/me</c> en aanmeldhistorie.</summary>
[Collection(SqlServerCollection.Name)]
public class AuthenticationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;

    public async Task InitializeAsync() => _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Bekende_gebruiker_ziet_rollen_en_permissions_via_me()
    {
        var (_, objectId) = await _api.CreateUserAsync("lid@example.com", DefaultRoles.Lid);

        using var body = JsonDocument.Parse(await _api.ClientFor(objectId).GetStringAsync("/api/v1/me"));

        Assert.Equal("lid", body.RootElement.GetProperty("roles")[0].GetProperty("code").GetString());
        var permissions = body.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).ToList();
        Assert.Contains(Permissions.ParadeRegister, permissions);
        Assert.DoesNotContain(Permissions.RoleManage, permissions);
    }

    [Fact]
    public async Task Onbekende_oid_krijgt_403_en_wordt_als_mislukte_aanmelding_vastgelegd()
    {
        var response = await _api.ClientFor(Guid.NewGuid().ToString()).GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = _api.Services.CreateScope();
        var history = await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().LoginHistory.SingleAsync();
        Assert.Equal(LoginResult.Failed, history.Result);
        Assert.Equal("unknown-account", history.Reason);
        Assert.Null(history.UserId);
        Assert.NotNull(history.SubjectHash);
    }

    [Fact]
    public async Task Geslaagde_aanmelding_wordt_één_keer_per_token_vastgelegd()
    {
        var (userId, objectId) = await _api.CreateUserAsync("lid2@example.com", DefaultRoles.Lid);
        var client = _api.ClientFor(objectId);

        await client.GetAsync("/api/v1/me");
        await client.GetAsync("/api/v1/me");

        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        Assert.Equal(1, await db.LoginHistory.CountAsync(l => l.UserId == userId && l.Result == LoginResult.Success));
        Assert.NotNull((await db.Users.SingleAsync(u => u.Id == userId)).LastLoginAt);
    }

    [Fact]
    public async Task Token_zonder_environmentAccess_krijgt_403_in_Dev()
    {
        var (_, objectId) = await _api.CreateUserAsync("tester@example.com", DefaultRoles.Lid);

        var response = await _api.ClientFor(objectId, environmentAccess: "acc").GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public static TheoryData<string, string> InvalidTokens() => new()
    {
        { "prod-audience", TestTokens.Create("oid", audience: TestTokens.ProdAudience) },
        { "verlopen", TestTokens.Create("oid", expires: DateTime.UtcNow.AddMinutes(-10)) },
        { "andere-issuer", TestTokens.Create("oid", issuer: "https://evil.example/v2.0") },
    };

    [Theory]
    [MemberData(nameof(InvalidTokens))]
    public async Task Ongeldig_token_krijgt_401(string reason, string token)
    {
        var client = _api.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/v1/me");

        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, reason);
    }

    [Fact]
    public async Task Zonder_role_manage_geen_toegang_tot_rollenbeheer()
    {
        var (_, objectId) = await _api.CreateUserAsync("redactie@example.com", DefaultRoles.Redactie);

        var response = await _api.ClientFor(objectId).GetAsync("/api/v1/admin/roles");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Beheerder_ziet_de_permissiecatalogus()
    {
        var (_, objectId) = await _api.CreateUserAsync("admin@example.com", AuthenticatedApiFactory.Admin);

        var permissions = await _api.ClientFor(objectId).GetFromJsonAsync<List<JsonElement>>("/api/v1/admin/permissions");

        Assert.Equal(Permissions.All.Count, permissions!.Count);
    }
}
