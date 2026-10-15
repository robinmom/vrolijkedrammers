using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Festival;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Jureren op het Dansgardefestival in de app (2026-10-15): alleen de categorie die aan de beurt is.</summary>
[ApiController]
[Route("api/v1/festival-jury")]
[RequirePermission(Permissions.FestivalJudge)]
public sealed class FestivalJuryController(FestivalJudgingService judging) : ControllerBase
{
    private Guid UserId => CurrentUser.Get(HttpContext)!.UserId;

    [HttpGet]
    [ProducesResponseType<FestivalJurySession>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<FestivalJurySession> Session(CancellationToken cancellationToken) => judging.SessionAsync(UserId, cancellationToken);

    /// <summary>Punten en opmerkingen (in porties; per onderdeel wint de nieuwste).</summary>
    [HttpPut("scores")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Save(FestivalScoresRequest request, CancellationToken cancellationToken)
    {
        await judging.SaveAsync(UserId, request.Scores ?? [], request.Comments ?? [], cancellationToken);
        return NoContent();
    }

    [HttpPost("submit")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Submit(FestivalSubmitRequest request, CancellationToken cancellationToken)
    {
        await judging.SubmitCategoryAsync(UserId, request.CategoryKey, cancellationToken);
        return NoContent();
    }
}

/// <summary>De hoofdjury in de app (2026-10-15): de uitslag voor de prijsuitreiking en "prijsuitreiking afgerond".</summary>
[ApiController]
[Route("api/v1/festival-jury/head")]
[RequirePermission(Permissions.FestivalHeadJury)]
public sealed class FestivalHeadJuryController(FestivalJudgingService judging) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<HeadJuryView>(StatusCodes.Status200OK)]
    public Task<HeadJuryView> Get(CancellationToken cancellationToken) => judging.HeadJuryAsync(cancellationToken);

    [HttpPost("awards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Awards(FestivalAwardsRequest request, CancellationToken cancellationToken)
    {
        await judging.SetAwardsCompletedAsync(request.Completed, byOrganisation: false, cancellationToken);
        return NoContent();
    }
}

/// <summary>Juryleden van het Dansgardefestival (portal): uitnodigen, hoofdjury aanwijzen, verwijderen.</summary>
[ApiController]
[Route("api/v1/admin/dance-festival/jury")]
[RequirePermission(Permissions.FestivalManage)]
public sealed class AdminFestivalJuryController(FestivalJudgingService judging) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<FestivalJurorView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<FestivalJurorView>> Jurors(CancellationToken cancellationToken) => judging.JurorsAsync(cancellationToken);

    [HttpPost]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Invite(FestivalJurorRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await judging.InviteAsync(request.Name, request.Email, request.HeadJury, cancellationToken)));

    [HttpPut("{userId:guid}/head")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Head(Guid userId, FestivalHeadRequest request, CancellationToken cancellationToken)
    {
        await judging.SetHeadJuryAsync(userId, request.HeadJury, cancellationToken);
        return NoContent();
    }

    [HttpPost("{userId:guid}/resend")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resend(Guid userId, CancellationToken cancellationToken)
    {
        await judging.ResendInviteAsync(userId, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        await judging.RemoveAsync(userId, cancellationToken);
        return NoContent();
    }
}

/// <summary>Uitslag van het Dansgardefestival (portal): per categorie, naar de hoofdjury, prijsuitreiking overrulen, afdrukken.</summary>
[ApiController]
[Route("api/v1/admin/dance-festival/results")]
[RequirePermission(Permissions.FestivalManage)]
public sealed class AdminFestivalResultsController(FestivalJudgingService judging) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<FestivalResults>(StatusCodes.Status200OK)]
    public Task<FestivalResults> Get(CancellationToken cancellationToken) => judging.ResultsAsync(cancellationToken);

    /// <summary>De uitslag naar de hoofdjury; met <c>force</c> ook als nog niet alles binnen is.</summary>
    [HttpPost("head-jury")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PublishToHeadJury(FestivalPublishRequest request, CancellationToken cancellationToken)
    {
        await judging.PublishToHeadJuryAsync(request.Force, cancellationToken);
        return NoContent();
    }

    /// <summary>Prijsuitreiking afgerond zetten of terugdraaien (overrulen van de hoofdjury).</summary>
    [HttpPost("awards")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Awards(FestivalAwardsRequest request, CancellationToken cancellationToken)
    {
        await judging.SetAwardsCompletedAsync(request.Completed, byOrganisation: true, cancellationToken);
        return NoContent();
    }

    [HttpGet("pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Pdf(CancellationToken cancellationToken) =>
        File(FestivalPdf.Results(await judging.ResultsAsync(cancellationToken)), "application/pdf", "uitslag-dansgardefestival.pdf");

    /// <summary>Staat de uitslag op de website en hoeveel rapporten zijn verstuurd.</summary>
    [HttpGet("publish")]
    [ProducesResponseType<FestivalPublishStatus>(StatusCodes.Status200OK)]
    public Task<FestivalPublishStatus> PublishStatus([FromServices] FestivalResultsPublisher publisher, CancellationToken cancellationToken) =>
        publisher.StatusAsync(cancellationToken);

    /// <summary>De uitslag als nieuwsbericht op de website (pas na de prijsuitreiking).</summary>
    [HttpPost("website")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<CreatedResponse> Website([FromServices] FestivalResultsPublisher publisher, CancellationToken cancellationToken) =>
        new(await publisher.PublishWebsiteAsync(cancellationToken));

    /// <summary>De juryrapporten naar de verenigingen (pas na de prijsuitreiking; wie er al een heeft, krijgt geen tweede).</summary>
    [HttpPost("reports")]
    [ProducesResponseType<FestivalReportsQueued>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<FestivalReportsQueued> Reports([FromServices] FestivalResultsPublisher publisher, CancellationToken cancellationToken) =>
        new(await publisher.SendReportsAsync(cancellationToken));

    /// <summary>Voorbeeld van het rapport van één inschrijving (PDF).</summary>
    [HttpGet("reports/{registrationId:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReportPdf(Guid registrationId, [FromServices] FestivalResultsPublisher publisher, CancellationToken cancellationToken) =>
        await publisher.ReportAsync(registrationId, cancellationToken) is { } report
            ? File(FestivalPdf.Report(report), "application/pdf", $"juryrapport-{report.ClubName}.pdf")
            : NotFound();
}

public sealed record FestivalReportsQueued(int Queued);

public sealed record FestivalScoresRequest([param: MaxLength(1000)] IReadOnlyList<FestivalScoreInput>? Scores, [param: MaxLength(200)] IReadOnlyList<FestivalCommentInput>? Comments);

public sealed record FestivalSubmitRequest([param: Required, StringLength(60)] string CategoryKey);

public sealed record FestivalAwardsRequest(bool Completed);

public sealed record FestivalPublishRequest(bool Force = false);

public sealed record FestivalJurorRequest([param: Required, StringLength(100)] string Name, [param: Required, EmailAddress, StringLength(254)] string Email, bool HeadJury = false);

public sealed record FestivalHeadRequest(bool HeadJury);
