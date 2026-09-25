using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.IntegrationTests.Infrastructure;
using Drammers.Modules.Identity.Provisioning;
using Drammers.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.IntegrationTests;

/// <summary>Fase 3: rollen toewijzen (direct effect + audit), blokkeren, lock-out-preventie en provisioning.</summary>
[Collection(SqlServerCollection.Name)]
public class RoleAdministrationTests(SqlServerFixture sql) : IAsyncLifetime
{
    private AuthenticatedApiFactory _api = null!;
    private HttpClient _admin = null!;
    private Guid _adminId;

    public async Task InitializeAsync()
    {
        _api = new AuthenticatedApiFactory(await sql.CreateMigratedDatabaseAsync());
        var (adminId, adminObjectId) = await _api.CreateUserAsync("admin@example.com", AuthenticatedApiFactory.Admin);
        _adminId = adminId;
        _admin = _api.ClientFor(adminObjectId);
    }

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task Rol_Redactie_toekennen_geeft_direct_news_manage_en_staat_in_de_auditlog()
    {
        var (userId, objectId) = await _api.CreateUserAsync("jan@example.com", DefaultRoles.Lid);
        var user = _api.ClientFor(objectId);
        Assert.DoesNotContain(Permissions.NewsManage, await PermissionsOfAsync(user));

        var response = await _admin.PutAsJsonAsync($"/api/v1/admin/users/{userId}/roles", new
        {
            roles = new[] { new { roleCode = DefaultRoles.Lid }, new { roleCode = DefaultRoles.Redactie } },
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Contains(Permissions.NewsManage, await PermissionsOfAsync(user));
        using var scope = _api.Services.CreateScope();
        var audit = await scope.ServiceProvider.GetRequiredService<DrammersDbContext>().AuditLog
            .SingleAsync(a => a.Action == "user.roles.changed" && a.EntityId == userId.ToString());
        Assert.Equal(_adminId, audit.ActorUserId);
        Assert.Contains(DefaultRoles.Redactie, audit.NewValues);
    }

    [Fact]
    public async Task Geblokkeerd_account_krijgt_403_en_sessies_worden_ingetrokken()
    {
        var (userId, objectId) = await _api.CreateUserAsync("piet@example.com", DefaultRoles.Lid);
        var user = _api.ClientFor(objectId);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/api/v1/admin/users/{userId}/block", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/me")).StatusCode);
        Assert.Contains(objectId, _api.Entra.RevokedSessions);
        Assert.False(_api.Entra.Enabled[objectId]);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"/api/v1/admin/users/{userId}/unblock", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Laatste_beheerder_kan_zijn_eigen_beheerrechten_niet_verwijderen()
    {
        var response = await _admin.PutAsJsonAsync($"/api/v1/admin/users/{_adminId}/roles", new { roles = new[] { new { roleCode = DefaultRoles.Lid } } });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("LOCKOUT_PREVENTED", body.RootElement.GetProperty("code").GetString());
        Assert.Contains(Permissions.RoleManage, await PermissionsOfAsync(_admin));
    }

    [Fact]
    public async Task Laatste_beheerder_kan_zichzelf_niet_blokkeren()
    {
        var response = await _admin.PostAsync($"/api/v1/admin/users/{_adminId}/block", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(_api.Entra.RevokedSessions);
    }

    [Fact]
    public async Task Systeemrol_kan_niet_worden_verwijderd_eigen_rol_wel()
    {
        var created = await _admin.PostAsJsonAsync("/api/v1/admin/roles", new
        {
            code = "penningmeester",
            name = "Penningmeester",
            description = (string?)null,
            permissions = new[] { Permissions.PaymentRead, Permissions.PaymentManage },
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var roleId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();

        Assert.Equal(HttpStatusCode.Conflict, (await _admin.DeleteAsync("/api/v1/admin/roles/11")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"/api/v1/admin/roles/{roleId}")).StatusCode);
    }

    [Fact]
    public async Task Provisioning_van_een_beheerder_is_idempotent()
    {
        var request = new { email = "Nieuwe.Beheerder@Example.com", displayName = "Nieuwe beheerder", roles = new[] { DefaultRoles.BeheerderIt } };

        var first = await _admin.PostAsJsonAsync("/api/v1/admin/users", request);
        var second = await _admin.PostAsJsonAsync("/api/v1/admin/users", request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(1, _api.Entra.CreateCalls);
        using var scope = _api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DrammersDbContext>();
        Assert.Equal(1, await db.Users.CountAsync(u => u.Email == "nieuwe.beheerder@example.com"));
        var saga = await db.AccountProvisioning.SingleAsync();
        Assert.Equal(ProvisioningStep.Completed, saga.Step);
    }

    [Fact]
    public async Task Bestaand_Entra_account_wordt_gekoppeld_in_plaats_van_dubbel_aangemaakt()
    {
        _api.Entra.AccountsByEmail["bestaand@example.com"] = "bestaande-oid";

        var response = await _admin.PostAsJsonAsync("/api/v1/admin/users", new { email = "bestaand@example.com", displayName = "Bestaand", roles = new[] { DefaultRoles.Bestuur } });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, _api.Entra.CreateCalls);
        using var scope = _api.Services.CreateScope();
        var access = await scope.ServiceProvider.GetRequiredService<IUserAccessService>().GetByExternalObjectIdAsync("bestaande-oid", default);
        Assert.Contains(Permissions.MemberApprove, access!.Permissions);
    }

    private static async Task<List<string?>> PermissionsOfAsync(HttpClient client)
    {
        using var body = JsonDocument.Parse(await client.GetStringAsync("/api/v1/me"));
        return [.. body.RootElement.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())];
    }
}
