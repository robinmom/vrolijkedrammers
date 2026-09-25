using System.Reflection;
using Drammers.Api.Authorization;
using Drammers.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Drammers.ApiTests;

/// <summary>
/// Deny by default (docs/07 §1, §7): elk endpoint heeft een expliciete annotatie, en publieke endpoints staan hieronder
/// met naam opgesomd. Een nieuw publiek endpoint vraagt dus een bewuste wijziging van deze test.
/// </summary>
public class AuthorizationConventionTests
{
    private static readonly string[] ExpectedAnonymousEndpoints =
    [
        $"{nameof(AppConfigController)}.{nameof(AppConfigController.Get)}",
        $"{nameof(CarnivalYearsController)}.{nameof(CarnivalYearsController.GetCurrent)}",
        $"{nameof(PortalConfigController)}.{nameof(PortalConfigController.Get)}",
    ];

    public static TheoryData<string> Endpoints() => new(ApiEndpoints().Select(e => e.Name));

    [Theory]
    [MemberData(nameof(Endpoints))]
    public void Elk_endpoint_heeft_een_expliciete_autorisatie_annotatie(string endpoint)
    {
        var action = ApiEndpoints().Single(e => e.Name == endpoint).Method;

        Assert.True(
            Has<RequirePermissionAttribute>(action) || Has<RequireActiveUserAttribute>(action) || Has<AllowAnonymousAttribute>(action),
            $"{endpoint} heeft geen [RequirePermission], [RequireActiveUser] of [AllowAnonymous].");
    }

    [Fact]
    public void Publieke_endpoints_zijn_precies_de_verwachte()
    {
        var anonymous = ApiEndpoints().Where(e => Has<AllowAnonymousAttribute>(e.Method)).Select(e => e.Name).Order();

        Assert.Equal(ExpectedAnonymousEndpoints.Order(), anonymous);
    }

    internal static IEnumerable<(string Name, MethodInfo Method)> ApiEndpoints() =>
        typeof(MeController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => ($"{t.Name}.{m.Name}", m)));

    private static bool Has<TAttribute>(MethodInfo method)
        where TAttribute : Attribute =>
        method.GetCustomAttribute<TAttribute>() is not null || method.DeclaringType!.GetCustomAttribute<TAttribute>() is not null;
}
