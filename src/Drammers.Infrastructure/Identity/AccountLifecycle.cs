using System.Text.Json;
using Drammers.Infrastructure.Identity.Entra;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Users;
using Drammers.Modules.Membership.Members;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Messaging;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Identity;

/// <summary>
/// Levenscyclus van ledenaccounts (ADR-014, fase 9b): is het lid niet (meer) actief (inactief, geschorst, overleden),
/// dan wordt het account uitgeschakeld en de inlog in Entra uitgezet; wordt het lid weer actief, dan gaat het account
/// weer aan. Accounts met een beheerrol (bestuur, redactie, IT, …) blijven aan: die rollen hangen niet van het
/// lidmaatschap af. Geblokkeerde accounts blijven geblokkeerd.
/// </summary>
public sealed class AccountLifecycle(DrammersDbContext db, IAuditLogger audit, IOutbox outbox, IUserAccessService userAccess)
{
    public const string EntraStateMessageType = "account.entra-state";

    public sealed record EntraStateMessage(string ObjectId, bool Enabled);

    /// <summary>Brengt de accounts in lijn met de lidstatus; zonder <paramref name="memberIds"/> alle gekoppelde leden.</summary>
    /// <returns>Aantal gewijzigde accounts.</returns>
    public async Task<int> ReconcileAsync(IReadOnlyCollection<Guid>? memberIds, CancellationToken cancellationToken)
    {
        var rows = await db.Users
            .Where(u => u.MemberId != null && (u.AccountStatus == AccountStatus.Active || u.AccountStatus == AccountStatus.Disabled))
            .Where(u => memberIds == null || memberIds.Contains(u.MemberId!.Value))
            .Join(db.Members, u => u.MemberId, m => m.Id, (u, m) => new { User = u, Status = m.LocalStatusOverride ?? m.MembershipStatus })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return 0;
        }

        // Rollen die niet uit het lidmaatschap volgen (niet door de sync toe te kennen): zo'n account blijft aan.
        var userIds = rows.Select(r => r.User.Id).ToList();
        var managers = await db.UserRoles.Where(ur => userIds.Contains(ur.UserId))
            .Join(db.Roles.Where(r => !r.IsAssignableBySync), ur => ur.RoleId, r => r.Id, (ur, _) => ur.UserId)
            .Distinct().ToListAsync(cancellationToken);

        var changed = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var row in rows)
        {
            var shouldBeActive = row.Status == MembershipStatus.Active || managers.Contains(row.User.Id);
            var target = shouldBeActive ? AccountStatus.Active : AccountStatus.Disabled;
            if (row.User.AccountStatus == target)
            {
                continue;
            }

            row.User.AccountStatus = target;
            row.User.PermissionsVersion++;
            if (!PendingObjectId.IsPending(row.User.ExternalObjectId))
            {
                outbox.Enqueue(EntraStateMessageType, new EntraStateMessage(row.User.ExternalObjectId, shouldBeActive));
            }

            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(
                new AuditEntry(shouldBeActive ? "user.enabled-member-active" : "user.disabled-member-inactive", "User", row.User.Id.ToString(), null,
                    JsonSerializer.Serialize(new { memberStatus = row.Status.ToString() })),
                cancellationToken);
            changed++;
        }

        await transaction.CommitAsync(cancellationToken);
        foreach (var row in rows)
        {
            userAccess.Invalidate(row.User.ExternalObjectId);
        }

        return changed;
    }
}

/// <summary>Zet de inlog in Entra aan of uit (worker; bij een fout opnieuw via de outbox). Uitzetten trekt ook sessies in.</summary>
public sealed class EntraAccountStateHandler(IEntraUserDirectory entra) : IOutboxMessageHandler
{
    public string Type => AccountLifecycle.EntraStateMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var state = JsonSerializer.Deserialize<AccountLifecycle.EntraStateMessage>(message.Payload, JsonSerializerOptions.Web)!;
        await entra.SetAccountEnabledAsync(state.ObjectId, state.Enabled, cancellationToken);
        if (!state.Enabled)
        {
            await entra.RevokeSessionsAsync(state.ObjectId, cancellationToken);
        }
    }
}
