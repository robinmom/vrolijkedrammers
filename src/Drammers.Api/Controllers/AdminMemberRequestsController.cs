using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>Wijzigingsverzoeken en het verbreken van combinaties beoordelen (fase 26, ledenadministratie).</summary>
[ApiController]
[Route("api/v1/admin/member-requests")]
[RequirePermission(Permissions.MemberUpdate)]
public sealed class AdminMemberRequestsController(MemberRequests requests) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MemberRequestsOverview>(StatusCodes.Status200OK)]
    public Task<MemberRequestsOverview> Get(CancellationToken cancellationToken) => requests.OverviewAsync(cancellationToken);

    [HttpPost("changes/{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveChange(Guid id, CancellationToken cancellationToken)
    {
        await requests.ApproveChangeAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("changes/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectChange(Guid id, MemberRequestRejection request, CancellationToken cancellationToken)
    {
        await requests.RejectChangeAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    [HttpPost("breaks/{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ApproveBreak(Guid id, CancellationToken cancellationToken)
    {
        await requests.ApproveBreakAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("breaks/{id:guid}/reject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RejectBreak(Guid id, MemberRequestRejection request, CancellationToken cancellationToken)
    {
        await requests.RejectBreakAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }
}

public sealed record MemberRequestRejection([param: StringLength(500)] string Reason);
