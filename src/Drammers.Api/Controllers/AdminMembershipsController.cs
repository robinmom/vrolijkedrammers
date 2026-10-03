using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Infrastructure.Members;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Mvc;

namespace Drammers.Api.Controllers;

/// <summary>
/// Lidmaatschappen (fase 25): overzicht per soort en tweepersoonslidmaatschappen splitsen. De tarieven staan onder
/// <c>/admin/contributions/rates</c>.
/// </summary>
[ApiController]
[Route("api/v1/admin/memberships")]
[RequirePermission(Permissions.ContributionManage)]
public sealed class AdminMembershipsController(Contributions contributions, MemberSplits splits, IClock clock) : ControllerBase
{
    /// <summary>Aantal actieve leden per soort lidmaatschap, op vandaag.</summary>
    [HttpGet("overview")]
    [ProducesResponseType<MembershipOverviewResponse>(StatusCodes.Status200OK)]
    public async Task<MembershipOverviewResponse> Overview(CancellationToken cancellationToken)
    {
        var overview = await contributions.BuildAsync(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime), cancellationToken);
        string Group(ContributionLine l) => l.Kind switch
        {
            MembershipKind.OnePerson => l.Senior ? "Lid (65+)" : "Lid",
            MembershipKind.TwoPersons when l.PartnerMemberId is not null => l.Senior ? "Combinatie (65+)" : "Combinatie",
            MembershipKind.TwoPersons => "Tweepersoonslid, nog niet gesplitst",
            MembershipKind.Partner => "Tweede lid van een combinatie",
            MembershipKind.Dansgarde => "Dansgarde",
            _ => "Onbekend",
        };
        var rows = overview.Lines.GroupBy(Group).Select(g => new MembershipCountResponse(g.Key, g.Count())).OrderBy(r => r.Label, StringComparer.Ordinal).ToList();
        return new MembershipOverviewResponse(overview.Lines.Count, overview.Lines.Count(l => l.Status == ContributionStatus.Exempt), rows);
    }

    /// <summary>Actieve tweepersoonsleden zonder gekoppeld tweede lid, met de stand van de uitnodiging.</summary>
    [HttpGet("splits")]
    [ProducesResponseType<IReadOnlyList<SplitCandidate>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<SplitCandidate>> Splits(CancellationToken cancellationToken) => splits.CandidatesAsync(cancellationToken);

    /// <summary>Hoofdleden mailen met een link om het tweede lid te registreren; zonder <c>MemberIds</c> iedereen die nog niet gemaild is.</summary>
    [HttpPost("splits/invite")]
    [ProducesResponseType<SplitInviteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public Task<SplitInviteResult> Invite(SplitInviteRequest request, CancellationToken cancellationToken) =>
        splits.InviteAsync(request.MemberIds, $"{Request.Scheme}://{Request.Host}", cancellationToken);
}

public sealed record MembershipCountResponse(string Label, int Count);

public sealed record MembershipOverviewResponse(int Active, int Exempt, IReadOnlyList<MembershipCountResponse> ByKind);

public sealed record SplitInviteRequest([param: MaxLength(1000)] IReadOnlyList<Guid>? MemberIds);
