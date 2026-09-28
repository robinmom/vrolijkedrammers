using System.ComponentModel.DataAnnotations;
using Drammers.Infrastructure.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// Push voor gasten zonder account (fase 10): alleen meldingen aan "Iedereen". Beperkt per IP; het token wordt
/// versleuteld opgeslagen en nooit gelogd.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/push-devices")]
[EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
public sealed class PushDevicesController(MyNotifications notifications) : ControllerBase
{
    [HttpPost("anonymous")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(AnonymousPushRequest request, CancellationToken cancellationToken)
    {
        await notifications.RegisterAnonymousTokenAsync(request.InstallId, request.Platform, request.Token, cancellationToken);
        return NoContent();
    }

    [HttpDelete("anonymous/{installId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(string installId, CancellationToken cancellationToken)
    {
        await notifications.RemoveAnonymousTokenAsync(installId, cancellationToken);
        return NoContent();
    }
}

public sealed record AnonymousPushRequest(
    [Required, StringLength(64, MinimumLength = 8)] string InstallId,
    [Required, RegularExpression("^(Ios|Android)$")] string Platform,
    [Required, StringLength(200, MinimumLength = 20)] string Token);
