using Drammers.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Controllers;

/// <summary>
/// Aanmeldinstellingen voor het beheerportal (MSAL). Het portal is hetzelfde pakket in Dev, Acc en Prod; de
/// omgevingsspecifieke waarden komen hier vandaan. Bevat geen geheimen.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/portal-config")]
public sealed class PortalConfigController(IOptions<AuthOptions> auth, IConfiguration configuration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PortalConfigResponse>(StatusCodes.Status200OK)]
    public PortalConfigResponse Get()
    {
        var options = auth.Value;
        var authority = options.Authority?.Replace("/v2.0", string.Empty, StringComparison.Ordinal).TrimEnd('/');
        return new PortalConfigResponse(
            configuration["Portal:ClientId"] ?? string.Empty,
            authority ?? string.Empty,
            string.IsNullOrEmpty(options.Audience) ? string.Empty : $"api://{options.Audience}/access_as_user");
    }
}

public sealed record PortalConfigResponse(string ClientId, string Authority, string ApiScope);
