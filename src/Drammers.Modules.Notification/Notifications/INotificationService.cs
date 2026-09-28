namespace Drammers.Modules.Notification.Notifications;

/// <summary>Een melding vanuit het systeem (nieuws bij publicatie, later optocht, tickets, …); geen rechtencontrole.</summary>
public sealed record SystemNotification(
    string Title,
    string Body,
    NotificationCategory Category,
    NotificationAudience Audience,
    string? DeepLink = null,
    string? SourceType = null,
    Guid? SourceId = null);

/// <summary>Domeininterface voor meldingen (fase 10), bruikbaar door latere fasen. Verzending loopt via de outbox.</summary>
public interface INotificationService
{
    /// <summary>
    /// Zet de melding klaar in dezelfde unit of work als de aanroeper (de aanroeper doet <c>SaveChanges</c>). Met een bron
    /// is het idempotent: bestaat er al een melding voor die bron, dan gebeurt er niets en is het resultaat <c>null</c>.
    /// </summary>
    Task<Guid?> EnqueueAsync(SystemNotification notification, CancellationToken cancellationToken);
}
