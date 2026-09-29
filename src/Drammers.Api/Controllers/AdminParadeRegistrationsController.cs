using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Parade.Categories;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Optochtinschrijvingen voor de Optochtcommissie. Fase 11: overzicht en detail met <c>parade.read</c>; in behandeling
/// nemen, goedkeuren, afwijzen, om aanvulling vragen en heropenen met <c>parade.manage</c>. Fase 12a: filters en totalen,
/// startnummers toekennen/wisselen/publiceren (<c>parade.assign-start-number</c>) en de gemeten lengte (<c>parade.manage</c>).
/// </summary>
[ApiController]
[Route("api/v1/admin/parade-registrations")]
[RequirePermission(Permissions.ParadeRead)]
public sealed class AdminParadeRegistrationsController(DrammersDbContext db, ParadeReview review, ParadeLineup lineup) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<ReviewSummary>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<ReviewSummary>> Search(
        [FromQuery] RegistrationStatus? status, [FromQuery] string? search, [FromQuery] int? categoryId,
        [FromQuery, RegularExpression("^(Adult|Youth)$")] string? ageGroup, [FromQuery] bool? hasVehicle,
        [FromQuery, RegularExpression("^(StartNumber|MeasuredLength|Warnings|JuryElsewhere|Documents)$")] string? missing,
        [FromQuery, RegularExpression("^(RegistrationNumber|StartNumber|GroupName|Category|Participants|Length|Status|SubmittedAt)$")] string? sort,
        [FromQuery] bool? descending, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        // Tekst i.p.v. enums als queryparameter: zo blijven de gedeelde enumtypes in de OpenAPI-beschrijving niet-nullable.
        var (p, size) = PagedResult<ReviewSummary>.Normalize(page, pageSize);
        var filter = new RegistrationFilter(status, search, categoryId,
            ageGroup is null ? null : Enum.Parse<AgeGroup>(ageGroup), hasVehicle,
            missing is null ? null : Enum.Parse<MissingData>(missing),
            sort is null ? RegistrationSort.RegistrationNumber : Enum.Parse<RegistrationSort>(sort), descending ?? false);
        var (items, total) = await review.SearchAsync(filter, p, size, cancellationToken);
        return new PagedResult<ReviewSummary>(items, p, size, total);
    }

    /// <summary>Totalen: per status en categorie, deelnemers en de lengte van de optocht (inclusief tussenruimte).</summary>
    [HttpGet("summary")]
    [ProducesResponseType<LineupSummary>(StatusCodes.Status200OK)]
    public Task<LineupSummary> Summary(CancellationToken cancellationToken) => lineup.SummaryAsync(cancellationToken);

    /// <summary>Startnummer toekennen of leegmaken; bezet → 409 <c>START_NUMBER_TAKEN</c>, met <c>swap</c> wisselen.</summary>
    [HttpPut("{id:guid}/start-number")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStartNumber(Guid id, StartNumberRequest request, CancellationToken cancellationToken)
    {
        await lineup.SetStartNumberAsync(id, request.StartNumber, request.Swap, cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/measured-length")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetMeasuredLength(Guid id, MeasuredLengthRequest request, CancellationToken cancellationToken)
    {
        await lineup.SetMeasuredLengthAsync(id, request.MeasuredLengthMeters, cancellationToken);
        return NoContent();
    }

    /// <summary>Publiceert de toegekende startnummers: status "Startnummer toegekend", push en e-mail aan elke groep.</summary>
    [HttpPost("publish-start-numbers")]
    [RequirePermission(Permissions.ParadeAssignStartNumber)]
    [ProducesResponseType<PublishResult>(StatusCodes.Status200OK)]
    public Task<PublishResult> PublishStartNumbers(CancellationToken cancellationToken) =>
        lineup.PublishStartNumbersAsync(CurrentUser.Get(HttpContext)!.UserId, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminRegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdminRegistrationResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var r = await review.GetAsync(id, cancellationToken);
        var category = await db.ParadeCategories.AsNoTracking().Where(c => c.Id == r.CategoryId).Select(c => c.Name).SingleOrDefaultAsync(cancellationToken);
        var managers = await db.ParadeRegistrationManagers.AsNoTracking().Where(m => m.RegistrationId == id)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName).ToListAsync(cancellationToken);
        var actors = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, cancellationToken);
        var statuses = (await review.StatusHistoryAsync(id, cancellationToken))
            .Select(h => new StatusChangeResponse(h.FromStatus, h.ToStatus, h.Reason, h.ActorUserId is { } a ? actors.GetValueOrDefault(a) : null, h.OccurredAt)).ToList();
        var changes = (await review.FieldHistoryAsync(id, cancellationToken))
            .Select(h => new FieldChangeResponse(h.FieldLabel, h.OldValue, h.NewValue, h.ChangedByUserId is { } a ? actors.GetValueOrDefault(a) : null, h.ChangeSource, h.ChangedAt)).ToList();
        var warnings = r.ValidationWarnings is null ? [] : JsonSerializer.Deserialize<List<ValidationIssueResponse>>(r.ValidationWarnings, JsonSerializerOptions.Web) ?? [];
        return new AdminRegistrationResponse(
            r.Id, r.RegistrationNumber, r.StartNumber, r.Status, r.Source, r.GroupName, r.ContactName, r.ContactPhone is null ? null : PhoneNormalizer.Display(r.ContactPhone),
            r.ContactEmail, category, r.Subject, r.SubjectDescription, r.ChildrenCount, r.AdultCount, AddressDto.From(r.BuildAddress),
            r.JuryInspectionSameAsBuildAddress, AddressDto.From(r.EffectiveJuryAddress), r.EstimatedLengthMeters, r.MeasuredLengthMeters, r.AdditionalInformation, r.SubmittedAt,
            managers, [.. warnings.Select(w => w.Message)], [.. ParadeReview.AllowedActions(r.Status)], statuses, changes,
            [.. (await review.DocumentsAsync(id, cancellationToken)).Select(DocumentResponse.From)], r.HasMusic);
    }

    [HttpPost("{id:guid}/review")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Review(Guid id, ReviewRequest request, CancellationToken cancellationToken)
    {
        await review.TransitionAsync(id, request.Action, request.Reason, CurrentUser.Get(HttpContext)!.UserId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    [ProducesResponseType<DocumentLinkResponse>(StatusCodes.Status200OK)]
    public async Task<DocumentLinkResponse> Download(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        new(await review.DocumentUrlAsync(id, documentId, cancellationToken));
}

public sealed record StartNumberRequest([Range(1, 9999)] int? StartNumber, bool Swap = false);

public sealed record MeasuredLengthRequest([Range(0.1, 100)] decimal? MeasuredLengthMeters);

public sealed record ReviewRequest(ReviewAction Action, [StringLength(1000)] string? Reason);

public sealed record StatusChangeResponse(RegistrationStatus? FromStatus, RegistrationStatus ToStatus, string? Reason, string? ActorName, DateTime OccurredAt);

public sealed record FieldChangeResponse(string Field, string? OldValue, string? NewValue, string? ChangedBy, RegistrationSource Source, DateTime ChangedAt);

public sealed record AdminRegistrationResponse(
    Guid Id, int? RegistrationNumber, int? StartNumber, RegistrationStatus Status, RegistrationSource Source, string? GroupName, string? ContactName,
    string? ContactPhone, string? ContactEmail, string? CategoryName, string? Subject, string? SubjectDescription, int ChildrenCount, int AdultCount,
    AddressDto BuildAddress, bool JuryInspectionSameAsBuildAddress, AddressDto JuryAddress, decimal? EstimatedLengthMeters, decimal? MeasuredLengthMeters, string? AdditionalInformation,
    DateTime? SubmittedAt, IReadOnlyList<string> Managers, IReadOnlyList<string> Warnings, IReadOnlyList<ReviewAction> AllowedActions,
    IReadOnlyList<StatusChangeResponse> StatusHistory, IReadOnlyList<FieldChangeResponse> Changes, IReadOnlyList<DocumentResponse> Documents,
    bool? HasMusic);
