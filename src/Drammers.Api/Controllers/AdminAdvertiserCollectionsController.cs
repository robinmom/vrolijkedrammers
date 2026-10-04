using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Advertisers;
using Drammers.Infrastructure.Members;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Incasso van de adverteerders (fase 27c): alle opgehaalde bijdragen met een machtiging van een campagnejaar in één
/// pain.008-bestand, met dezelfde gegevens van de vereniging als de contributie.
/// </summary>
[ApiController]
[Route("api/v1/admin/advertisers/collections")]
[RequirePermission(Permissions.AdvertiserManage)]
public sealed class AdminAdvertiserCollectionsController(SepaCollections collections, AdvertiserAdministration advertisers) : ControllerBase
{
    /// <summary>Wie er in de incasso komt en wie niet (met reden); zonder jaar het lopende campagnejaar. Er wordt niets vastgelegd.</summary>
    [HttpGet("preview")]
    [ProducesResponseType<CollectionPreview>(StatusCodes.Status200OK)]
    public async Task<CollectionPreview> Preview([FromQuery, Required] DateOnly date, [FromQuery] int? year, CancellationToken cancellationToken) =>
        await collections.PreviewAdvertisersAsync(date, year ?? await advertisers.CampaignYearAsync(cancellationToken), cancellationToken);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CollectionRunSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CollectionRunSummary>> Runs(CancellationToken cancellationToken) =>
        collections.RunsAsync(cancellationToken, CollectionKind.Advertisers);

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(AdvertiserCollectionRequest request, CancellationToken cancellationToken)
    {
        var year = request.Year ?? await advertisers.CampaignYearAsync(cancellationToken);
        var id = await collections.CreateAdvertisersAsync(request.Date, year, request.Description, cancellationToken);
        return Created((string?)null, new CreatedResponse(id));
    }

    /// <summary>Het pain.008.001.08-bestand om aan te leveren bij de bank; geaudit.</summary>
    [HttpGet("{id:guid}/file")]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<FileContentResult> File(Guid id, CancellationToken cancellationToken)
    {
        var (name, content) = await collections.FileAsync(id, cancellationToken, CollectionKind.Advertisers);
        return File(content, "application/xml", name.Replace("incasso-", "incasso-adverteerders-", StringComparison.Ordinal));
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await collections.DeleteAsync(id, cancellationToken, CollectionKind.Advertisers);
        return NoContent();
    }
}

public sealed record AdvertiserCollectionRequest(DateOnly Date, [param: Range(2000, 2100)] int? Year, [param: StringLength(100)] string? Description);
