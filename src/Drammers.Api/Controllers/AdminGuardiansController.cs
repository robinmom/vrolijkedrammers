using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Guardians;
using Drammers.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>
/// Ouders en verzorgers (fase 17): per lid koppelen, uitnodigen en ontkoppelen, koppelverzoeken uit de app beoordelen,
/// voorstellen op basis van hetzelfde e-mailadres bevestigen of afwijzen, en "Eigen account geven" vanaf 15.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
public sealed class AdminGuardiansController(Guardians guardians, DrammersDbContext db) : ControllerBase
{
    [HttpGet("members/{id:guid}/guardians")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<MemberGuardians>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<MemberGuardians> Get(Guid id, CancellationToken cancellationToken) => guardians.ForMemberAsync(id, cancellationToken);

    /// <summary>Koppelt een bestaand app-account als ouder/verzorger (hooguit 2 per kind, alleen onder 18).</summary>
    [HttpPost("members/{id:guid}/guardians")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Link(Guid id, LinkGuardianRequest request, CancellationToken cancellationToken)
    {
        await guardians.LinkAsync(id, request.UserId, request.Relationship, cancellationToken);
        return NoContent();
    }

    /// <summary>Nodigt een ouder uit per e-mail: een nieuw account met alleen de rol Ouder/verzorger, of het bestaande account.</summary>
    [HttpPost("members/{id:guid}/guardians/invite")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Invite(Guid id, InviteGuardianRequest request, CancellationToken cancellationToken)
    {
        await guardians.InviteAsync(id, request.Email, request.Name, request.Relationship, cancellationToken);
        return NoContent();
    }

    [HttpDelete("members/{id:guid}/guardians/{relationId:guid}")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlink(Guid id, Guid relationId, CancellationToken cancellationToken)
    {
        await guardians.UnlinkAsync(id, relationId, cancellationToken);
        return NoContent();
    }

    /// <summary>Eigen account op het eigen e-mailadres (vanaf 15); de ouders blijven tot 18 gekoppeld voor meldingen.</summary>
    [HttpPost("members/{id:guid}/own-account")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GiveOwnAccount(Guid id, OwnAccountRequest request, CancellationToken cancellationToken)
    {
        await guardians.GiveOwnAccountAsync(id, request.Email, cancellationToken);
        return Accepted();
    }

    /// <summary>Accounts om als ouder te koppelen (zoeken op naam of e-mail).</summary>
    [HttpGet("guardian-candidates")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType<IReadOnlyList<GuardianCandidateResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<GuardianCandidateResponse>> Candidates([FromQuery, StringLength(100)] string? search, CancellationToken cancellationToken)
    {
        var term = search?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2)
        {
            return [];
        }

        return await db.Users.AsNoTracking()
            .Where(u => u.AccountStatus != AccountStatus.Deleted && (u.DisplayName.Contains(term) || u.Email.Contains(term)))
            .OrderBy(u => u.DisplayName).Take(10)
            .Select(u => new GuardianCandidateResponse(u.Id, u.DisplayName, u.Email, u.MemberId != null))
            .ToListAsync(cancellationToken);
    }

    [HttpGet("guardian-requests")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<IReadOnlyList<GuardianRequestView>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<GuardianRequestView>> Requests([FromQuery] GuardianLinkRequestStatus? status, CancellationToken cancellationToken) =>
        guardians.RequestsAsync(status, cancellationToken);

    [HttpPost("guardian-requests/{id:guid}/approve")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Approve(Guid id, ApproveGuardianRequest request, CancellationToken cancellationToken)
    {
        await guardians.ApproveRequestAsync(id, request.MemberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("guardian-requests/{id:guid}/reject")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(Guid id, RejectGuardianRequest request, CancellationToken cancellationToken)
    {
        await guardians.RejectRequestAsync(id, request.Reason, cancellationToken);
        return NoContent();
    }

    /// <summary>Voorstellen: een lid jonger dan 15 en een ander lid met hetzelfde e-mailadres. Er wordt niets vanzelf gekoppeld.</summary>
    [HttpGet("guardian-suggestions")]
    [RequirePermission(Permissions.MemberRead)]
    [ProducesResponseType<IReadOnlyList<GuardianSuggestion>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<GuardianSuggestion>> Suggestions(CancellationToken cancellationToken) => guardians.SuggestionsAsync(cancellationToken);

    [HttpPost("guardian-suggestions/link")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AcceptSuggestion(GuardianSuggestionRequest request, CancellationToken cancellationToken)
    {
        await guardians.AcceptSuggestionAsync(request.ChildMemberId, request.ParentMemberId, cancellationToken);
        return NoContent();
    }

    [HttpPost("guardian-suggestions/dismiss")]
    [RequirePermission(Permissions.MemberUpdate)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DismissSuggestion(GuardianSuggestionRequest request, CancellationToken cancellationToken)
    {
        await guardians.DismissSuggestionAsync(request.ChildMemberId, request.ParentMemberId, cancellationToken);
        return NoContent();
    }
}

public sealed record LinkGuardianRequest(Guid UserId, GuardianRelationship Relationship);

public sealed record InviteGuardianRequest(
    [param: Required, EmailAddress, StringLength(254)] string Email,
    [param: Required, StringLength(100)] string Name,
    GuardianRelationship Relationship);

public sealed record OwnAccountRequest([param: Required, EmailAddress, StringLength(254)] string Email);

public sealed record GuardianCandidateResponse(Guid UserId, string Name, string Email, bool IsMember);

public sealed record ApproveGuardianRequest(Guid MemberId);

public sealed record RejectGuardianRequest([param: StringLength(500)] string? Reason);

public sealed record GuardianSuggestionRequest(Guid ChildMemberId, Guid ParentMemberId);
