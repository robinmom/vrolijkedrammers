using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Festival;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Programma van het Dansgardefestival (2026-10-15): tijden, blokken, schuiven, loten en afdrukken (A4).</summary>
[ApiController]
[Route("api/v1/admin/dance-festival/program")]
[RequirePermission(Permissions.FestivalManage)]
public sealed class AdminFestivalProgramController(FestivalProgramService program) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<ProgramView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ProgramView> Get(CancellationToken cancellationToken) => program.GetAsync(cancellationToken);

    /// <summary>Begintijd van het programma (de opening).</summary>
    [HttpPut("start")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SetStart(ProgramStartRequest request, CancellationToken cancellationToken)
    {
        await program.SetStartAsync(ParseTime(request.Start) ?? throw new DomainException(ErrorCodes.Validation, "Geef een begintijd."), cancellationToken);
        return NoContent();
    }

    [HttpPost("blocks")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> AddBlock(ProgramBlockRequest request, CancellationToken cancellationToken) =>
        Created((string?)null, new CreatedResponse(await program.AddBlockAsync(request.Title, request.Minutes, request.AfterItemId, cancellationToken)));

    /// <summary>Starttijd (leeg = aansluiten op het vorige), minuten per dans of duur, en bij een blok de naam.</summary>
    [HttpPut("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateItem(Guid id, ProgramItemRequest request, CancellationToken cancellationToken)
    {
        await program.UpdateItemAsync(id, ParseTime(request.Start), request.Minutes, request.Title, cancellationToken);
        return NoContent();
    }

    [HttpDelete("items/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteItem(Guid id, CancellationToken cancellationToken)
    {
        await program.DeleteBlockAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("items/{id:guid}/move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MoveItem(Guid id, ProgramMoveRequest request, CancellationToken cancellationToken)
    {
        await program.MoveItemAsync(id, request.Direction, cancellationToken);
        return NoContent();
    }

    [HttpPost("dances/{entryId:guid}/move")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MoveDance(Guid entryId, ProgramMoveRequest request, CancellationToken cancellationToken)
    {
        await program.MoveDanceAsync(entryId, request.Direction, cancellationToken);
        return NoContent();
    }

    /// <summary>Loting: de volgorde binnen elke categorie willekeurig maken.</summary>
    [HttpPost("draw")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Draw(CancellationToken cancellationToken)
    {
        await program.DrawAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Het programma als PDF (A4, huisstijl) om af te drukken.</summary>
    [HttpGet("pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<FileContentResult> Pdf(CancellationToken cancellationToken) =>
        File(FestivalPdf.Program(await program.GetAsync(cancellationToken)), "application/pdf", "programma-dansgardefestival.pdf");

    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : TimeOnly.TryParseExact(value.Trim().Replace('.', ':'), ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                ? time
                : throw new DomainException(ErrorCodes.Validation, $"\"{value}\" is geen tijd (bijv. 10:30).");
}

public sealed record ProgramStartRequest([param: Required, StringLength(5)] string Start);

public sealed record ProgramBlockRequest([param: Required, StringLength(80)] string Title, [param: Range(0, 240)] int Minutes, Guid? AfterItemId = null);

public sealed record ProgramItemRequest([param: StringLength(5)] string? Start, [param: Range(0, 240)] int Minutes, [param: StringLength(80)] string? Title = null);

public sealed record ProgramMoveRequest([param: Range(-1, 1)] int Direction);
