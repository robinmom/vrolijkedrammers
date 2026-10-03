using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// "Ik ben al lid": account aanvragen met het e-mailadres uit de ledenadministratie, eventueel met lidnummer (ADR-014,
/// fase 9; sinds fase 24 is het lidnummer niet meer nodig). Altijd hetzelfde antwoord
/// (202), ongeacht of er een lid is, het e-mailadres klopt of er al een account bestaat: geen enumeratie.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/account-requests")]
[EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
public sealed class AccountRequestsController(MemberAccounts accounts) : ControllerBase
{
    public const string GenericMessage =
        "Je aanvraag is in behandeling. Klopt alles met de ledenadministratie, dan ontvang je binnen enkele minuten een e-mail met uitleg om in te loggen. Kijk ook in je map met ongewenste e-mail. Zo niet, dan kijkt het bestuur ernaar en hoor je van ons.";

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
    [param: StringLength(15)] string? MemberNumber,
    [param: Required, EmailAddress, StringLength(254)] string Email);

public sealed record AccountRequestAcceptedResponse(string Message);
