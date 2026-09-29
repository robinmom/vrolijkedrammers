using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Identity.Devices;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Notifications;

public sealed record InboxItem(Guid Id, string Title, string Body, NotificationCategory Category, string? DeepLink, DateTime SentAt, DateTime? ReadAt);

public sealed record InboxPage(IReadOnlyList<InboxItem> Items, int UnreadCount, bool HasMore);

public sealed record CategoryPreference(NotificationCategory Category, bool Enabled, bool CanDisable);

/// <summary>
/// Push-tokens registreren (ingelogd apparaat of gast), de inbox en voorkeuren per categorie (fase 10). Eén token hoort
/// bij precies één installatie: registreert een gast-installatie na uitloggen hetzelfde token, dan neemt die de rij over
/// en omgekeerd, zodat een toestel nooit dubbel of voor de verkeerde persoon ontvangt.
/// </summary>
public sealed class MyNotifications(DrammersDbContext db, PushTokenProtector tokens, IClock clock)
{
    public const int PageSize = 30;

    public async Task RegisterDeviceTokenAsync(Guid userId, Guid deviceId, string token, CancellationToken cancellationToken)
    {
        var device = await db.Devices.AsNoTracking().SingleOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, cancellationToken)
            ?? throw new DomainException(ErrorCodes.DeviceNotFound, "Apparaat niet gevonden.", DomainErrorKind.NotFound);
        if (device.Status != DeviceStatus.Active)
        {
            throw new DomainException(ErrorCodes.DeviceRevoked, "Dit apparaat is afgemeld.", DomainErrorKind.Conflict);
        }

        var row = await UpsertAsync(token, db.PushDevices.Where(p => p.DeviceId == deviceId), device.Platform.ToString(), cancellationToken);
        row.DeviceId = deviceId;
        row.UserId = userId;
        row.AnonymousInstallId = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RegisterAnonymousTokenAsync(string installId, string platform, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(installId) || installId.Length > 64 || platform is not ("Ios" or "Android"))
        {
            throw new DomainException(ErrorCodes.Validation, "Ongeldige installatie.");
        }

        var row = await UpsertAsync(token, db.PushDevices.Where(p => p.AnonymousInstallId == installId), platform, cancellationToken);
        row.AnonymousInstallId = installId;
        row.DeviceId = null;
        row.UserId = null;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Gast zet push uit (of de app wordt verwijderd): het token verdwijnt.</summary>
    public Task<int> RemoveAnonymousTokenAsync(string installId, CancellationToken cancellationToken) =>
        db.PushDevices.Where(p => p.AnonymousInstallId == installId).ExecuteDeleteAsync(cancellationToken);

