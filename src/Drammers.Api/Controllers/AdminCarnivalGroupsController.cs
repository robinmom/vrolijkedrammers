using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Het register van carnavalsgroepen (Optocht → Groepen): wie doet er mee, sinds wanneer, en per jaar hoe de wagen
/// heette, in welke categorie ze zaten en op welke plaats ze eindigden. De gegevens van de inschrijving (adres,
/// contactpersoon, aantallen) komen uit de inschrijving zelf, dus ze staan er zodra een groep zich heeft ingeschreven.
/// </summary>
[ApiController]
[Route("api/v1/admin/parade-groups")]
[RequirePermission(Permissions.ParadeRead)]
public sealed class AdminCarnivalGroupsController(CarnivalGroups groups) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CarnivalGroupSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CarnivalGroupSummary>> GetAll(
        [FromQuery] string? search, [FromQuery] bool includeInactive, CancellationToken cancellationToken) =>
        groups.ListAsync(search, includeInactive, cancellationToken);

    /// <summary>Ingediende inschrijvingen die nog aan geen groep hangen, met de groep die er het meest op lijkt.</summary>
    [HttpGet("unlinked")]
    [ProducesResponseType<IReadOnlyList<UnlinkedRegistration>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<UnlinkedRegistration>> Unlinked(CancellationToken cancellationToken) =>
        groups.UnlinkedAsync(cancellationToken);

    /// <summary>De categorieën om bij een jaar te kiezen.</summary>
    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<ParadeCategoryOption>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ParadeCategoryOption>> Categories(CancellationToken cancellationToken) =>
        groups.CategoriesAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<CarnivalGroupDetail>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<CarnivalGroupDetail> Get(Guid id, CancellationToken cancellationToken) => groups.GetAsync(id, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedResponse>> Create(CarnivalGroupRequest request, CancellationToken cancellationToken)
    {
        var id = await groups.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/parade-groups/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid id, CarnivalGroupRequest request, CancellationToken cancellationToken)
    {
        await groups.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    /// <summary>Kan alleen zolang er geen inschrijving aan hangt; anders zet je de groep op niet actief.</summary>
    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await groups.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Een jaar van de groep opslaan; ook een jaar van vóór de app, dus zonder inschrijving.</summary>
    [HttpPut("{id:guid}/years/{year:int}")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SaveYear(Guid id, int year, CarnivalGroupYearRequest request, CancellationToken cancellationToken)
    {
        await groups.SaveYearAsync(id, year, new CarnivalGroupSeasonInput(request.FloatName, request.CategoryId, request.Place, request.Notes),
            cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}/years/{year:int}")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteYear(Guid id, int year, CancellationToken cancellationToken)
    {
        await groups.DeleteYearAsync(id, year, cancellationToken);
        return NoContent();
    }

    /// <summary>Een inschrijving aan een groep hangen: aan een bestaande, of aan een nieuwe met de naam uit de inschrijving.</summary>
    [HttpPost("link")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType<LinkedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<LinkedResponse> Link(LinkRegistrationRequest request, CancellationToken cancellationToken) =>
        new(await groups.LinkAsync(request.RegistrationId, request.GroupId, request.CreateNew, cancellationToken));

    [HttpPost("unlink")]
    [RequirePermission(Permissions.ParadeManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink(UnlinkRegistrationRequest request, CancellationToken cancellationToken)
    {
        await groups.UnlinkAsync(request.RegistrationId, cancellationToken);
        return NoContent();
    }
}

public sealed record CarnivalGroupRequest(
    [param: Required, StringLength(100)] string Name,
    [param: StringLength(100)] string? ContactName,
    [param: StringLength(20)] string? ContactPhone,
    [param: StringLength(254), EmailAddress] string? ContactEmail,
    [param: Range(1900, 2200)] int? ParticipatingSince,
    [param: StringLength(2000)] string? Notes,
    bool Active)
{
    public CarnivalGroupInput ToInput() => new(Name, ContactName, ContactPhone, ContactEmail, ParticipatingSince, Notes, Active);
}

public sealed record CarnivalGroupYearRequest(
    [param: StringLength(150)] string? FloatName,
    int? CategoryId,
    [param: Range(1, 200)] int? Place,
    [param: StringLength(2000)] string? Notes);

public sealed record LinkRegistrationRequest([param: Required] Guid RegistrationId, Guid? GroupId, bool CreateNew);

public sealed record UnlinkRegistrationRequest([param: Required] Guid RegistrationId);

public sealed record LinkedResponse(Guid GroupId);
