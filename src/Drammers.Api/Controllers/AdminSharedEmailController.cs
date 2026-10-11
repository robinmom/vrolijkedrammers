using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Accounts met hetzelfde e-mailadres (2026-10-11): overzicht en samenvoegen volgens de vaste regel (de volwassene houdt
/// het account, kinderen komen eronder). Bij inloggen gebeurt dit ook vanzelf.
/// </summary>
[ApiController]
[Route("api/v1/admin/shared-email-accounts")]
[RequirePermission(Permissions.MemberApprove)]
public sealed class AdminSharedEmailController(SharedEmailAccounts sharedEmail) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SharedEmailConflict>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SharedEmailConflict>> List(CancellationToken cancellationToken) => sharedEmail.ListAsync(cancellationToken);

    [HttpPost("resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resolve(SharedEmailResolveRequest request, CancellationToken cancellationToken)
    {
        await sharedEmail.ResolveAsync(request.Email, cancellationToken);
        return NoContent();
    }
}

public sealed record SharedEmailResolveRequest([Required, EmailAddress, StringLength(254)] string Email);