    private async Task<PushDevice> UpsertAsync(string token, IQueryable<PushDevice> owner, string platform, CancellationToken cancellationToken)
    {
        if (!PushTokenProtector.IsValid(token))
        {
            throw new DomainException(ErrorCodes.InvalidPushToken, "Ongeldig push-token.");
        }

        var hash = PushTokenProtector.Hash(token);
        var byOwner = await owner.SingleOrDefaultAsync(cancellationToken);
        var byToken = await db.PushDevices.SingleOrDefaultAsync(p => p.TokenHash == hash, cancellationToken);
        if (byOwner is not null && byToken is not null && byOwner.Id != byToken.Id)
        {
            db.PushDevices.Remove(byToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        var row = byOwner ?? byToken;
        if (row is null)
        {
            row = new PushDevice { Id = IdGenerator.NewId(), Platform = platform, ProtectedToken = "", TokenHash = hash };
            db.PushDevices.Add(row);
        }

        row.Platform = platform;
        row.TokenHash = hash;
        row.ProtectedToken = tokens.Protect(token);
        row.Enabled = true;
        row.InvalidatedAt = null;
        row.LastRegisteredAt = clock.UtcNow.UtcDateTime;
        return row;
    }

    // ----- Inbox ----------------------------------------------------------------------------------------------------

    private IQueryable<NotificationRecipient> Inbox(Guid userId) => InboxOf(db, [userId]);

    /// <summary>Wat in de inbox staat (niet ingepland en niet geannuleerd); ook gebruikt voor het telbolletje in een push.</summary>
    public static IQueryable<NotificationRecipient> InboxOf(DrammersDbContext db, IReadOnlyCollection<Guid> userIds) =>
        db.NotificationRecipients.AsNoTracking().Where(r => r.UserId != null && userIds.Contains(r.UserId.Value)
            && db.Notifications.Any(n => n.Id == r.NotificationId && n.Status != NotificationStatus.Scheduled && n.Status != NotificationStatus.Canceled));

    public async Task<InboxPage> GetInboxAsync(Guid userId, int page, CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        var rows = await Inbox(userId)
            .Join(db.Notifications, r => r.NotificationId, n => n.Id, (r, n) => new { r, n })
            .GroupJoin(db.Members, x => x.r.OnBehalfOfMemberId, m => m.Id, (x, ms) => new { x.r, x.n, ms })
            .SelectMany(x => x.ms.DefaultIfEmpty(), (x, m) => new
            {
                x.n.Id,
                x.n.Title,
                x.n.Body,
                x.n.Category,
                x.n.DeepLink,
                SentAt = x.n.SentAt ?? x.n.ScheduledAt ?? x.n.CreatedAt,
                x.r.ReadAt,
                OnBehalfOf = m == null ? null : m.FirstName ?? m.FullName,
            })
            .OrderByDescending(x => x.SentAt).ThenByDescending(x => x.Id)
            .Skip((page - 1) * PageSize).Take(PageSize + 1)
            .ToListAsync(cancellationToken);
        var unread = await Inbox(userId).CountAsync(r => r.ReadAt == null, cancellationToken);
        var items = rows.Take(PageSize)
            .Select(x => new InboxItem(x.Id, x.OnBehalfOf is null ? x.Title : $"Namens {x.OnBehalfOf}: {x.Title}", x.Body, x.Category, x.DeepLink, x.SentAt, x.ReadAt))
            .ToList();
        return new InboxPage(items, unread, rows.Count > PageSize);
    }

    /// <summary>De laatste meldingen die deze ouder namens een kind kreeg (fase 17, kind-detail in de app).</summary>
    public async Task<IReadOnlyList<InboxItem>> ForChildAsync(Guid userId, Guid childMemberId, int take, CancellationToken cancellationToken)
    {
        var rows = await Inbox(userId).Where(r => r.OnBehalfOfMemberId == childMemberId)
            .Join(db.Notifications, r => r.NotificationId, n => n.Id, (r, n) => new
            {
                n.Id,
                n.Title,
                n.Body,
                n.Category,
                n.DeepLink,
                SentAt = n.SentAt ?? n.ScheduledAt ?? n.CreatedAt,
                r.ReadAt,
            })
            .OrderByDescending(x => x.SentAt).ThenByDescending(x => x.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => new InboxItem(x.Id, x.Title, x.Body, x.Category, x.DeepLink, x.SentAt, x.ReadAt))];
    }

    public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken)
    {
        var updated = await db.NotificationRecipients
            .Where(r => r.UserId == userId && r.NotificationId == notificationId && r.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.ReadAt, clock.UtcNow.UtcDateTime), cancellationToken);
        if (updated > 0)
        {
            await db.Notifications.Where(n => n.Id == notificationId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadCount, n => n.ReadCount + 1), cancellationToken);
        }
        else if (!await db.NotificationRecipients.AnyAsync(r => r.UserId == userId && r.NotificationId == notificationId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.NotificationNotFound, "Melding niet gevonden.", DomainErrorKind.NotFound);
        }
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ids = await db.NotificationRecipients.Where(r => r.UserId == userId && r.ReadAt == null).Select(r => r.NotificationId).ToListAsync(cancellationToken);
        foreach (var id in ids)
        {
            await MarkReadAsync(userId, id, cancellationToken);
        }
    }

    // ----- Voorkeuren -----------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<CategoryPreference>> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var stored = await db.NotificationPreferences.AsNoTracking().Where(p => p.UserId == userId)
            .ToDictionaryAsync(p => p.Category, p => p.Enabled, cancellationToken);
        return [.. Enum.GetValues<NotificationCategory>().Select(c => new CategoryPreference(
            c, !NotificationPreference.CanDisable(c) || stored.GetValueOrDefault(c, true), NotificationPreference.CanDisable(c)))];
    }

    public async Task<IReadOnlyList<CategoryPreference>> SetPreferencesAsync(
        Guid userId, IReadOnlyCollection<(NotificationCategory Category, bool Enabled)> changes, CancellationToken cancellationToken)
    {
        if (changes.Any(c => !c.Enabled && !NotificationPreference.CanDisable(c.Category)))
        {
            throw new DomainException(ErrorCodes.Validation, "Dringende meldingen en systeemmeldingen kun je niet uitzetten.");
        }

        var stored = await db.NotificationPreferences.Where(p => p.UserId == userId).ToListAsync(cancellationToken);
        foreach (var (category, enabled) in changes.Where(c => NotificationPreference.CanDisable(c.Category)))
        {
            var row = stored.SingleOrDefault(p => p.Category == category);
            if (row is null)
            {
                db.NotificationPreferences.Add(new NotificationPreference { UserId = userId, Category = category, Enabled = enabled });
            }
            else
            {
                row.Enabled = enabled;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return await GetPreferencesAsync(userId, cancellationToken);
    }
}
