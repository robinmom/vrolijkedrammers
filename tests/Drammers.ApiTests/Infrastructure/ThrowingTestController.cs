using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.ApiTests.Infrastructure;

/// <summary>Alleen in tests geregistreerd om de globale foutafhandeling te controleren.</summary>
[ApiController]
[AllowAnonymous]
[Route("test/throw")]
public sealed class ThrowingTestController : ControllerBase
{
    [HttpGet]
    public IActionResult Throw() => throw new InvalidOperationException("Geheime interne details die niet mogen lekken");
}
