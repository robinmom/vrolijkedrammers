using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>SEPA-incasso van de contributie (fase 23c): gegevens van de vereniging, voorbeeld, runs en het pain.008-bestand.</summary>
[ApiController]
[Route("api/v1/admin/collections")]
[RequirePermission(Permissions.ContributionManage)]
public sealed class AdminCollectionsController(SepaCollections collections) : ControllerBase
{
    [HttpGet("creditor")]
    [ProducesResponseType<SepaCreditor>(StatusCodes.Status200OK)]
    public Task<SepaCreditor> GetCreditor(CancellationToken cancellationToken) => collections.GetCreditorAsync(cancellationToken);

    [HttpPut("creditor")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> PutCreditor(SepaCreditorRequest request, CancellationToken cancellationToken)
    {
        await collections.SetCreditorAsync(new SepaCreditor(request.Name, request.Iban, request.CreditorId), cancellationToken);
        return NoContent();
    }

    /// <summary>Wie er op deze incassodatum meedoet en wie niet (met reden); er wordt niets vastgelegd.</summary>
    [HttpGet("preview")]
    [ProducesResponseType<CollectionPreview>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<CollectionPreview> Preview([FromQuery, Required] DateOnly date, CancellationToken cancellationToken) =>
        collections.PreviewAsync(date, cancellationToken);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CollectionRunSummary>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<CollectionRunSummary>> Runs(CancellationToken cancellationToken) => collections.RunsAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(CollectionRunRequest request, CancellationToken cancellationToken)
    {
        var id = await collections.CreateAsync(request.Date, request.Description, cancellationToken);
        return Created((string?)null, new CreatedResponse(id));
    }

    /// <summary>Het pain.008.001.08-bestand om aan te leveren bij de bank; geaudit.</summary>
    [HttpGet("{id:guid}/file")]
    [Produces("application/xml")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<FileContentResult> File(Guid id, CancellationToken cancellationToken)
    {
        var (name, content) = await collections.FileAsync(id, cancellationToken);
        return File(content, "application/xml", name);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await collections.DeleteAsync(id, cancellationToken);
        return NoContent();
    }
}

public sealed record SepaCreditorRequest(
    [param: Required, StringLength(70)] string Name,
    [param: Required, StringLength(40)] string Iban,
    [param: Required, StringLength(35)] string CreditorId);

public sealed record CollectionRunRequest(DateOnly Date, [param: StringLength(100)] string? Description);
