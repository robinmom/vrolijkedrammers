using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Modules.Parade.Registrations;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Optochtinschrijvingen van leden (fase 11a, docs/13 §4): alleen eigen inschrijvingen (beheerder), anders 404. Het
/// opgavenummer staat in geen enkel verzoek (ADR-011).
/// </summary>
[ApiController]
[Route("api/v1/parade/registrations")]
[RequirePermission(Permissions.ParadeRegister)]
public sealed class ParadeRegistrationsController(ParadeRegistrations registrations) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RegistrationSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<RegistrationSummaryResponse>> Mine(CancellationToken cancellationToken) =>
        [.. (await registrations.MineAsync(UserId, cancellationToken)).Select(r => new RegistrationSummaryResponse(
            r.Id, r.GroupName, r.Status, r.RegistrationNumber, r.StartNumber, r.SubmittedAt, r.CreatedAt))];

    [HttpPost]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RegistrationResponse>> Create(CancellationToken cancellationToken)
    {
        var registration = await registrations.CreateDraftAsync(CurrentUser.Get(HttpContext)!, cancellationToken);
        return Created($"/api/v1/parade/registrations/{registration.Id}", await ResponseAsync(registration, [], cancellationToken));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<RegistrationResponse> Get(Guid id, CancellationToken cancellationToken) =>
        await ResponseAsync(await registrations.GetAsync(UserId, id, cancellationToken), [], cancellationToken);

    /// <summary>Autosave van de wizard; <c>version</c> uit de vorige respons, anders 412.</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status412PreconditionFailed)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<RegistrationResponse> Update(Guid id, UpdateRegistrationRequest request, CancellationToken cancellationToken)
    {
        var (registration, issues) = await registrations.UpdateAsync(UserId, id, request.ToInput(), request.Version, cancellationToken);
        return await ResponseAsync(registration, issues, cancellationToken);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteDraft(Guid id, CancellationToken cancellationToken)
    {
        await registrations.DeleteDraftAsync(UserId, id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/validate")]
    [ProducesResponseType<IReadOnlyList<ValidationIssueResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ValidationIssueResponse>> Validate(Guid id, CancellationToken cancellationToken) =>
        [.. (await registrations.ValidateAsync(UserId, id, cancellationToken)).Select(ValidationIssueResponse.From)];

    /// <summary>Definitief indienen: opgavenummer (volgorde van binnenkomst). Nogmaals indienen geeft hetzelfde nummer.</summary>
    [HttpPost("{id:guid}/submit")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<RegistrationResponse> Submit(Guid id, CancellationToken cancellationToken) =>
        await ResponseAsync(await registrations.SubmitAsync(UserId, id, cancellationToken), [], cancellationToken);

    [HttpPost("{id:guid}/withdraw")]
    [ProducesResponseType<RegistrationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<RegistrationResponse> Withdraw(Guid id, WithdrawRequest request, CancellationToken cancellationToken) =>
        await ResponseAsync(await registrations.WithdrawAsync(UserId, id, request.Reason, cancellationToken), [], cancellationToken);

    /// <summary>Onthouden bouwlocaties (volgend jaar "zelfde locatie"); de nieuwste eerst.</summary>
    [HttpGet("/api/v1/parade/build-locations")]
    [ProducesResponseType<IReadOnlyList<BuildLocationResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<BuildLocationResponse>> Locations(CancellationToken cancellationToken) =>
        [.. (await registrations.LocationsAsync(UserId, cancellationToken)).Select(l => new BuildLocationResponse(l.Id, AddressDto.From(l.Address), l.LastUsedAt))];

    [HttpDelete("/api/v1/parade/build-locations/{locationId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteLocation(Guid locationId, CancellationToken cancellationToken)
    {
        await registrations.DeleteLocationAsync(UserId, locationId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/managers")]
    [ProducesResponseType<IReadOnlyList<ManagerResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<ManagerResponse>> Managers(Guid id, CancellationToken cancellationToken) =>
        [.. (await registrations.ManagersAsync(UserId, id, cancellationToken)).Select(m => new ManagerResponse(m.UserId, m.DisplayName, m.Role))];

    [HttpPost("{id:guid}/managers")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddManager(Guid id, AddManagerRequest request, CancellationToken cancellationToken)
    {
        await registrations.AddManagerAsync(UserId, id, request.Email, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/managers/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveManager(Guid id, Guid userId, CancellationToken cancellationToken)
    {
        await registrations.RemoveManagerAsync(UserId, id, userId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents")]
    [ProducesResponseType<IReadOnlyList<DocumentResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<DocumentResponse>> Documents(Guid id, CancellationToken cancellationToken) =>
        [.. (await registrations.DocumentsAsync(UserId, id, cancellationToken)).Select(DocumentResponse.From)];

    [HttpPost("{id:guid}/documents")]
    [RequestSizeLimit(26 * 1024 * 1024)]
    [ProducesResponseType<DocumentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DocumentResponse>> AddDocument(Guid id, [FromForm] DocumentType type, IFormFile file, CancellationToken cancellationToken)
    {
        var document = await registrations.AddDocumentAsync(UserId, id, new UploadedDocument(file.FileName, file.Length, type, file.OpenReadStream), cancellationToken);
        return Created($"/api/v1/parade/registrations/{id}/documents/{document.Id}", DocumentResponse.From(document));
    }

    [HttpDelete("{id:guid}/documents/{documentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteDocument(Guid id, Guid documentId, CancellationToken cancellationToken)
    {
        await registrations.DeleteDocumentAsync(UserId, id, documentId, cancellationToken);
        return NoContent();
    }

    [HttpGet("{id:guid}/documents/{documentId:guid}/download")]
    [ProducesResponseType<DocumentLinkResponse>(StatusCodes.Status200OK)]
    public async Task<DocumentLinkResponse> Download(Guid id, Guid documentId, CancellationToken cancellationToken) =>
        new(await registrations.DocumentUrlAsync(UserId, id, documentId, cancellationToken));

    private async Task<RegistrationResponse> ResponseAsync(ParadeRegistration r, IReadOnlyList<ValidationIssue> issues, CancellationToken cancellationToken)
    {
        var (fields, canWithdraw) = await registrations.OwnerPolicyAsync(r, cancellationToken);
        var warnings = r.ValidationWarnings is null
            ? []
            : JsonSerializer.Deserialize<List<ValidationIssueResponse>>(r.ValidationWarnings, JsonSerializerOptions.Web) ?? [];
        return new RegistrationResponse(
            r.Id, Convert.ToBase64String(r.RowVersion), r.Status, r.RegistrationNumber, r.StartNumber, r.GroupName, r.ContactName, r.ContactPhone,
            r.ContactPhone is null ? null : PhoneNormalizer.Display(r.ContactPhone), r.ContactEmail, r.CategoryId, r.Subject, r.SubjectDescription,
            r.ChildrenCount, r.AdultCount, AddressDto.From(r.BuildAddress), r.JuryInspectionSameAsBuildAddress, AddressDto.From(r.JuryInspectionAddress),
            r.EstimatedLengthMeters, r.AdditionalInformation, r.SubmittedAt, r.WithdrawnAt,
            [.. fields.Order(StringComparer.Ordinal)], canWithdraw,
            [.. warnings.Select(w => w with { Severity = "Warn" })], [.. issues.Select(ValidationIssueResponse.From)],
            await registrations.ReviewReasonAsync(r, cancellationToken));
    }
}

public sealed record AddressDto(string? Street, string? HouseNumber, string? Addition, string? PostalCode, string? City, string? Country)
{
    public static AddressDto From(Address a) => new(a.Street, a.HouseNumber, a.Addition, a.PostalCode, a.City, a.Country);

    public AddressInput ToInput() => new(Street, HouseNumber, Addition, PostalCode, City, Country);
}

public sealed record UpdateRegistrationRequest(
    string? Version,
    [StringLength(100)] string? GroupName,
    [StringLength(100)] string? ContactName,
    [StringLength(30)] string? ContactPhone,
    [StringLength(254)] string? ContactEmail,
    int? CategoryId,
    [StringLength(150)] string? Subject,
    [StringLength(2000)] string? SubjectDescription,
    [Range(0, 1000)] int ChildrenCount,
    [Range(0, 1000)] int AdultCount,
    AddressDto? BuildAddress,
    bool JuryInspectionSameAsBuildAddress,
    AddressDto? JuryInspectionAddress,
    decimal? EstimatedLengthMeters,
    [StringLength(4000)] string? AdditionalInformation)
{
    public RegistrationInput ToInput() => new(GroupName, ContactName, ContactPhone, ContactEmail, CategoryId, Subject, SubjectDescription,
        ChildrenCount, AdultCount, BuildAddress?.ToInput(), JuryInspectionSameAsBuildAddress, JuryInspectionAddress?.ToInput(), EstimatedLengthMeters, AdditionalInformation);
}

public sealed record ValidationIssueResponse(string Field, string Message, string Severity)
{
    public static ValidationIssueResponse From(ValidationIssue i) => new(i.Field, i.Message, i.Severity.ToString());
}

/// <summary>
/// Inschrijving met <c>Version</c> (meesturen bij de volgende opslag), <c>EditableFields</c> (statusbeleid),
/// <c>Warnings</c> (blokkeren indienen niet) en <c>Issues</c> (alle meldingen van deze opslag, bij een concept ook blokkerende).
/// </summary>
public sealed record RegistrationResponse(
    Guid Id, string Version, RegistrationStatus Status, int? RegistrationNumber, int? StartNumber, string? GroupName, string? ContactName,
    string? ContactPhone, string? ContactPhoneDisplay, string? ContactEmail, int? CategoryId, string? Subject, string? SubjectDescription,
    int ChildrenCount, int AdultCount, AddressDto BuildAddress, bool JuryInspectionSameAsBuildAddress, AddressDto JuryInspectionAddress,
    decimal? EstimatedLengthMeters, string? AdditionalInformation, DateTime? SubmittedAt, DateTime? WithdrawnAt,
    IReadOnlyList<string> EditableFields, bool CanWithdraw, IReadOnlyList<ValidationIssueResponse> Warnings, IReadOnlyList<ValidationIssueResponse> Issues,
    string? ReviewReason);

public sealed record RegistrationSummaryResponse(
    Guid Id, string? GroupName, RegistrationStatus Status, int? RegistrationNumber, int? StartNumber, DateTime? SubmittedAt, DateTime CreatedAt);

public sealed record BuildLocationResponse(Guid Id, AddressDto Address, DateTime LastUsedAt);

public sealed record WithdrawRequest([StringLength(1000)] string? Reason);

public sealed record AddManagerRequest([Required, EmailAddress, StringLength(254)] string Email);

public sealed record ManagerResponse(Guid UserId, string DisplayName, ManagerRole Role);

public sealed record DocumentResponse(Guid Id, DocumentType DocumentType, string FileName, string ContentType, long SizeBytes, DateTime UploadedAt)
{
    public static DocumentResponse From(ParadeDocument d) => new(d.Id, d.DocumentType, d.FileName, d.ContentType, d.SizeBytes, d.UploadedAt);
}

public sealed record DocumentLinkResponse(Uri Url);
