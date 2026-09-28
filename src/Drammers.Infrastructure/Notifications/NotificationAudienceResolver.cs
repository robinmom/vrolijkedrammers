using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Notifications;

/// <summary>Een ontvanger: een account (eventueel namens een kind) of een gast-apparaat.</summary>
public sealed record AudienceRecipient(Guid? UserId, Guid? PushDeviceId, Guid? OnBehalfOfMemberId);

/// <summary>Aantallen vóór verzending (docs/02 §5.3 stap 3); gelijk aan wat de worker daadwerkelijk aanmaakt.</summary>
public sealed record AudiencePreview(int Accounts, int Guests, int PushDevices, int OptedOut);

/// <summary>
/// Rolt een doelgroep uit naar ontvangers (fase 10). Alleen actieve accounts; bij groepen en leden alleen actieve leden
/// in geldige groepslidmaatschappen (zelfde regels als de contentdoelgroepen). Voor een gericht lid ontvangen ook de
/// ouders/verzorgers, "namens" dat lid. Iemand die zelf al ontvangt, krijgt geen tweede melding namens een kind.
/// </summary>
public sealed class NotificationAudienceResolver(DrammersDbContext db, IClock clock)
{
    public async Task<IReadOnlyList<AudienceRecipient>> ResolveAsync(NotificationAudience audience, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var users = new Dictionary<Guid, Guid?>();
        void Add(Guid userId, Guid? onBehalfOf)
        {
            if (!users.TryGetValue(userId, out var existing) || (existing is not null && onBehalfOf is null))
            {
                users[userId] = onBehalfOf;
            }
        }

        var active = db.Users.AsNoTracking().Where(u => u.AccountStatus == AccountStatus.Active);
        if (audience.Everyone)
        {
            foreach (var id in await active.Select(u => u.Id).ToListAsync(cancellationToken))
            {
                Add(id, null);
            }
        }

        var roleCodes = (audience.Roles ?? []).ToHashSet(StringComparer.Ordinal);
        if (audience.Members)
        {
            roleCodes.Add(ContentViewer.MemberRole);
        }

        if (roleCodes.Count > 0 && !audience.Everyone)
        {
            var roleIds = await db.Roles.AsNoTracking().Where(r => roleCodes.Contains(r.Code)).Select(r => r.Id).ToListAsync(cancellationToken);
            var withRole = await active
                .Where(u => u.Roles.Any(r => roleIds.Contains(r.RoleId) && (r.ValidFrom == null || r.ValidFrom <= today) && (r.ValidTo == null || r.ValidTo >= today)))
                .Select(u => u.Id).ToListAsync(cancellationToken);
            foreach (var id in withRole)
            {
                Add(id, null);
            }
        }

        var memberIds = (audience.MemberIds ?? []).ToHashSet();
        var groupIds = (audience.Groups ?? []).ToList();
        if (groupIds.Count > 0)
        {
            var inGroups = await db.GroupMemberships.AsNoTracking()
                .Where(gm => groupIds.Contains(gm.GroupId) && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
                .Join(db.Groups.Where(g => g.Active), gm => gm.GroupId, g => g.Id, (gm, _) => gm.MemberId)
                .ToListAsync(cancellationToken);
            memberIds.UnionWith(inGroups);
        }

        if (memberIds.Count > 0)
        {
            var activeMembers = (await db.Members.AsNoTracking()
                .Where(m => (m.LocalStatusOverride ?? m.MembershipStatus) == MembershipStatus.Active)
                .Select(m => m.Id).ToListAsync(cancellationToken))
                .Where(memberIds.Contains).ToHashSet();

            foreach (var row in (await active.Where(u => u.MemberId != null).Select(u => new { u.Id, u.MemberId }).ToListAsync(cancellationToken))
                .Where(u => activeMembers.Contains(u.MemberId!.Value)))
            {
                Add(row.Id, null);
            }

            var guardians = await db.GuardianRelations.AsNoTracking()
                .Join(active, g => g.GuardianUserId, u => u.Id, (g, _) => new { g.GuardianUserId, g.MemberId })
                .ToListAsync(cancellationToken);
            foreach (var guardian in guardians.Where(g => activeMembers.Contains(g.MemberId)).OrderBy(g => g.MemberId))
            {
                Add(guardian.GuardianUserId, guardian.MemberId);
            }
        }

        var result = users.Select(u => new AudienceRecipient(u.Key, null, u.Value)).ToList();
        if (audience.Everyone)
        {
            var guests = await db.PushDevices.AsNoTracking()
                .Where(p => p.UserId == null && p.AnonymousInstallId != null && p.Enabled)
                .Select(p => p.Id).ToListAsync(cancellationToken);
            result.AddRange(guests.Select(id => new AudienceRecipient(null, id, null)));
        }

        return result;
    }

    public async Task<AudiencePreview> PreviewAsync(NotificationAudience audience, NotificationCategory category, CancellationToken cancellationToken)
    {
        var recipients = await ResolveAsync(audience, cancellationToken);
        var userIds = recipients.Where(r => r.UserId is not null).Select(r => r.UserId!.Value).ToHashSet();
        var optedOut = NotificationPreference.CanDisable(category)
            ? (await db.NotificationPreferences.AsNoTracking().Where(p => p.Category == category && !p.Enabled).Select(p => p.UserId).ToListAsync(cancellationToken))
                .Where(userIds.Contains).ToHashSet()
            : [];
        var devices = (await db.PushDevices.AsNoTracking().Where(p => p.Enabled && p.UserId != null).Select(p => p.UserId!.Value).ToListAsync(cancellationToken))
            .Count(u => userIds.Contains(u) && !optedOut.Contains(u));
        var guests = recipients.Count(r => r.PushDeviceId is not null);
        return new AudiencePreview(userIds.Count, guests, devices + guests, optedOut.Count);
    }
}
