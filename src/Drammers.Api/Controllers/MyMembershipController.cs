using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Eigen lidmaatschap in de app (fase 26): gegevens wijzigen (na goedkeuring) en een combinatie verbreken (beide akkoord,
/// daarna de ledenadministratie).
/// </summary>
[ApiController]
[Route("api/v1/me")]
[RequirePermission(Permissions.MemberReadOwn)]
public sealed class MyMembershipController(MemberRequests requests) : ControllerBase
{
    private Guid MemberId => CurrentUser.Get(HttpContext)!.MemberId
        ?? throw new DomainException(ErrorCodes.MemberNotFound, "Je account is niet aan een lid gekoppeld.", DomainErrorKind.NotFound);

    /// <summary>Het laatste wijzigingsverzoek en, bij een combinatie, de stand van een verzoek om te verbreken.</summary>
    [HttpGet("membership-requests")]
    [ProducesResponseType<MyMemberRequests>(StatusCodes.Status200OK)]
    public Task<MyMemberRequests> Get(CancellationToken cancellationToken) => requests.MineAsync(MemberId, cancellationToken);

    [HttpPost("change-requests")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Submit(MyChangeRequestInput input, CancellationToken cancellationToken)
    {
        var id = await requests.SubmitChangeAsync(MemberId, CurrentUser.Get(HttpContext)!.UserId,
            new MemberChangeInput(input.AddressLine, input.PostalCode, input.City, input.Email, input.Phone, input.MobilePhone, input.Iban,
                input.AccountHolder, input.MandateConsent), cancellationToken);
        return Created((string?)null, new CreatedResponse(id));
    }

    [HttpDelete("change-requests/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await requests.CancelChangeAsync(MemberId, id, cancellationToken);
        return NoContent();
    }

    /// <summary>Verbreken aanvragen (telt als eigen akkoord). Het tweede lid geeft hierbij zijn IBAN en machtiging.</summary>
    [HttpPost("combination-break")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> StartBreak(MyBankInput input, CancellationToken cancellationToken)
    {
        await requests.StartBreakAsync(MemberId, new BankInput(input.Iban, input.AccountHolder, input.MandateConsent), cancellationToken);
        return NoContent();
    }

    [HttpPost("combination-break/agree")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AgreeBreak(MyBankInput input, CancellationToken cancellationToken)
    {
        await requests.AgreeBreakAsync(MemberId, new BankInput(input.Iban, input.AccountHolder, input.MandateConsent), cancellationToken);
        return NoContent();
    }

    /// <summary>Niet akkoord of toch niet verbreken.</summary>
    [HttpPost("combination-break/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CancelBreak(CancellationToken cancellationToken)
    {
        await requests.CancelBreakAsync(MemberId, cancellationToken);
        return NoContent();
    }
}

public sealed record MyChangeRequestInput(
    [param: StringLength(150)] string? AddressLine,
    [param: StringLength(10)] string? PostalCode,
    [param: StringLength(50)] string? City,
    [param: StringLength(150)] string? Email,
    [param: StringLength(50)] string? Phone,
    [param: StringLength(50)] string? MobilePhone,
    [param: StringLength(40)] string? Iban,
    [param: StringLength(100)] string? AccountHolder,
    bool MandateConsent);

public sealed record MyBankInput([param: StringLength(40)] string? Iban, [param: StringLength(100)] string? AccountHolder, bool MandateConsent);
