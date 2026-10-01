using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// De jury van de optocht (fase 22a). Bekijken en juryleden indelen in categorieën mag het bestuur en de hoofdjury
/// (<c>jury.assign</c>); uitnodigen, hoofdjury aanwijzen, weging instellen en uit de jury halen alleen het bestuur
/// (<c>jury.manage</c>). Zonder <c>paradeId</c> gaat het om de huidige optocht.
/// </summary>
[ApiController]
[Route("api/v1/admin/jury")]
[RequirePermission(Permissions.JuryAssign)]
public sealed class AdminJuryController(ParadeJury jury) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<JuryOverviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<JuryOverviewResponse> Get([FromQuery] Guid? paradeId, CancellationToken cancellationToken)
    {
        var o = await jury.OverviewAsync(paradeId, cancellationToken);
        return new JuryOverviewResponse(o.ParadeId, o.ParadeName, o.ParadeDate,
            [.. o.Jurors.Select(j => new JurorResponse(j.UserId, j.Name, j.Email, j.Invited, j.HeadJury, j.CategoryIds))],
            [.. o.Categories.Select(c => new JudgingCategoryResponse(c.CategoryId, c.Name, c.Judged, c.WeightOriginality, c.WeightCarnivalesque,
                c.WeightQuality, c.WeightOverall, c.JurorCount, c.EntryCount))]);
    }

    [HttpPost("jurors")]
    [RequirePermission(Permissions.JuryManage)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Invite(InviteJurorRequest request, CancellationToken cancellationToken)
    {
        var id = await jury.InviteAsync(request.Name, request.Email, cancellationToken);
        return Created($"/api/v1/admin/jury/jurors/{id}", new CreatedResponse(id));
    }

    /// <summary>De categorieën van een jurylid in deze optocht.</summary>
    [HttpPut("parades/{paradeId:guid}/jurors/{userId:guid}/categories")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetCategories(Guid paradeId, Guid userId, JurorCategoriesRequest request, CancellationToken cancellationToken)
    {
        await jury.SetCategoriesAsync(paradeId, userId, request.CategoryIds, cancellationToken);
        return NoContent();
    }

    [HttpPut("jurors/{userId:guid}/head-jury")]
    [RequirePermission(Permissions.JuryManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetHeadJury(Guid userId, HeadJuryRequest request, CancellationToken cancellationToken)
    {
        await jury.SetHeadJuryAsync(userId, request.HeadJury, cancellationToken);
        return NoContent();
    }

    [HttpPost("jurors/{userId:guid}/resend-invite")]
    [RequirePermission(Permissions.JuryManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResendInvite(Guid userId, CancellationToken cancellationToken)
    {
        await jury.ResendInviteAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>Uit de jury halen (rollen Jury en Hoofdjury eraf, geen categorieën meer).</summary>
    [HttpDelete("jurors/{userId:guid}")]
    [RequirePermission(Permissions.JuryManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        await jury.RemoveAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpPut("parades/{paradeId:guid}/categories/{categoryId:int}")]
    [RequirePermission(Permissions.JuryManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetWeights(Guid paradeId, int categoryId, JudgingWeightsRequest request, CancellationToken cancellationToken)
    {
        await jury.SetWeightsAsync(paradeId, categoryId,
            new JudgingWeights(request.Judged, request.Originality, request.Carnivalesque, request.Quality, request.Overall), cancellationToken);
        return NoContent();
    }
}

public sealed record JuryOverviewResponse(
    Guid ParadeId, string ParadeName, DateOnly ParadeDate, IReadOnlyList<JurorResponse> Jurors, IReadOnlyList<JudgingCategoryResponse> Categories);

public sealed record JurorResponse(Guid UserId, string Name, string Email, bool Invited, bool HeadJury, IReadOnlyList<int> CategoryIds);

public sealed record JudgingCategoryResponse(
    int CategoryId, string Name, bool Judged, int Originality, int Carnivalesque, int Quality, int Overall, int JurorCount, int EntryCount);

public sealed record InviteJurorRequest([Required, StringLength(100)] string Name, [Required, EmailAddress, StringLength(254)] string Email);

public sealed record JurorCategoriesRequest([Required] IReadOnlyList<int> CategoryIds);

public sealed record HeadJuryRequest(bool HeadJury);

public sealed record JudgingWeightsRequest(
    bool Judged, [Range(0, 5)] int Originality, [Range(0, 5)] int Carnivalesque, [Range(0, 5)] int Quality, [Range(0, 5)] int Overall);
