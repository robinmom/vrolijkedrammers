using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// "Ik ben al lid": account aanvragen met lidnummer en e-mailadres (ADR-014, fase 9). Altijd hetzelfde antwoord
/// (202), ongeacht of er een lid is, het e-mailadres klopt of er al een account bestaat: geen enumeratie.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/account-requests")]
[EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
public sealed class AccountRequestsController(MemberAccounts accounts) : ControllerBase
{
    public const string GenericMessage =
        "Bedankt! Klopt alles, dan ontvang je binnen enkele minuten een e-mail om in te loggen. Zo niet, dan kijkt het bestuur ernaar en hoor je van ons.";

    [HttpPost]
    [ProducesResponseType<AccountRequestAcceptedResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<AcceptedResult> Submit(AccountRequestRequest request, CancellationToken cancellationToken)
    {
        await accounts.SubmitAccountRequestAsync(request.MemberNumber, request.Email, HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Accepted((string?)null, new AccountRequestAcceptedResponse(GenericMessage));
    }
}

public sealed record AccountRequestRequest(
    [param: Required, StringLength(15, MinimumLength = 1)] string MemberNumber,
    [param: Required, EmailAddress, StringLength(254)] string Email);

public sealed record AccountRequestAcceptedResponse(string Message);
