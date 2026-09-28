using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Ticketing;
using Drammers.Modules.Ticketing.Tickets;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Ledentickets beheren (fase 13): overzicht met <c>ticket.read</c>, acties met <c>ticket.manage</c>.</summary>
[ApiController]
[Route("api/v1/admin/tickets")]
[RequirePermission(Permissions.TicketRead)]
public sealed class AdminTicketsController(TicketAdministration administration) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<TicketSummary>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<TicketSummary>> Search([FromQuery] string? search, [FromQuery] TicketStatus? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<TicketSummary>.Normalize(page, pageSize);
        var (items, total) = await administration.SearchAsync(search, status, p, size, cancellationToken);
        return new PagedResult<TicketSummary>(items, p, size, total);
    }

    /// <summary>Ledentickets uitgeven aan alle actieve leden die er nog geen hebben (idempotent).</summary>
    [HttpPost("issue")]
    [RequirePermission(Permissions.TicketManage)]
    [ProducesResponseType<IssueTicketsResponse>(StatusCodes.Status200OK)]
    public async Task<IssueTicketsResponse> Issue(CancellationToken cancellationToken) => new(await administration.IssueForActiveMembersAsync(cancellationToken));

    [HttpPost("{id:guid}/action")]
    [RequirePermission(Permissions.TicketManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Act(Guid id, TicketActionRequest request, CancellationToken cancellationToken)
    {
        await administration.ApplyAsync(id, request.Action, request.Reason, cancellationToken);
        return NoContent();
    }
}

public sealed record IssueTicketsResponse(int Issued);

public sealed record TicketActionRequest(TicketAction Action, [StringLength(500)] string? Reason);
