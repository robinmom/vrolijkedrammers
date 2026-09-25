using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Membership.Groups;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Groepen (bijv. Jeugdcommissie, Dansgarde) als doelgroep voor content en later meldingen (fase 8, docs/04 §4).</summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminGroupsController(DrammersDbContext db, GroupAdministration groups) : ControllerBase
{
    [HttpGet("groups")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<IReadOnlyList<GroupSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<GroupSummaryResponse>> GetAll(CancellationToken cancellationToken) =>
        await db.Groups.AsNoTracking().OrderBy(g => g.Name)
            .Select(g => new GroupSummaryResponse(g.Id, g.Name, g.Description, g.Type, g.CarnivalYearId, g.Active, g.Memberships.Count))
            .ToListAsync(cancellationToken);

    [HttpGet("groups/{id:guid}")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<GroupDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<GroupDetailResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var group = await db.Groups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.GroupNotFound, "Groep niet gevonden.", DomainErrorKind.NotFound);
        var members = await db.GroupMemberships.AsNoTracking().Where(m => m.GroupId == id)
            .Join(db.Members, gm => gm.MemberId, m => m.Id, (gm, m) => new { gm, m })
            .OrderBy(x => x.m.LastName).ThenBy(x => x.m.FullName)
            .Select(x => new GroupMemberResponse(x.m.Id, x.m.MemberNumber, x.m.FullName, x.gm.Function, x.gm.ValidFrom, x.gm.ValidTo))
            .ToListAsync(cancellationToken);
        return new GroupDetailResponse(group.Id, group.Name, group.Description, group.Type, group.CarnivalYearId, group.Active, members);
    }

    [HttpPost("groups")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreatedResponse>> Create(GroupRequest request, CancellationToken cancellationToken)
    {
        var id = await groups.CreateAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/groups/{id}", new CreatedResponse(id));
    }

    [HttpPut("groups/{id:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, GroupRequest request, CancellationToken cancellationToken)
    {
        await groups.UpdateAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("groups/{id:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await groups.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPut("groups/{id:guid}/members/{memberId:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetMember(Guid id, Guid memberId, GroupMemberRequest request, CancellationToken cancellationToken)
    {
        await groups.SetMemberAsync(id, memberId, new GroupMemberInput(request.Function, request.ValidFrom, request.ValidTo), cancellationToken);
        return NoContent();
    }

    [HttpDelete("groups/{id:guid}/members/{memberId:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId, CancellationToken cancellationToken)
    {
        await groups.RemoveMemberAsync(id, memberId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Groepen als keuzelijst bij het publiceren van content (alleen naam, geen leden). Voor redacteuren, die geen
    /// ledengegevens mogen zien.
    /// </summary>
    [HttpGet("content-audiences/groups")]
    [RequirePermission(Permissions.EventManage)]
    [ProducesResponseType<IReadOnlyList<AudienceOptionResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AudienceOptionResponse>> AudienceGroups(CancellationToken cancellationToken) =>
        await db.Groups.AsNoTracking().Where(g => g.Active).OrderBy(g => g.Name)
            .Select(g => new AudienceOptionResponse(g.Id, g.Name)).ToListAsync(cancellationToken);
}

public sealed record GroupRequest(
    [System.ComponentModel.DataAnnotations.Required] string Name, string? Description, GroupType Type, int? CarnivalYearId, bool Active = true)
{
    public GroupInput ToInput() => new(Name, Description, Type, CarnivalYearId, Active);
}

public sealed record GroupMemberRequest(GroupFunction Function, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record GroupSummaryResponse(Guid Id, string Name, string? Description, GroupType Type, int? CarnivalYearId, bool Active, int MemberCount);

public sealed record GroupMemberResponse(Guid MemberId, string MemberNumber, string FullName, GroupFunction Function, DateOnly? ValidFrom, DateOnly? ValidTo);

public sealed record GroupDetailResponse(
    Guid Id, string Name, string? Description, GroupType Type, int? CarnivalYearId, bool Active, IReadOnlyList<GroupMemberResponse> Members);

public sealed record AudienceOptionResponse(Guid Id, string Name);
