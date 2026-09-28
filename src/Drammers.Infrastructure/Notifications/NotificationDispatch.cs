using System.Text.Json;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Drammers.Infrastructure.Notifications;

/// <summary>
/// Verstuurt een melding (outbox <c>notification.dispatch</c>, ADR-009): doelgroep uitrollen naar ontvangers en
/// apparaten (één keer, in één transactie), daarna per batch van 100 naar Expo. Na een herstart gaat het verder met de
/// apparaten die nog <c>Pending</c> zijn; een batch die al verstuurd maar nog niet opgeslagen was, kan in dat zeldzame
/// geval dubbel aankomen (at-least-once).
/// </summary>
public sealed class NotificationDispatchHandler(
    DrammersDbContext db,
    NotificationAudienceResolver resolver,
    IPushSender sender,
    PushTokenProtector tokens,
    IOutbox outbox,
    IClock clock,
    ILogger<NotificationDispatchHandler> logger) : IOutboxMessageHandler
{
    public string Type => NotificationAdministration.DispatchMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var id = JsonSerializer.Deserialize<NotificationAdministration.DispatchMessage>(message.Payload, NotificationAdministration.Json)!.NotificationId;
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (notification is null || notification.Status is not (NotificationStatus.Scheduled or NotificationStatus.Sending))
        {
            return; // geannuleerd of al klaar
        }

        if (!notification.Expanded)
        {
            await ExpandAsync(notification, cancellationToken);
        }

        await SendPendingAsync(notification, cancellationToken);
        await CompleteAsync(notification, cancellationToken);
    }

    private async Task ExpandAsync(Notification notification, CancellationToken cancellationToken)
    {
        var audience = JsonSerializer.Deserialize<NotificationAudience>(notification.AudienceJson, NotificationAdministration.Json)!;
        var recipients = await resolver.ResolveAsync(audience, cancellationToken);
        var optedOut = NotificationPreference.CanDisable(notification.Category)
            ? (await db.NotificationPreferences.AsNoTracking().Where(p => p.Category == notification.Category && !p.Enabled)
                .Select(p => p.UserId).ToListAsync(cancellationToken)).ToHashSet()
            : [];
        var devicesByUser = (await db.PushDevices.AsNoTracking().Where(p => p.Enabled && p.UserId != null)
                .Select(p => new { p.Id, UserId = p.UserId!.Value }).ToListAsync(cancellationToken))
            .ToLookup(p => p.UserId, p => p.Id);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = new List<(NotificationRecipient Recipient, List<Guid> Devices)>();
        foreach (var r in recipients)
        {
            var devices = r.PushDeviceId is { } guestDevice ? [guestDevice] : devicesByUser[r.UserId!.Value].ToList();
            var status = r.UserId is { } userId && optedOut.Contains(userId) ? DeliveryStatus.OptedOut
                : devices.Count == 0 ? DeliveryStatus.NoDevice
                : DeliveryStatus.Pending;
            rows.Add((new NotificationRecipient
            {
                NotificationId = notification.Id,
                UserId = r.UserId,
                PushDeviceId = r.PushDeviceId,
                OnBehalfOfMemberId = r.OnBehalfOfMemberId,
                DeliveryStatus = status,
            }, status == DeliveryStatus.Pending ? devices : []));
        }

        db.NotificationRecipients.AddRange(rows.Select(r => r.Recipient));
        await db.SaveChangesAsync(cancellationToken);
        db.NotificationDeliveries.AddRange(rows.SelectMany(r => r.Devices.Select(device => new NotificationDelivery
        {
            RecipientId = r.Recipient.Id,
            NotificationId = notification.Id,
            PushDeviceId = device,
            Status = DeliveryStatus.Pending,
        })));
        notification.Expanded = true;
        notification.Status = NotificationStatus.Sending;
        notification.RecipientCount = rows.Count;
        notification.PushCount = rows.Sum(r => r.Devices.Count);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task SendPendingAsync(Notification notification, CancellationToken cancellationToken)
    {
        var onBehalfNames = new Dictionary<Guid, string>();
        while (true)
        {
            var batch = await db.NotificationDeliveries
                .Where(d => d.NotificationId == notification.Id && d.Status == DeliveryStatus.Pending)
                .OrderBy(d => d.Id).Take(sender.BatchSize).ToListAsync(cancellationToken);
            if (batch.Count == 0)
            {
                return;
            }

            var deviceIds = batch.Select(d => d.PushDeviceId).Distinct().ToList();
            var devices = await db.PushDevices.Where(p => deviceIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
            var recipientIds = batch.Select(d => d.RecipientId).Distinct().ToList();
            var onBehalf = await db.NotificationRecipients.AsNoTracking()
                .Where(r => recipientIds.Contains(r.Id) && r.OnBehalfOfMemberId != null)
                .ToDictionaryAsync(r => r.Id, r => r.OnBehalfOfMemberId!.Value, cancellationToken);
            foreach (var memberId in onBehalf.Values.Distinct().Where(m => !onBehalfNames.ContainsKey(m)).ToList())
            {
                onBehalfNames[memberId] = await db.Members.AsNoTracking().Where(m => m.Id == memberId)
                    .Select(m => m.FirstName ?? m.FullName).SingleOrDefaultAsync(cancellationToken) ?? "je kind";
            }

            var sendable = new List<(NotificationDelivery Delivery, PushMessage Message)>();
            foreach (var delivery in batch)
            {
                if (!devices.TryGetValue(delivery.PushDeviceId, out var device) || !device.Enabled)
                {
                    delivery.Status = DeliveryStatus.Failed;
                    delivery.ErrorCode = "DeviceDisabled";
                    delivery.CompletedAt = clock.UtcNow.UtcDateTime;
                    continue;
                }

                var title = onBehalf.TryGetValue(delivery.RecipientId, out var member)
                    ? $"Namens {onBehalfNames[member]}: {notification.Title}"
                    : notification.Title;
                sendable.Add((delivery, new PushMessage(
                    tokens.Unprotect(device.ProtectedToken), title, notification.Body, ChannelId(notification.Category),
                    notification.Category == NotificationCategory.Urgent, Data(notification))));
            }

            if (sendable.Count > 0)
            {
                var tickets = await sender.SendAsync([.. sendable.Select(s => s.Message)], cancellationToken);
                var now = clock.UtcNow.UtcDateTime;
                for (var i = 0; i < sendable.Count; i++)
                {
                    var delivery = sendable[i].Delivery;
                    delivery.SentAt = now;
                    if (tickets[i].TicketId is { } ticket)
                    {
                        delivery.Status = DeliveryStatus.Sent;
                        delivery.TicketId = ticket;
                    }
                    else
                    {
                        delivery.Status = DeliveryStatus.Failed;
                        delivery.ErrorCode = tickets[i].ErrorCode;
                        delivery.CompletedAt = now;
                        if (tickets[i].ErrorCode == "DeviceNotRegistered")
                        {
                            Invalidate(devices[delivery.PushDeviceId], now);
                        }
                    }
                }
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task CompleteAsync(Notification notification, CancellationToken cancellationToken)
    {
        await NotificationCounters.RefreshAsync(db, notification, cancellationToken);
        notification.Status = notification.PushCount > 0 && notification.FailedCount == notification.PushCount ? NotificationStatus.Failed
            : notification.FailedCount > 0 ? NotificationStatus.PartiallyFailed
            : NotificationStatus.Sent;
        notification.SentAt = clock.UtcNow.UtcDateTime;
        if (await db.NotificationDeliveries.AnyAsync(d => d.NotificationId == notification.Id && d.Status == DeliveryStatus.Sent, cancellationToken))
        {
            outbox.Enqueue(NotificationAdministration.ReceiptsMessageType, new NotificationAdministration.ReceiptsMessage(notification.Id, 1), notification.SentAt.Value.AddMinutes(15));
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Melding {NotificationId} verstuurd: {Recipients} ontvangers, {Push} pushberichten, {Failed} mislukt",
            notification.Id, notification.RecipientCount, notification.PushCount, notification.FailedCount);
    }

    internal static void Invalidate(PushDevice device, DateTime now)
    {
        device.Enabled = false;
        device.InvalidatedAt = now;
    }

    /// <summary>Android-kanaal per categorie; de app maakt dezelfde kanalen aan.</summary>
    public static string ChannelId(NotificationCategory category) => category.ToString().ToLowerInvariant();

    private static Dictionary<string, string> Data(Notification notification)
    {
        var data = new Dictionary<string, string> { ["notificationId"] = notification.Id.ToString() };
        if (notification.DeepLink is { } link)
        {
            data["url"] = link;
        }

        return data;
    }
}

/// <summary>
/// Haalt ≥ 15 minuten na verzending de receipts op (outbox <c>notification.receipts</c>): afgeleverd of mislukt;
/// <c>DeviceNotRegistered</c> zet het apparaat uit. Ontbreken er nog receipts, dan nog maximaal twee keer later.
/// </summary>
public sealed class NotificationReceiptsHandler(DrammersDbContext db, IPushSender sender, IOutbox outbox, IClock clock) : IOutboxMessageHandler
{
    public const int MaxAttempts = 3;

    public string Type => NotificationAdministration.ReceiptsMessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<NotificationAdministration.ReceiptsMessage>(message.Payload, NotificationAdministration.Json)!;
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == request.NotificationId, cancellationToken);
        if (notification is null)
        {
            return;
        }

        var open = await db.NotificationDeliveries
            .Where(d => d.NotificationId == notification.Id && d.Status == DeliveryStatus.Sent && d.TicketId != null)
            .ToListAsync(cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        foreach (var chunk in open.Chunk(1000))
        {
            var receipts = await sender.GetReceiptsAsync([.. chunk.Select(d => d.TicketId!)], cancellationToken);
            var invalid = new List<Guid>();
            foreach (var delivery in chunk)
            {
                if (!receipts.TryGetValue(delivery.TicketId!, out var receipt))
                {
                    continue;
                }

                delivery.Status = receipt.Delivered ? DeliveryStatus.Delivered : DeliveryStatus.Failed;
                delivery.ErrorCode = receipt.ErrorCode;
                delivery.CompletedAt = now;
                if (receipt.ErrorCode == "DeviceNotRegistered")
                {
                    invalid.Add(delivery.PushDeviceId);
                }
            }

            foreach (var device in await db.PushDevices.Where(p => invalid.Contains(p.Id)).ToListAsync(cancellationToken))
            {
                NotificationDispatchHandler.Invalidate(device, now);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await NotificationCounters.RefreshAsync(db, notification, cancellationToken);
        if (open.Any(d => d.Status == DeliveryStatus.Sent) && request.Attempt < MaxAttempts)
        {
            outbox.Enqueue(Type, request with { Attempt = request.Attempt + 1 }, now.AddMinutes(30));
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Gedenormaliseerde tellers (docs/04 §6) en de afleverstatus per ontvanger bijwerken.</summary>
internal static class NotificationCounters
{
    public static async Task RefreshAsync(DrammersDbContext db, Notification notification, CancellationToken cancellationToken)
    {
        var id = notification.Id;
        var deliveries = await db.NotificationDeliveries.AsNoTracking().Where(d => d.NotificationId == id)
            .GroupBy(d => d.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(cancellationToken);
        notification.DeliveredCount = deliveries.Where(d => d.Key == DeliveryStatus.Delivered).Sum(d => d.Count);
        notification.FailedCount = deliveries.Where(d => d.Key == DeliveryStatus.Failed).Sum(d => d.Count);
        notification.ReadCount = await db.NotificationRecipients.CountAsync(r => r.NotificationId == id && r.ReadAt != null, cancellationToken);

        // Ontvanger: afgeleverd zodra één apparaat het heeft, verstuurd als er iets onderweg is, anders mislukt.
        await db.NotificationRecipients
            .Where(r => r.NotificationId == id && db.NotificationDeliveries.Any(d => d.RecipientId == r.Id && d.Status == DeliveryStatus.Delivered))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeliveryStatus, DeliveryStatus.Delivered), cancellationToken);
        await db.NotificationRecipients
            .Where(r => r.NotificationId == id && r.DeliveryStatus == DeliveryStatus.Pending
                && db.NotificationDeliveries.Any(d => d.RecipientId == r.Id && d.Status == DeliveryStatus.Sent))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeliveryStatus, DeliveryStatus.Sent), cancellationToken);
        await db.NotificationRecipients
            .Where(r => r.NotificationId == id && (r.DeliveryStatus == DeliveryStatus.Pending || r.DeliveryStatus == DeliveryStatus.Sent)
                && !db.NotificationDeliveries.Any(d => d.RecipientId == r.Id && d.Status != DeliveryStatus.Failed))
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.DeliveryStatus, DeliveryStatus.Failed), cancellationToken);
    }
}
