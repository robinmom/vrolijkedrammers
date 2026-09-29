using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Ticketing;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// "Mijn kinderen" (fase 17b): het kind-detail en de QR van een kind op de telefoon van de ouder. Alleen voor een
/// gekoppelde ouder, zolang het kind jonger dan 18 is en geen eigen account heeft (dan staat de QR op de eigen telefoon).
/// </summary>
[ApiController]
[Route("api/v1/me/children/{memberId:guid}")]
[RequirePermission(Permissions.GuardianReadOwn)]
public sealed class MeChildrenController(Guardians guardians, MemberTickets tickets) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    private string InstallationId => Request.Headers[DeviceCheck.HeaderName].ToString();

    [HttpGet]
    [ProducesResponseType<MyChildDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<MyChildDetail> Get(Guid memberId, CancellationToken cancellationToken) =>
        guardians.ChildDetailAsync(UserId, memberId, cancellationToken);

    [HttpGet("ticket")]
    [ProducesResponseType<MyTicket>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<MyTicket> Ticket(Guid memberId, CancellationToken cancellationToken) =>
        tickets.GetAsync(UserId, InstallationId, cancellationToken, memberId);

    [HttpPost("ticket/challenge")]
    [ProducesResponseType<ChallengeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ChallengeResponse> Challenge(Guid memberId, CancellationToken cancellationToken) =>
        new(await tickets.ChallengeAsync(UserId, cancellationToken, memberId));

    /// <summary>Koppelt het ticket van het kind aan dit toestel van de ouder (telt mee voor het maximum per carnavalsjaar).</summary>
    [HttpPost("ticket/bind-device")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Bind(Guid memberId, BindDeviceRequest request, CancellationToken cancellationToken)
    {
        await tickets.BindAsync(UserId, InstallationId, request.Challenge, request.Signature, cancellationToken, memberId);
        return NoContent();
    }

    [HttpGet("ticket/code")]
    [ProducesResponseType<ServerCode>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ServerCode> Code(Guid memberId, CancellationToken cancellationToken) =>
        tickets.ServerCodeAsync(UserId, InstallationId, cancellationToken, memberId);
}
