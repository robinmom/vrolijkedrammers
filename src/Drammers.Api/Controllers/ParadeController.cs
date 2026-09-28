using System.ComponentModel.DataAnnotations;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Modules.Parade.Categories;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Drammers.Api.Controllers;

/// <summary>
/// Openbare optochtinfo (Figma 04), de categorieën met hun regels en inschrijven zonder account (fase 11c: gasten in de
/// app en het webformulier), met e-mailcode en statuslink.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/parade")]
public sealed class ParadeController(ParadeAdministration parades, ParadePublicRegistrations publicRegistrations) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<ParadeInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ParadeInfoResponse> Current(CancellationToken cancellationToken)
    {
        var p = await parades.CurrentAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht bekend.", DomainErrorKind.NotFound);
        return new ParadeInfoResponse(p.Id, p.Name, p.ParadeDate, p.StartTime, p.StartLocation, p.RouteDescription, p.RouteLengthKm,
            p.RegistrationOpensAt, p.RegistrationClosesAt, p.IsRegistrationOpen(parades.Now), p.SubjectRequired, p.MaxDocumentsPerRegistration, p.MaxDocumentSizeMb,
            MarkdownRenderer.ToSafeHtml(p.InfoText));
    }

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<ParadeCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ParadeCategoryResponse>> Categories(CancellationToken cancellationToken)
    {
        var parade = await parades.CurrentAsync(cancellationToken);
        return [.. (await parades.CategoriesForAsync(parade?.Id, activeOnly: true, cancellationToken)).Select(ParadeCategoryResponse.From)];
    }

    /// <summary>Inschrijven zonder account: alles in één keer; daarna een e-mailcode. Pas na de code een opgavenummer.</summary>
    [HttpPost("public-registrations")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType<PublicRegistrationStartedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<PublicRegistrationStartedResponse>> StartPublic(PublicRegistrationRequest request, CancellationToken cancellationToken)
    {
        var id = await publicRegistrations.StartAsync(request.Registration.ToInput(), request.RulesAccepted, cancellationToken);
        return Created((string?)null, new PublicRegistrationStartedResponse(id));
    }

    [HttpPost("public-registrations/{id:guid}/verify-email")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType<PublicRegistrationSubmittedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<PublicRegistrationSubmittedResponse> VerifyPublic(Guid id, VerifyCodeRequest request, CancellationToken cancellationToken)
    {
        var result = await publicRegistrations.VerifyAsync(id, request.Code, $"{Request.Scheme}://{Request.Host}", cancellationToken);
        return new PublicRegistrationSubmittedResponse(result.RegistrationNumber, result.StatusToken);
    }

    [HttpPost("public-registrations/{id:guid}/resend-code")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousFormsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendPublic(Guid id, CancellationToken cancellationToken)
    {
        await publicRegistrations.ResendCodeAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Status via de statuslink uit de bevestigingsmail (alleen lezen, zonder contactgegevens).</summary>
    [HttpGet("public-registrations/status")]
    [EnableRateLimiting(Authorization.AuthorizationSetup.AnonymousStatusPolicy)]
    [ProducesResponseType<PublicStatus>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PublicStatus> PublicStatus([FromQuery, Required] string token, CancellationToken cancellationToken) =>
        publicRegistrations.StatusAsync(token, cancellationToken);
}

public sealed record PublicRegistrationRequest([Required] UpdateRegistrationRequest Registration, bool RulesAccepted);

public sealed record PublicRegistrationStartedResponse(Guid Id);

public sealed record PublicRegistrationSubmittedResponse(int RegistrationNumber, string StatusToken);

public sealed record VerifyCodeRequest([Required, StringLength(6, MinimumLength = 6)] string Code);

public sealed record ParadeInfoResponse(
    Guid Id, string Name, DateOnly ParadeDate, TimeOnly StartTime, string? StartLocation, string? RouteDescription, decimal? RouteLengthKm,
    DateTime RegistrationOpensAt, DateTime RegistrationClosesAt, bool RegistrationOpen, bool SubjectRequired, int MaxDocumentsPerRegistration, int MaxDocumentSizeMb,
    string? InfoHtml);

public sealed record ParadeCategoryResponse(
    int Id, string Code, string Name, AgeGroup AgeGroup, CategoryType Type, int? MinimumParticipants, int? MaximumParticipants,
    ParticipantCountBasis ParticipantCountBasis, ValidationMode ValidationMode, bool HasVehicle, bool Active, int SortOrder)
{
    public static ParadeCategoryResponse From(ParadeCategory c) => new(
        c.Id, c.Code, c.Name, c.AgeGroup, c.Type, c.MinimumParticipants, c.MaximumParticipants, c.ParticipantCountBasis, c.ValidationMode, c.HasVehicle, c.Active, c.SortOrder);
}
