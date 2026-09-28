using System.Text.Json;
using Drammers.Infrastructure.Identity;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Notification.Notifications;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Notifications;

public sealed record NotificationDraft(
    string Title,
    string Body,
    NotificationCategory Category,
    NotificationAudience Audience,
    string? DeepLink,
    DateTime? ScheduledAt);

/// <summary>
/// Meldingen opstellen, plannen en annuleren (fase 10, docs/02 §5.3). Regels: Dringend en "Iedereen" alleen met
/// <c>notification.send.urgent</c>; met alleen <c>notification.send.group</c> uitsluitend naar de eigen groepen. De
/// bevestiging voor grote doelgroepen zit in het portal (OQ-44: geen vier-ogenprincipe); elke verzending wordt geaudit.
/// </summary>
public sealed class NotificationAdministration(DrammersDbContext db, IOutbox outbox, IAuditLogger audit, IClock clock) : INotificationService
{
    public const string DispatchMessageType = "notification.dispatch";
    public const string ReceiptsMessageType = "notification.receipts";
    public const string DeepLinkScheme = "drammers://";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record DispatchMessage(Guid NotificationId);

    public sealed record ReceiptsMessage(Guid NotificationId, int Attempt);

    public async Task<Notification> CreateAsync(NotificationDraft draft, UserAccess sender, CancellationToken cancellationToken)
    {
        Validate(draft.Title, draft.Body, draft.DeepLink, draft.Audience);
        await AuthorizeAsync(draft, sender, cancellationToken);
        var now = clock.UtcNow.UtcDateTime;
        if (draft.ScheduledAt is { } at && at < now.AddMinutes(1))
        {
            throw new DomainException(ErrorCodes.Validation, "Een geplande melding moet minstens een minuut in de toekomst liggen.");
        }

        var notification = Add(draft.Title, draft.Body, draft.Category, draft.Audience, draft.DeepLink, sender.UserId, null, null, draft.ScheduledAt);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(
            new AuditEntry(draft.ScheduledAt is null ? "notification.sent" : "notification.scheduled", "Notification", notification.Id.ToString(), null,
                JsonSerializer.Serialize(new { draft.Category, draft.ScheduledAt, audience = draft.Audience }, Json)),
            cancellationToken);
        return notification;
    }

    public async Task CancelAsync(Guid id, Guid actorId, CancellationToken cancellationToken)
    {
        var notification = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.NotificationNotFound, "Melding niet gevonden.", DomainErrorKind.NotFound);
        if (notification.Status != NotificationStatus.Scheduled)
        {
            throw new DomainException(ErrorCodes.NotificationNotCancelable, "Alleen een geplande melding die nog niet is verstuurd, kan worden geannuleerd.", DomainErrorKind.Conflict);
        }

