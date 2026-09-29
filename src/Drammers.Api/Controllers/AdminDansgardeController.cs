using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Dansgarde in het portal (fase 17): alle leden met groep "Dansgarde" in e-Boekhouden (vrij veld 3), de dansgroepen en
/// het indelen. Dansgroepen zelf maak je aan als groep van het type Dansgarde (<c>/admin/groups</c>).
/// </summary>
[ApiController]
[Route("api/v1/admin/dansgarde")]
public sealed class AdminDansgardeController(Dansgarde dansgarde) : ControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<DansgardeOverview>(StatusCodes.Status200OK)]
    public Task<DansgardeOverview> Overview(CancellationToken cancellationToken) => dansgarde.OverviewAsync(cancellationToken);

    [HttpGet("groups")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<DanceGroups>(StatusCodes.Status200OK)]
    public Task<DanceGroups> Groups(CancellationToken cancellationToken) => dansgarde.GroupsAsync(cancellationToken);

    /// <summary>Deelt een dansgarde-lid in bij één dansgroep; <c>groupId</c> leeg = uit de dansgroep halen.</summary>
    [HttpPut("{memberId:guid}/group")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Assign(Guid memberId, AssignDanceGroupRequest request, CancellationToken cancellationToken)
    {
        await dansgarde.AssignAsync(memberId, request.GroupId, cancellationToken);
        return NoContent();
    }
}

public sealed record AssignDanceGroupRequest(Guid? GroupId);
