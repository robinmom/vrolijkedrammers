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
        $"{nameof(CarnivalYearsController)}.{nameof(CarnivalYearsController.List)}",
        $"{nameof(PortalConfigController)}.{nameof(PortalConfigController.Get)}",
        $"{nameof(EventsController)}.{nameof(EventsController.GetCategories)}",
        $"{nameof(EventsController)}.{nameof(EventsController.Search)}",
        $"{nameof(EventsController)}.{nameof(EventsController.Get)}",
        $"{nameof(EventsController)}.{nameof(EventsController.Ical)}",
        $"{nameof(NewsController)}.{nameof(NewsController.Search)}",
        $"{nameof(NewsController)}.{nameof(NewsController.Get)}",
        $"{nameof(NewsController)}.{nameof(NewsController.Seasons)}",
        $"{nameof(PhotoAlbumsController)}.{nameof(PhotoAlbumsController.Search)}",
        $"{nameof(PhotoAlbumsController)}.{nameof(PhotoAlbumsController.Get)}",
        $"{nameof(PhotoAlbumsController)}.{nameof(PhotoAlbumsController.Photos)}",
        $"{nameof(PhotoAlbumsController)}.{nameof(PhotoAlbumsController.Seasons)}",
        // Fase 9: accountverzoek (generiek antwoord, rate limit) en aanmeldinstellingen van de app (geen geheimen).
        $"{nameof(AccountRequestsController)}.{nameof(AccountRequestsController.Submit)}",
        $"{nameof(AppAuthController)}.{nameof(AppAuthController.GetConfig)}",
        $"{nameof(AppAuthController)}.{nameof(AppAuthController.Bridge)}",
        // Fase 9b: lid worden (openbaar formulier; e-mailcode, rate limit, handmatige goedkeuring).
        $"{nameof(MembershipApplicationsController)}.{nameof(MembershipApplicationsController.Start)}",
        $"{nameof(MembershipApplicationsController)}.{nameof(MembershipApplicationsController.Verify)}",
        $"{nameof(MembershipApplicationsController)}.{nameof(MembershipApplicationsController.ResendCode)}",
        // Fase 10: push voor gasten (alleen meldingen aan iedereen; rate limit, token versleuteld).
        $"{nameof(PushDevicesController)}.{nameof(PushDevicesController.Register)}",
        $"{nameof(PushDevicesController)}.{nameof(PushDevicesController.Remove)}",
        // Fase 11: openbare optochtinfo en categorieën (Figma 04).
        $"{nameof(ParadeController)}.{nameof(ParadeController.Current)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.Categories)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.ArrivalTimes)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.Results)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.StartPublic)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.VerifyPublic)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.ResendPublic)}",
        $"{nameof(ParadeController)}.{nameof(ParadeController.PublicStatus)}",
        // Fase 19: kaartverkoop zonder account en de webhook van Mollie (status altijd bij Mollie opgehaald).
        $"{nameof(SalesController)}.{nameof(SalesController.Products)}",
        $"{nameof(SalesController)}.{nameof(SalesController.Order)}",
        $"{nameof(SalesController)}.{nameof(SalesController.Get)}",
        $"{nameof(SalesController)}.{nameof(SalesController.Pay)}",
        $"{nameof(SalesController)}.{nameof(SalesController.QrImage)}",
        $"{nameof(SalesController)}.{nameof(SalesController.Waitlist)}",
        $"{nameof(MollieWebhookController)}.{nameof(MollieWebhookController.Webhook)}",
        $"{nameof(WebsiteController)}.{nameof(WebsiteController.Hero)}",
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
