using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Applications;
using Drammers.Modules.Membership.Groups;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Members;

public sealed record DanceGroupRef(Guid Id, string Name);

public sealed record DansgardeMember(
    Guid MemberId, string FullName, string MemberNumber, DateOnly? BirthDate, int? Age, DanceGroupRef? DanceGroup,
    IReadOnlyList<string> Guardians, bool HasSuggestion, bool OwnAccount, DateOnly? TurnsFifteenOn);

public sealed record DansgardeOverview(
    int Total, int WithoutGroup, int WithoutGuardian, int TurningFifteenSoon, IReadOnlyList<DansgardeMember> Members,
    IReadOnlyList<DanceGroupRef> Groups);

public sealed record DanceGroupMember(Guid MemberId, string FullName, int? Age);

public sealed record DanceGroup(
    Guid Id, string Name, string? Description, bool Active, IReadOnlyList<string> Leaders, IReadOnlyList<DanceGroupMember> Members);

public sealed record DanceGroups(IReadOnlyList<DanceGroup> Groups, IReadOnlyList<DanceGroupMember> Unassigned);

/// <summary>
/// Dansgarde (fase 17): wie bij de dansgarde hoort, volgt uit het vrije veld "groep" in e-Boekhouden (vrij veld 3,
/// <see cref="Member.ParadeGroupName"/>) met de waarde "Dansgarde". De dansgroepen (Mini Drammers, Drammerinekes, …)
/// zijn portalgroepen van het type <see cref="GroupType.DanceGuard"/>; een dansgarde-lid zit in hooguit één dansgroep.
/// </summary>
public sealed class Dansgarde(DrammersDbContext db, Guardians guardians, IAuditLogger audit, IClock clock)
{
    public const string GroupValue = "Dansgarde";

