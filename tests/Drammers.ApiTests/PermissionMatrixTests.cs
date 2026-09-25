using System.Reflection;
using System.Security.Claims;
using Drammers.Api.Authentication;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.ApiTests;

/// <summary>
/// Permission-matrix (docs/07 §7): voor elk beschermd endpoint × elke standaardrol volgt het verwachte resultaat uit de
/// rolmatrix in de seed. Draait de echte policy provider en handlers; nieuwe endpoints komen er vanzelf bij.
/// </summary>
public class PermissionMatrixTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (endpoint, _) in ProtectedEndpoints())
        {
            foreach (var role in DefaultRoles.All)
            {
                data.Add(endpoint, role.Code);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Rol_heeft_alleen_toegang_met_de_juiste_permission(string endpoint, string roleCode)
    {
        var permissions = ProtectedEndpoints().Single(e => e.Name == endpoint).Permissions;
        var role = DefaultRoles.All.Single(r => r.Code == roleCode);
        var expected = permissions.All(role.Permissions.Contains);

        var allowed = await AuthorizeAsync(role, permissions, AccountStatus.Active);

        Assert.Equal(expected, allowed);
    }

    [Fact]
    public async Task Geblokkeerd_account_krijgt_nergens_toegang_ook_niet_als_bestuur()
    {
        var bestuur = DefaultRoles.All.Single(r => r.Code == DefaultRoles.Bestuur);

        Assert.False(await AuthorizeAsync(bestuur, [Permissions.RoleManage], AccountStatus.Blocked));
    }

    [Fact]
    public void Elke_permission_in_de_matrix_bestaat_in_de_catalogus()
    {
        var catalogue = Permissions.All.Select(p => p.Code).ToHashSet();

        Assert.All(DefaultRoles.All.SelectMany(r => r.Permissions), code => Assert.Contains(code, catalogue));
    }

    [Fact]
    public void Bestuur_en_beheerder_kunnen_rollen_beheren_zodat_er_altijd_een_beheerder_mogelijk_is()
    {
        Assert.All(
            DefaultRoles.All.Where(r => r.Code is DefaultRoles.Bestuur or DefaultRoles.BeheerderIt),
            r => Assert.Contains(Permissions.RoleManage, r.Permissions));
    }

    private static IEnumerable<(string Name, string[] Permissions)> ProtectedEndpoints() =>
        AuthorizationConventionTests.ApiEndpoints()
            .Select(e => (e.Name, Permissions: e.Method.GetCustomAttributes<RequirePermissionAttribute>()
                .Concat(e.Method.DeclaringType!.GetCustomAttributes<RequirePermissionAttribute>())
                .Select(a => a.Permission).Distinct().ToArray()))
            .Where(e => e.Permissions.Length > 0);

    private static async Task<bool> AuthorizeAsync(RoleDefinition role, IEnumerable<string> permissions, AccountStatus status)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<AuthOptions>(_ => { });
        services.AddDrammersAuthorization();
        var user = new UserAccess(Guid.NewGuid(), "oid-1", "test@example.com", "Test", null, status, 1,
            [new RoleSummary(role.Code, role.Name)], role.Permissions.ToHashSet());
        services.AddSingleton<IUserAccessService>(new SingleUserAccess(user));
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "oid-1")], "Bearer"));
        var httpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider, User = principal };
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext = httpContext;

        var authorization = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        foreach (var permission in permissions)
        {
            var result = await authorization.AuthorizeAsync(principal, null, PermissionPolicyProvider.PolicyPrefix + permission);
            if (!result.Succeeded)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class SingleUserAccess(UserAccess user) : IUserAccessService
    {
        public Task<UserAccess?> GetByExternalObjectIdAsync(string externalObjectId, CancellationToken cancellationToken) =>
            Task.FromResult<UserAccess?>(externalObjectId == user.ExternalObjectId ? user : null);

        public void Invalidate(string externalObjectId)
        {
        }
    }
}
