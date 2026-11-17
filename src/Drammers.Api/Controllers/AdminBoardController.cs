using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Modules.Membership.Board;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Bestuur en raad van elf (Leden → Bestuur): wie zit er in, sinds wanneer, en bij het bestuur ook wanneer de periode
/// van drie jaar afloopt en of die verlengd is. Met de hand bijgehouden; het portal tekent er de balk met een streep
/// per periode bij.
/// </summary>
[ApiController]
[Route("api/v1/admin/board")]
[RequirePermission(Permissions.MemberRead)]
public sealed class AdminBoardController(BoardAdministration board) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<BoardSeatView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<BoardSeatView>> GetAll(
        [FromQuery] GoverningBody? body, [FromQuery] bool includePast, CancellationToken cancellationToken) =>
        board.ListAsync(body, includePast, cancellationToken);

    [HttpPost]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreatedResponse>> Create(BoardSeatRequest request, CancellationToken cancellationToken)
    {
        var id = await board.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/board/{id}", new CreatedResponse(id));
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid id, BoardSeatRequest request, CancellationToken cancellationToken)
    {
        await board.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await board.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>De huidige bestuursperiode met één of twee jaar verlengen.</summary>
    [HttpPost("{id:guid}/extend")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<BoardSeatView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<BoardSeatView> Extend(Guid id, ExtendTermRequest request, CancellationToken cancellationToken) =>
        board.ExtendAsync(id, request.Years, request.Note, cancellationToken);

    /// <summary>Herverkozen: een nieuwe periode van drie jaar, standaard aansluitend op de vorige.</summary>
    [HttpPost("{id:guid}/renew")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<BoardSeatView>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<BoardSeatView> Renew(Guid id, RenewTermRequest request, CancellationToken cancellationToken) =>
        board.RenewAsync(id, request.StartDate, request.Note, cancellationToken);
}

public sealed record BoardTermRequest(
    [param: Required] DateOnly StartDate,
    [param: Range(0, 2)] int ExtensionYears,
    [param: StringLength(200)] string? Note);

public sealed record BoardSeatRequest(
    GoverningBody Body,
    Guid? MemberId,
    [param: StringLength(100)] string? Name,
    [param: StringLength(60)] string? Function,
    [param: Required] DateOnly StartDate,
    DateOnly? EndDate,
    [param: StringLength(2000)] string? Notes,
    IReadOnlyList<BoardTermRequest>? Terms)
{
    public BoardSeatInput ToInput() => new(Body, MemberId, Name ?? "", Function, StartDate, EndDate, Notes,
        [.. (Terms ?? []).Select(t => new BoardTermInput(t.StartDate, t.ExtensionYears, t.Note))]);
}

public sealed record ExtendTermRequest([param: Range(1, 2)] int Years, [param: StringLength(200)] string? Note);

public sealed record RenewTermRequest(DateOnly? StartDate, [param: StringLength(200)] string? Note);
