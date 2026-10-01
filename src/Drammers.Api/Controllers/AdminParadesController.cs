using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Parades;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Optochten en categorieën configureren (fase 11a, <c>parade.config</c>).</summary>
[ApiController]
[Route("api/v1/admin")]
[RequirePermission(Permissions.ParadeConfig)]
public sealed class AdminParadesController(DrammersDbContext db, ParadeAdministration parades, ParadeJury jury) : ControllerBase
{
    [HttpGet("parades")]
    [ProducesResponseType<IReadOnlyList<AdminParadeResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminParadeResponse>> List(CancellationToken cancellationToken) =>
        [.. (await db.Parades.AsNoTracking().OrderByDescending(p => p.ParadeDate).ToListAsync(cancellationToken)).Select(AdminParadeResponse.From)];

    [HttpGet("parades/{id:guid}")]
    [ProducesResponseType<AdminParadeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdminParadeResponse> Get(Guid id, CancellationToken cancellationToken) =>
        AdminParadeResponse.From(await db.Parades.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ParadeNotFound, "Optocht niet gevonden.", DomainErrorKind.NotFound));

    [HttpPost("parades")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(ParadeRequest request, CancellationToken cancellationToken)
    {
        var parade = await parades.CreateAsync(request.ToInput(), cancellationToken);
        // Fase 22a: weging en jury-indeling overnemen van de vorige optocht.
        await jury.EnsureCategoriesAsync(parade.Id, cancellationToken);
        return Created($"/api/v1/admin/parades/{parade.Id}", new CreatedResponse(parade.Id));
    }

    [HttpPut("parades/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, ParadeRequest request, CancellationToken cancellationToken)
    {
        await parades.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    /// <summary>Vaste plekken vooraan (startnummer 1 … n), bijv. geluidswagen, verenigingswagen en het Convent.</summary>
    [HttpPut("parades/{id:guid}/fixed-entries")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetFixedEntries(Guid id, FixedEntriesRequest request, CancellationToken cancellationToken)
    {
        await parades.SetFixedEntriesAsync(id,
            [.. request.Entries.Select(e => new ParadeFixedEntry { Name = e.Name, AdultCount = e.AdultCount, ChildrenCount = e.ChildrenCount, HasMusic = e.HasMusic })],
            cancellationToken);
        return NoContent();
    }

    [HttpGet("parade-categories")]
    [ProducesResponseType<IReadOnlyList<ParadeCategoryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ParadeCategoryResponse>> Categories(CancellationToken cancellationToken) =>
        [.. (await parades.CategoriesForAsync(null, activeOnly: false, cancellationToken)).Select(ParadeCategoryResponse.From)];

    [HttpPost("parade-categories")]
    [ProducesResponseType<ParadeCategoryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParadeCategoryResponse>> CreateCategory(CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await parades.SaveCategoryAsync(null, request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/parade-categories/{category.Id}", ParadeCategoryResponse.From(category));
    }

    [HttpPut("parade-categories/{id:int}")]
    [ProducesResponseType<ParadeCategoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ParadeCategoryResponse> UpdateCategory(int id, CategoryRequest request, CancellationToken cancellationToken) =>
        ParadeCategoryResponse.From(await parades.SaveCategoryAsync(id, request.ToInput(), cancellationToken));
}

public sealed record ParadeRequest(
    int CarnivalYearId,
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    DateOnly ParadeDate,
    TimeOnly StartTime,
    [StringLength(200)] string? StartLocation,
    [StringLength(2000)] string? RouteDescription,
    [Range(0, 100)] decimal? RouteLengthKm,
    DateTime RegistrationOpensAt,
    DateTime RegistrationClosesAt,
    DateTime? EditDeadlineAt,
    bool SubjectRequired,
    [Range(0, 50)] decimal DefaultSpacingMeters,
    [Range(0, 20)] int MaxDocumentsPerRegistration,
    [Range(1, 25)] int MaxDocumentSizeMb,
    ParadeStatus Status,
    [StringLength(8000)] string? InfoText = null)
{
    public ParadeInput ToInput() => new(CarnivalYearId, Name, ParadeDate, StartTime, StartLocation, RouteDescription, RouteLengthKm,
        RegistrationOpensAt.ToUniversalTime(), RegistrationClosesAt.ToUniversalTime(), EditDeadlineAt?.ToUniversalTime(), SubjectRequired,
        DefaultSpacingMeters, MaxDocumentsPerRegistration, MaxDocumentSizeMb, Status, InfoText);
}

public sealed record AdminParadeResponse(
    Guid Id, int CarnivalYearId, string Name, DateOnly ParadeDate, TimeOnly StartTime, string? StartLocation, string? RouteDescription,
    decimal? RouteLengthKm, DateTime RegistrationOpensAt, DateTime RegistrationClosesAt, DateTime? EditDeadlineAt, bool SubjectRequired,
    decimal DefaultSpacingMeters, int MaxDocumentsPerRegistration, int MaxDocumentSizeMb, ParadeStatus Status, string? InfoText,
    IReadOnlyList<FixedEntryDto> FixedEntries)
{
    public static AdminParadeResponse From(Parade p) => new(p.Id, p.CarnivalYearId, p.Name, p.ParadeDate, p.StartTime, p.StartLocation, p.RouteDescription,
        p.RouteLengthKm, p.RegistrationOpensAt, p.RegistrationClosesAt, p.EditDeadlineAt, p.SubjectRequired, p.DefaultSpacingMeters,
        p.MaxDocumentsPerRegistration, p.MaxDocumentSizeMb, p.Status, p.InfoText,
        [.. p.FixedEntries.Select(e => new FixedEntryDto(e.Name, e.AdultCount, e.ChildrenCount, e.HasMusic))]);
}

public sealed record FixedEntryDto([Required, StringLength(100)] string Name, [Range(0, 1000)] int AdultCount, [Range(0, 1000)] int ChildrenCount, bool HasMusic);

public sealed record FixedEntriesRequest([Required, MaxLength(ParadeFixedEntry.MaxCount)] IReadOnlyList<FixedEntryDto> Entries);

public sealed record CategoryRequest(
    [Required, StringLength(40, MinimumLength = 2)] string Code,
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    AgeGroup AgeGroup,
    CategoryType Type,
    [Range(0, 1000)] int? MinimumParticipants,
    [Range(1, 1000)] int? MaximumParticipants,
    ParticipantCountBasis ParticipantCountBasis,
    ValidationMode ValidationMode,
    bool HasVehicle,
    bool Active,
    int SortOrder)
{
    public CategoryInput ToInput() => new(Code, Name, AgeGroup, Type, MinimumParticipants, MaximumParticipants, ParticipantCountBasis, ValidationMode, HasVehicle, Active, SortOrder);
}
