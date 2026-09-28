using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Ticketing;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// "Mijn QR" (fase 13, ADR-005): het ledenticket van de ingelogde gebruiker, de hardwaresleutel van dit toestel,
/// koppelen met proof-of-possession en de fallbackcode van de server. Het toestel komt uit <c>X-Device-Id</c>.
/// </summary>
[ApiController]
[Route("api/v1/me")]
[RequirePermission(Permissions.TicketReadOwn)]
public sealed class MeTicketController(MemberTickets tickets) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    private string InstallationId => Request.Headers[DeviceCheck.HeaderName].ToString();

    [HttpGet("ticket")]
    [ProducesResponseType<MyTicket>(StatusCodes.Status200OK)]
    public Task<MyTicket> Get(CancellationToken cancellationToken) => tickets.GetAsync(UserId, InstallationId, cancellationToken);

    /// <summary>Publieke sleutel uit de Secure Enclave/Keystore van dit toestel (SubjectPublicKeyInfo, base64).</summary>
    [HttpPut("devices/current/key")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RegisterKey(DeviceKeyRequest request, CancellationToken cancellationToken)
    {
        await tickets.RegisterDeviceKeyAsync(UserId, InstallationId, request.PublicKey, request.SecurityLevel, cancellationToken);
        return NoContent();
    }

    [HttpPost("ticket/challenge")]
    [ProducesResponseType<ChallengeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ChallengeResponse> Challenge(CancellationToken cancellationToken) =>
        new(await tickets.ChallengeAsync(UserId, cancellationToken));

    /// <summary>Koppelt het ticket aan dit toestel; met een hardwaresleutel alleen met de handtekening over de challenge.</summary>
    [HttpPost("ticket/bind-device")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Bind(BindDeviceRequest request, CancellationToken cancellationToken)
    {
        await tickets.BindAsync(UserId, InstallationId, request.Challenge, request.Signature, cancellationToken);
        return NoContent();
    }

    /// <summary>Door de server ondertekende code (45 s) voor toestellen zonder hardwaresleutel.</summary>
    [HttpGet("ticket/code")]
    [ProducesResponseType<ServerCode>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ServerCode> Code(CancellationToken cancellationToken) => tickets.ServerCodeAsync(UserId, InstallationId, cancellationToken);
}

public sealed record DeviceKeyRequest([Required, StringLength(500)] string PublicKey, [Required, StringLength(30)] string SecurityLevel);

public sealed record ChallengeResponse(string Challenge);

public sealed record BindDeviceRequest([StringLength(100)] string? Challenge, [StringLength(200)] string? Signature);
