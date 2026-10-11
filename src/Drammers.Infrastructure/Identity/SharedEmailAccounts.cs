using Drammers.Infrastructure.Members;
using Drammers.Infrastructure.Persistence;
using Drammers.Infrastructure.Persistence.Configurations;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Guardians;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Identity;

/// <summary>Meerdere accounts met hetzelfde e-mailadres (portal): wie het account houdt en wie eronder komt.</summary>
public sealed record SharedEmailConflict(
    string Email, bool Resolvable, string? KeepName, IReadOnlyList<string> Children, IReadOnlyList<string> Others, string? Reason);

/// <summary>
/// Eén e-mailadres = één account (2026-10-11). Inloggen gaat per e-mailadres, dus twee accounts met hetzelfde adres
/// kunnen niet allebei inloggen; vaak een ouder en een kind (dansgarde) met het adres van de ouder. Het account van de
/// volwassene blijft; kinderen komen eronder als "Mijn kinderen" en hun eigen account vervalt. Een ouderaccount zonder
/// lid (uit een aanmelding van een kind) gaat op in het account van de volwassene. Zijn er twee volwassenen, dan kiest het
/// systeem niet: dat staat in het portal, en een van beiden krijgt een eigen adres.
/// </summary>
public sealed class SharedEmailAccounts(DrammersDbContext db, AccountAdministration administration, IAuditLogger audit, IClock clock)
{
    private sealed record Candidate(User User, Member? Member, string Name);

    private sealed record Plan(Candidate? Keep, IReadOnlyList<Candidate> Children, IReadOnlyList<Candidate> Merge, string? Reason);

    private DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public async Task<IReadOnlyList<SharedEmailConflict>> ListAsync(CancellationToken cancellationToken)
    {
        var emails = await db.Users.AsNoTracking().Where(u => u.AccountStatus != AccountStatus.Deleted)
            .GroupBy(u => u.Email).Where(g => g.Count() > 1).Select(g => g.Key).ToListAsync(cancellationToken);
        var result = new List<SharedEmailConflict>();
        foreach (var email in emails.Order(StringComparer.Ordinal))
        {
            var plan = Classify(await CandidatesAsync(email, cancellationToken));
            result.Add(new SharedEmailConflict(email, plan.Reason is null, plan.Keep?.Name,
                [.. plan.Children.Select(c => c.Name)], [.. plan.Merge.Select(c => c.Name)], plan.Reason));
        }

        return result;
    }

