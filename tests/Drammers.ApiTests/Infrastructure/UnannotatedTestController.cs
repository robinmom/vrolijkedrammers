using Microsoft.AspNetCore.Mvc;

namespace Drammers.ApiTests.Infrastructure;

/// <summary>Bewust zonder autorisatie-annotatie: de fallbackpolicy moet dit endpoint afschermen (deny by default).</summary>
[ApiController]
[Route("test/unannotated")]
public sealed class UnannotatedTestController : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => Ok("dit mag niemand zien");
}
