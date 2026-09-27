using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Modules.Membership.Applications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// Lid worden (fase 9b, ADR-014): openbaar formulier in de app en op de webpagina. Na de e-mailcode staat de aanmelding
/// in de wachtrij van het bestuur; er ontstaat nooit automatisch een lidmaatschap. Rate limit per IP.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/membership-applications")]
[EnableRateLimiting(AuthorizationSetup.AnonymousFormsPolicy)]
public sealed class MembershipApplicationsController(MembershipApplications applications) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ApplicationStartedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<ApplicationStartedResponse>> Start(ApplicationRequest request, CancellationToken cancellationToken)
    {
        var id = await applications.StartAsync(request.ToInput(), HttpContext.Connection.RemoteIpAddress?.ToString(), cancellationToken);
        return Created((string?)null, new ApplicationStartedResponse(id));
    }

    /// <summary>E-mailadres bevestigen met de code; daarmee is de aanmelding ingediend.</summary>
    [HttpPost("{id:guid}/verify-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Verify(Guid id, VerifyApplicationRequest request, CancellationToken cancellationToken)
    {
        await applications.VerifyAsync(id, request.Code, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/resend-code")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendCode(Guid id, CancellationToken cancellationToken)
    {
        await applications.ResendCodeAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record ApplicationRequest(
    [param: Required, StringLength(50)] string FirstName,
    [param: StringLength(20)] string? NamePrefix,
    [param: Required, StringLength(60)] string LastName,
    [param: StringLength(1)] string? Gender,
    DateOnly BirthDate,
    [param: Required, StringLength(150)] string AddressLine,
    [param: Required, StringLength(10)] string PostalCode,
    [param: Required, StringLength(50)] string City,
    [param: Required, EmailAddress, StringLength(150)] string Email,
    [param: StringLength(30)] string? Phone,
    [param: StringLength(100)] string? GuardianName,
    [param: StringLength(30)] string? GuardianPhone,
    [param: Required, StringLength(40)] string Iban,
    [param: Required, StringLength(100)] string AccountHolder,
    bool MandateConsent,
    bool PrivacyConsent,
    bool PhotoConsent,
    ApplicationSource Source)
{
    public ApplicationInput ToInput() => new(
        FirstName, NamePrefix, LastName, Gender, BirthDate, AddressLine, PostalCode, City, Email, Phone, GuardianName, GuardianPhone,
        Iban, AccountHolder, MandateConsent, PrivacyConsent, PhotoConsent, Source == ApplicationSource.Portal ? ApplicationSource.App : Source);
}

public sealed record ApplicationStartedResponse(Guid Id);

public sealed record VerifyApplicationRequest([param: Required, StringLength(6, MinimumLength = 6)] string Code);