    /// <summary>Voert de regel uit; geeft het account dat blijft, of gooit een fout als het systeem niet kan kiezen.</summary>
    public async Task<User> ResolveAsync(string email, CancellationToken cancellationToken)
    {
        var plan = Classify(await CandidatesAsync(email.Trim().ToLowerInvariant(), cancellationToken));
        if (plan.Reason is not null || plan.Keep is null)
        {
            throw new DomainException(ErrorCodes.Validation, plan.Reason ?? "Niets om samen te voegen.", DomainErrorKind.Conflict);
        }

        var keep = plan.Keep.User;
        var now = clock.UtcNow.UtcDateTime;
        foreach (var child in plan.Children.Where(c => c.Member is not null))
        {
            var memberId = child.Member!.Id;
            var guardians = await db.GuardianRelations.Where(g => g.MemberId == memberId).Select(g => g.GuardianUserId).ToListAsync(cancellationToken);
            if (!guardians.Contains(keep.Id) && guardians.Count < GuardianRelation.MaxPerChild)
            {
                db.GuardianRelations.Add(new GuardianRelation
                {
                    Id = IdGenerator.NewId(),
                    MemberId = memberId,
                    GuardianUserId = keep.Id,
                    GuardianName = keep.DisplayName.Length > 100 ? keep.DisplayName[..100] : keep.DisplayName,
                    Relationship = GuardianRelationship.Parent,
                    VerifiedAt = now,
                    CreatedAt = now,
                });
            }
        }

        // Een los ouderaccount (zonder lid) gaat op in dit account: zijn kinderen en rollen komen mee.
        foreach (var other in plan.Merge)
        {
            await db.GuardianRelations.Where(g => g.GuardianUserId == other.User.Id
                    && !db.GuardianRelations.Any(k => k.MemberId == g.MemberId && k.GuardianUserId == keep.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.GuardianUserId, keep.Id), cancellationToken);
            foreach (var role in other.User.Roles.Where(r => keep.Roles.All(k => k.RoleId != r.RoleId)))
            {
                keep.Roles.Add(new UserRole { UserId = keep.Id, RoleId = role.RoleId, ValidFrom = role.ValidFrom, ValidTo = role.ValidTo, AssignedAt = now });
            }
        }

        var ouder = await db.Roles.Where(r => r.Code == DefaultRoles.Ouder).Select(r => r.Id).SingleAsync(cancellationToken);
        if (plan.Children.Count > 0 && keep.Roles.All(r => r.RoleId != ouder))
        {
            keep.Roles.Add(new UserRole { UserId = keep.Id, RoleId = ouder, AssignedAt = now });
        }

        // Is er al met een van de andere accounts ingelogd en met dit account nog niet, dan neemt dit account die inlog over.
        var dropped = plan.Children.Concat(plan.Merge).Select(c => c.User).ToList();
        if (PendingObjectId.IsPending(keep.ExternalObjectId) && dropped.FirstOrDefault(u => !PendingObjectId.IsPending(u.ExternalObjectId)) is { } loggedIn)
        {
            var objectId = loggedIn.ExternalObjectId;
            loggedIn.ExternalObjectId = $"samengevoegd:{loggedIn.Id:N}";
            await db.SaveChangesAsync(cancellationToken);
            keep.ExternalObjectId = objectId;
        }

        keep.PermissionsVersion++;
        await db.SaveChangesAsync(cancellationToken);
        foreach (var user in dropped)
        {
            await administration.DeleteUserAsync(user.Id, cancellationToken);
        }

        await audit.WriteAsync(new AuditEntry("user.shared-email-merged", "User", keep.Id.ToString(), null,
            System.Text.Json.JsonSerializer.Serialize(new { children = plan.Children.Select(c => c.User.Id), merged = plan.Merge.Select(c => c.User.Id) })), cancellationToken);
        return keep;
    }

    private async Task<List<Candidate>> CandidatesAsync(string email, CancellationToken cancellationToken)
    {
        var users = await db.Users.Include(u => u.Roles)
            .Where(u => u.Email == email && u.AccountStatus != AccountStatus.Deleted).ToListAsync(cancellationToken);
        var memberIds = users.Where(u => u.MemberId != null).Select(u => u.MemberId!.Value).ToList();
        var members = await db.Members.AsNoTracking().Where(m => memberIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id, cancellationToken);
        return [.. users.Select(u =>
        {
            var member = u.MemberId is { } id ? members.GetValueOrDefault(id) : null;
            return new Candidate(u, member, member?.FullName ?? u.DisplayName);
        })];
    }

    private Plan Classify(List<Candidate> candidates)
    {
        if (candidates.Count < 2)
        {
            return new Plan(candidates.SingleOrDefault(), [], [], candidates.Count == 0 ? "Geen accounts met dit adres." : null);
        }

        var children = candidates.Where(c => c.Member is { } m && ChildAccounts.IsChild(m, Today)).ToList();
        var adults = candidates.Where(c => c.Member is not null && !children.Contains(c)).ToList();
        var withoutMember = candidates.Where(c => c.Member is null).ToList();
        if (adults.Count > 1)
        {
            return new Plan(null, children, [], $"Twee volwassenen met hetzelfde e-mailadres ({string.Join(" en ", adults.Select(a => a.Name))}): geef een van beiden een eigen e-mailadres.");
        }

        var keep = adults.SingleOrDefault() ?? (withoutMember.Count == 1 ? withoutMember[0] : null);
        if (keep is null)
        {
            return new Plan(null, children, [], "Geen volwassene bij dit e-mailadres: vul de geboortedatum in of geef een eigen e-mailadres.");
        }

        return new Plan(keep, children, [.. withoutMember.Where(c => c != keep)], null);
    }
}
