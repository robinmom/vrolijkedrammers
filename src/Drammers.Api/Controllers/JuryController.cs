using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.ParadeManagement;
using Drammers.Modules.Parade.Judging;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Jureren in de app (fase 22b, <c>parade.judge</c>): de inzendingen van de huidige optocht met de eigen scores, scores
/// opslaan in porties (ook later vanaf een toestel zonder netwerk) en indienen.
/// </summary>
[ApiController]
[Route("api/v1/jury")]
[RequirePermission(Permissions.ParadeJudge)]
public sealed class JuryController(ParadeJudging judging) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    [HttpGet("current")]
    [ProducesResponseType<JurorSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<JurorSessionResponse> Current(CancellationToken cancellationToken)
    {
        var s = await judging.SessionAsync(UserId, cancellationToken);
        return new JurorSessionResponse(s.ParadeId, s.ParadeName, s.ParadeDate, s.StartTime, s.SubmittedAt,
            [.. s.Categories.Select(c => new JudgingCategoryRefResponse(c.Id, c.Name))],
            [.. s.Entries.Select(e => new JudgingEntryResponse(e.RegistrationId, e.StartNumber, e.GroupName, e.Motto, e.CategoryId, e.CategoryName, e.Assigned))],
            [.. s.Scores.Select(x => new JudgingScoreResponse(x.RegistrationId, x.Pass, x.Criterion, x.Value, x.ScoredAt))]);
    }

    [HttpPut("parades/{paradeId:guid}/scores")]
    [ProducesResponseType<SaveScoresResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<SaveScoresResponse> SaveScores(Guid paradeId, SaveScoresRequest request, CancellationToken cancellationToken) =>
        new(await judging.SaveScoresAsync(UserId, paradeId,
            [.. request.Scores.Select(s => new ScoreInput(s.RegistrationId, s.Pass, s.Criterion, s.Value, s.ScoredAt))], cancellationToken));

    [HttpPost("parades/{paradeId:guid}/submit")]
    [ProducesResponseType<SubmitJudgingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<SubmitJudgingResponse> Submit(Guid paradeId, CancellationToken cancellationToken) =>
        new(await judging.SubmitAsync(UserId, paradeId, cancellationToken));
}

public sealed record JurorSessionResponse(
    Guid ParadeId, string ParadeName, DateOnly ParadeDate, TimeOnly StartTime, DateTime? SubmittedAt,
    IReadOnlyList<JudgingCategoryRefResponse> Categories, IReadOnlyList<JudgingEntryResponse> Entries, IReadOnlyList<JudgingScoreResponse> Scores);

public sealed record JudgingCategoryRefResponse(int Id, string Name);

public sealed record JudgingEntryResponse(Guid RegistrationId, int? StartNumber, string GroupName, string? Motto, int CategoryId, string CategoryName, bool Assigned);

public sealed record JudgingScoreResponse(Guid RegistrationId, int Pass, JudgingCriterion Criterion, int Value, DateTime ScoredAt);

public sealed record ScoreRequest(Guid RegistrationId, [Range(1, 3)] int Pass, JudgingCriterion Criterion, [Range(0, 100)] int Value, DateTime ScoredAt);

public sealed record SaveScoresRequest([Required, MaxLength(1000)] IReadOnlyList<ScoreRequest> Scores);

public sealed record SaveScoresResponse(int Changed);

public sealed record SubmitJudgingResponse(DateTime SubmittedAt);