        notification.Status = NotificationStatus.Canceled;
        notification.CanceledAt = clock.UtcNow.UtcDateTime;
        notification.CanceledBy = actorId;
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry("notification.canceled", "Notification", id.ToString()), cancellationToken);
    }

    public async Task<Guid?> EnqueueAsync(SystemNotification notification, CancellationToken cancellationToken)
    {
        if (notification.SourceType is not null && notification.SourceId is { } sourceId
            && (db.Notifications.Local.Any(n => n.SourceType == notification.SourceType && n.SourceId == sourceId)
                || await db.Notifications.AnyAsync(n => n.SourceType == notification.SourceType && n.SourceId == sourceId, cancellationToken)))
        {
            return null;
        }

        var title = Truncate(notification.Title, Notification.TitleMaxLength);
        var body = Truncate(notification.Body, Notification.BodyMaxLength);
        return Add(title, body, notification.Category, notification.Audience, notification.DeepLink, null, notification.SourceType, notification.SourceId, null).Id;
    }

    private Notification Add(
        string title, string body, NotificationCategory category, NotificationAudience audience, string? deepLink,
        Guid? senderId, string? sourceType, Guid? sourceId, DateTime? scheduledAt)
    {
        var notification = new Notification
        {
            Id = IdGenerator.NewId(),
            Title = title.Trim(),
            Body = body.Trim(),
            Category = category,
            DeepLink = string.IsNullOrWhiteSpace(deepLink) ? null : deepLink.Trim(),
            AudienceJson = JsonSerializer.Serialize(audience, Json),
            SenderUserId = senderId,
            SourceType = sourceType,
            SourceId = sourceId,
            CreatedAt = clock.UtcNow.UtcDateTime,
            ScheduledAt = scheduledAt,
            Status = scheduledAt is null ? NotificationStatus.Sending : NotificationStatus.Scheduled,
        };
        db.Notifications.Add(notification);
        outbox.Enqueue(DispatchMessageType, new DispatchMessage(notification.Id), scheduledAt);
        return notification;
    }

    private static void Validate(string title, string body, string? deepLink, NotificationAudience audience)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > Notification.TitleMaxLength)
        {
            throw new DomainException(ErrorCodes.Validation, $"De titel is verplicht en maximaal {Notification.TitleMaxLength} tekens.");
        }

        if (string.IsNullOrWhiteSpace(body) || body.Trim().Length > Notification.BodyMaxLength)
        {
            throw new DomainException(ErrorCodes.Validation, $"De tekst is verplicht en maximaal {Notification.BodyMaxLength} tekens.");
        }

        if (!string.IsNullOrWhiteSpace(deepLink) && (!deepLink.Trim().StartsWith(DeepLinkScheme, StringComparison.Ordinal) || deepLink.Trim().Length > 200))
        {
            throw new DomainException(ErrorCodes.Validation, $"Een link in de app begint met {DeepLinkScheme}.");
        }

        if (audience.IsEmpty)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies een doelgroep.");
        }
    }

    private async Task AuthorizeAsync(NotificationDraft draft, UserAccess sender, CancellationToken cancellationToken)
    {
        var permissions = sender.Permissions;
        if ((draft.Category == NotificationCategory.Urgent || draft.Audience.Everyone) && !permissions.Contains(Permissions.NotificationSendUrgent))
        {
            throw new DomainException(ErrorCodes.Forbidden, "Dringende meldingen en meldingen aan iedereen vragen het recht notification.send.urgent.", DomainErrorKind.Forbidden);
        }

        if (draft.Category == NotificationCategory.System)
        {
            throw new DomainException(ErrorCodes.Validation, "De categorie Systeem is alleen voor meldingen van de app zelf.");
        }

        if (permissions.Contains(Permissions.NotificationSend))
        {
            return;
        }

        // Alleen notification.send.group: uitsluitend groepen waar de afzender zelf (als actief lid) in zit.
        var audience = draft.Audience;
        if (audience.Everyone || audience.Members || (audience.Roles?.Count ?? 0) > 0 || (audience.MemberIds?.Count ?? 0) > 0
            || (audience.Groups?.Count ?? 0) == 0 || sender.MemberId is not { } memberId)
        {
            throw new DomainException(ErrorCodes.Forbidden, "Je mag alleen meldingen versturen aan je eigen groep(en).", DomainErrorKind.Forbidden);
        }

        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var own = await db.GroupMemberships.AsNoTracking()
            .Where(gm => gm.MemberId == memberId && (gm.ValidFrom == null || gm.ValidFrom <= today) && (gm.ValidTo == null || gm.ValidTo >= today))
            .Select(gm => gm.GroupId).ToListAsync(cancellationToken);
        if (audience.Groups!.Except(own).Any())
        {
            throw new DomainException(ErrorCodes.Forbidden, "Je mag alleen meldingen versturen aan je eigen groep(en).", DomainErrorKind.Forbidden);
        }
    }

    private static string Truncate(string value, int max)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : string.Concat(trimmed.AsSpan(0, max - 1), "…");
    }

    /// <summary>Doelgroep van content (nieuws) als meldingsdoelgroep: Publiek → iedereen, Leden → leden, Beperkt → dezelfde doelgroepen.</summary>
    public static NotificationAudience AudienceFor(
        Modules.Content.Shared.ContentVisibility visibility, IEnumerable<(Modules.Content.Shared.AudienceType Type, string Ref)> audiences)
    {
        var list = audiences.ToList();
        return visibility switch
        {
            Modules.Content.Shared.ContentVisibility.Public => new NotificationAudience(Everyone: true),
            Modules.Content.Shared.ContentVisibility.Members => new NotificationAudience(Members: true),
            _ => new NotificationAudience(
                Roles: [.. list.Where(a => a.Type == Modules.Content.Shared.AudienceType.Role).Select(a => a.Ref)],
                Groups: [.. list.Where(a => a.Type == Modules.Content.Shared.AudienceType.Group).Select(a => Guid.Parse(a.Ref))],
                MemberIds: [.. list.Where(a => a.Type == Modules.Content.Shared.AudienceType.Member).Select(a => Guid.Parse(a.Ref))]),
        };
    }
}