    /// <summary>Tot zoveel dagen vooruit meldt het overzicht "wordt binnenkort 15".</summary>
    public const int TurningFifteenWindowDays = 60;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsDansgarde(string? groupValue) =>
        string.Equals(groupValue?.Trim(), GroupValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>Actieve dansgarde-leden (SQL Server vergelijkt zonder hoofdletters en spaties achteraan).</summary>
    public static IQueryable<Member> Members(DrammersDbContext db) =>
        db.Members.Where(m => m.ParadeGroupName == GroupValue && (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active);

    public async Task<DansgardeOverview> OverviewAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var members = await Members(db).AsNoTracking()
            .Select(m => new { m.Id, m.FullName, m.MemberNumber, m.BirthDate })
            .ToListAsync(cancellationToken);
        var ids = members.Select(m => m.Id).ToList();
        var groups = await db.Groups.AsNoTracking().Where(g => g.Type == GroupType.DanceGuard && g.Active)
            .OrderBy(g => g.Name).Select(g => new DanceGroupRef(g.Id, g.Name)).ToListAsync(cancellationToken);
        var groupIds = groups.Select(g => g.Id).ToList();
        var memberships = (await db.GroupMemberships.AsNoTracking()
            .Where(gm => groupIds.Contains(gm.GroupId) && ids.Contains(gm.MemberId) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Select(gm => new { gm.MemberId, gm.GroupId }).ToListAsync(cancellationToken))
            .GroupBy(gm => gm.MemberId).ToDictionary(g => g.Key, g => g.First().GroupId);
        var relations = (await db.GuardianRelations.AsNoTracking().Where(g => ids.Contains(g.MemberId))
            .Join(db.Users, g => g.GuardianUserId, u => u.Id, (g, u) => new { g.MemberId, u.DisplayName, g.CreatedAt })
            .ToListAsync(cancellationToken)).OrderBy(g => g.CreatedAt).ToLookup(g => g.MemberId, g => g.DisplayName);
        var withAccount = (await db.Users.AsNoTracking().Where(u => u.MemberId != null && ids.Contains(u.MemberId.Value) && u.AccountStatus != AccountStatus.Deleted)
            .Select(u => u.MemberId!.Value).ToListAsync(cancellationToken)).ToHashSet();
        var suggested = (await guardians.SuggestionsAsync(cancellationToken)).Select(s => s.ChildMemberId).ToHashSet();

        var rows = members.Select(m =>
        {
            var age = Guardians.AgeOn(m.BirthDate, today);
            var fifteen = m.BirthDate?.AddYears(MembershipApplication.MinimumAgeOwnAccount);
            var soon = fifteen is { } f && f > today && f <= today.AddDays(TurningFifteenWindowDays) ? fifteen : null;
            var group = memberships.TryGetValue(m.Id, out var gid) ? groups.First(g => g.Id == gid) : null;
            return new DansgardeMember(m.Id, m.FullName, m.MemberNumber, m.BirthDate, age, group, [.. relations[m.Id]],
                suggested.Contains(m.Id), withAccount.Contains(m.Id), soon);
        }).OrderBy(r => r.FullName, StringComparer.CurrentCulture).ToList();

        return new DansgardeOverview(
            rows.Count,
            rows.Count(r => r.DanceGroup is null),
            rows.Count(r => !r.OwnAccount && r.Guardians.Count == 0),
            rows.Count(r => r.TurnsFifteenOn is not null),
            rows, groups);
    }

    public async Task<DanceGroups> GroupsAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var groups = await db.Groups.AsNoTracking().Where(g => g.Type == GroupType.DanceGuard)
            .OrderByDescending(g => g.Active).ThenBy(g => g.Name).ToListAsync(cancellationToken);
        var groupIds = groups.Select(g => g.Id).ToList();
        var memberships = await db.GroupMemberships.AsNoTracking()
            .Where(gm => groupIds.Contains(gm.GroupId) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Join(db.Members, gm => gm.MemberId, m => m.Id, (gm, m) => new { gm.GroupId, gm.Function, m.Id, m.FullName, m.BirthDate })
            .ToListAsync(cancellationToken);
        var dansgarde = await Members(db).AsNoTracking().Select(m => new { m.Id, m.FullName, m.BirthDate }).ToListAsync(cancellationToken);
        var assigned = memberships.Where(m => m.Function == GroupFunction.Member && groups.Any(g => g.Id == m.GroupId && g.Active))
            .Select(m => m.Id).ToHashSet();

        return new DanceGroups(
            [.. groups.Select(g => new DanceGroup(
                g.Id, g.Name, g.Description, g.Active,
                [.. memberships.Where(m => m.GroupId == g.Id && m.Function == GroupFunction.Lead).Select(m => m.FullName).Order(StringComparer.CurrentCulture)],
                [.. memberships.Where(m => m.GroupId == g.Id && m.Function == GroupFunction.Member).OrderBy(m => m.FullName, StringComparer.CurrentCulture)
                    .Select(m => new DanceGroupMember(m.Id, m.FullName, Guardians.AgeOn(m.BirthDate, today)))]))],
            [.. dansgarde.Where(m => !assigned.Contains(m.Id)).OrderBy(m => m.FullName, StringComparer.CurrentCulture)
                .Select(m => new DanceGroupMember(m.Id, m.FullName, Guardians.AgeOn(m.BirthDate, today)))]);
    }

    /// <summary>Deelt een dansgarde-lid in bij één dansgroep (of haalt het eruit met <c>null</c>).</summary>
    public async Task AssignAsync(Guid memberId, Guid? groupId, CancellationToken cancellationToken)
    {
        if (!await Members(db).AnyAsync(m => m.Id == memberId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.NotDansgarde,
                $"Alleen actieve leden met groep \"{GroupValue}\" in e-Boekhouden kunnen in een dansgroep.", DomainErrorKind.Validation);
        }

        if (groupId is { } target && !await db.Groups.AnyAsync(g => g.Id == target && g.Type == GroupType.DanceGuard && g.Active, cancellationToken))
        {
            throw new DomainException(ErrorCodes.GroupNotFound, "Dansgroep niet gevonden.", DomainErrorKind.NotFound);
        }

        var danceGroupIds = await db.Groups.Where(g => g.Type == GroupType.DanceGuard).Select(g => g.Id).ToListAsync(cancellationToken);
        var current = await db.GroupMemberships
            .Where(gm => gm.MemberId == memberId && gm.Function == GroupFunction.Member && danceGroupIds.Contains(gm.GroupId))
            .ToListAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        db.GroupMemberships.RemoveRange(current.Where(gm => gm.GroupId != groupId));
        if (groupId is { } id && current.All(gm => gm.GroupId != id)
            && !await db.GroupMemberships.AnyAsync(gm => gm.GroupId == id && gm.MemberId == memberId, cancellationToken))
        {
            db.GroupMemberships.Add(new GroupMembership { GroupId = id, MemberId = memberId, Function = GroupFunction.Member });
        }

        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry("dansgarde.assigned", "Member", memberId.ToString(), null, JsonSerializer.Serialize(new { groupId }, Json)),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
