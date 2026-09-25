using Drammers.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Publieke app-configuratie: minimale appversie, onderhoud en feature flags (docs/05 §2).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/app-config")]
public sealed class AppConfigController(AppConfigReader reader) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AppConfigResponse>(StatusCodes.Status200OK)]
    public async Task<AppConfigResponse> Get(CancellationToken cancellationToken)
    {
        var config = await reader.GetAsync(cancellationToken);
        return new AppConfigResponse(
            new MinAppVersion(config.MinAppVersionIos, config.MinAppVersionAndroid),
            config.RecommendedAppVersion,
            new Maintenance(config.MaintenanceMode, config.MaintenanceMessage),
            config.SupportEmail,
            config.Features);
    }
}

public sealed record AppConfigResponse(
    MinAppVersion MinAppVersion,
    string RecommendedAppVersion,
    Maintenance Maintenance,
    string? SupportEmail,
    IReadOnlyDictionary<string, bool> Features);

public sealed record MinAppVersion(string Ios, string Android);

public sealed record Maintenance(bool Enabled, string? Message);
