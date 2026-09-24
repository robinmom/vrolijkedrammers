using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Diagnose-endpoint: 204 als het token geldig is voor deze omgeving, anders 401 (fase 1-acceptatie).</summary>
[ApiController]
[Authorize]
[Route("api/v1/auth/check")]
public sealed class AuthCheckController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult Get() => NoContent();
}
