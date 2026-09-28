using Drammers.Infrastructure.ParadeManagement;
using Drammers.Modules.Parade.Categories;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Openbare optochtinfo (Figma 04) en de categorieën met hun regels (fase 11).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/parade")]
public sealed class ParadeController(ParadeAdministration parades) : ControllerBase
{
    [HttpGet("current")]
    [ProducesResponseType<ParadeInfoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ParadeInfoResponse> Current(CancellationToken cancellationToken)
    {
        var p = await parades.CurrentAsync(cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Er is nog geen optocht bekend.", DomainErrorKind.NotFound);
        return new ParadeInfoResponse(p.Id, p.Name, p.ParadeDate, p.StartTime, p.StartLocation, p.RouteDescription, p.RouteLengthKm,
            p.RegistrationOpensAt, p.RegistrationClosesAt, p.IsRegistrationOpen(parades.Now), p.SubjectRequired, p.MaxDocumentsPerRegistration, p.MaxDocumentSizeMb);
    }

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<ParadeCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ParadeCategoryResponse>> Categories(CancellationToken cancellationToken)
    {
        var parade = await parades.CurrentAsync(cancellationToken);
        return [.. (await parades.CategoriesForAsync(parade?.Id, activeOnly: true, cancellationToken)).Select(ParadeCategoryResponse.From)];
    }
}

public sealed record ParadeInfoResponse(
    Guid Id, string Name, DateOnly ParadeDate, TimeOnly StartTime, string? StartLocation, string? RouteDescription, decimal? RouteLengthKm,
    DateTime RegistrationOpensAt, DateTime RegistrationClosesAt, bool RegistrationOpen, bool SubjectRequired, int MaxDocumentsPerRegistration, int MaxDocumentSizeMb);

public sealed record ParadeCategoryResponse(
    int Id, string Code, string Name, AgeGroup AgeGroup, CategoryType Type, int? MinimumParticipants, int? MaximumParticipants,
    ParticipantCountBasis ParticipantCountBasis, ValidationMode ValidationMode, bool HasVehicle, bool Active, int SortOrder)
{
    public static ParadeCategoryResponse From(ParadeCategory c) => new(
        c.Id, c.Code, c.Name, c.AgeGroup, c.Type, c.MinimumParticipants, c.MaximumParticipants, c.ParticipantCountBasis, c.ValidationMode, c.HasVehicle, c.Active, c.SortOrder);
}
