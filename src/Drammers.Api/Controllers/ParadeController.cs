using System.ComponentModel.DataAnnotations;
using Drammers.Api.Content;
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
public sealed class ParadeController(ParadeAdministration parades, ParadePublicRegistrations publicRegistrations, ParadeResults results, ContentUrls urls) : ControllerBase
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

    /// <summary>Openbare aanrijtijdenlijst (fase 16, zoals op de website): alleen wagens, pas na publiceren.</summary>
    [HttpGet("arrival-times")]
    [ProducesResponseType<PublicArrivals>(StatusCodes.Status200OK)]
    public Task<PublicArrivals> ArrivalTimes([FromServices] ParadeArrivals arrivals, CancellationToken cancellationToken) =>
        arrivals.PublicAsync(cancellationToken);

    /// <summary>
    /// De gepubliceerde uitslag (fase 22c): de laatste optocht waarvan de uitslag na de prijsuitreiking is gepubliceerd.
    /// Daarvoor 404, zodat de uitslag nergens te vinden is. Alleen plaats, groep, motto en totaal.
    /// </summary>
    [HttpGet("results")]
    [ProducesResponseType<PublicResultsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PublicResultsResponse>> Results(CancellationToken cancellationToken)
    {
        var o = await results.PublishedAsync(cancellationToken);
        if (o is null)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: "Er is nog geen uitslag gepubliceerd.");
        }

        var categories = new List<PublicCategoryResultResponse>();
        foreach (var c in o.Categories)
        {
            var rows = new List<PublicResultRowResponse>();
            foreach (var r in c.Rows)
            {
                // Fase 22d: de eerste foto van de inzending (korte SAS-link, zoals de andere foto's in de app).
                rows.Add(new PublicResultRowResponse(r.Place, r.StartNumber, r.GroupName, r.Motto, r.Total,
                    await urls.ForAsync(Infrastructure.Files.FileContainers.PhotosDerived, r.Photo?.ThumbnailBlobPath, cancellationToken)));
            }

            categories.Add(new PublicCategoryResultResponse(c.Name, c.MaxPoints, rows));
        }

        return new PublicResultsResponse(o.ParadeName, o.ParadeDate, o.PublishedAt!.Value, o.AlbumId, categories);
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

public sealed record PublicResultsResponse(string ParadeName, DateOnly ParadeDate, DateTime PublishedAt, Guid? AlbumId, IReadOnlyList<PublicCategoryResultResponse> Categories);

public sealed record PublicCategoryResultResponse(string Name, int MaxPoints, IReadOnlyList<PublicResultRowResponse> Rows);

public sealed record PublicResultRowResponse(int Place, int? StartNumber, string GroupName, string? Motto, decimal Total, string? PhotoUrl);
