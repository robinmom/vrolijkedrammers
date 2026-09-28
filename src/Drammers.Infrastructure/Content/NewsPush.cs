using System.Text.RegularExpressions;
using Drammers.Infrastructure.Notifications;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Shared;
using Drammers.Modules.Notification.Notifications;

namespace Drammers.Infrastructure.Content;

/// <summary>
/// Push bij publicatie van nieuws (fase 10): zodra een bericht met <see cref="NewsItem.PushOnPublish"/> zichtbaar wordt,
/// één melding aan dezelfde doelgroep als het bericht. Eén keer per bericht (bron <c>News</c>/id), ook bij opnieuw
/// publiceren of bewerken.
/// </summary>
public static partial class NewsPush
{
    public const string SourceType = "News";

    public static async Task EnqueueIfLiveAsync(INotificationService notifications, NewsItem news, DateTime now, CancellationToken cancellationToken)
    {
        var live = news.Status == PublicationStatus.Published || (news.Status == PublicationStatus.Scheduled && news.PublishAt <= now);
        if (!news.PushOnPublish || !live)
        {
            return;
        }

        var audience = NotificationAdministration.AudienceFor(news.Visibility, news.Audiences.Select(a => (a.AudienceType, a.AudienceRef)));
        await notifications.EnqueueAsync(
            new SystemNotification(news.Title, BodyText(news), NotificationCategory.News, audience, $"drammers://nieuws/{news.Id}", SourceType, news.Id),
            cancellationToken);
    }

    /// <summary>De samenvatting, anders het begin van de tekst zonder Markdown-tekens.</summary>
    public static string BodyText(NewsItem news)
    {
        if (!string.IsNullOrWhiteSpace(news.Summary))
        {
            return news.Summary;
        }

        var plain = MarkdownNoise().Replace(news.Body, " ");
        return Whitespace().Replace(plain, " ").Trim();
    }

    [GeneratedRegex(@"[#*_>`~\[\]()!|]+|https?://\S+")]
    private static partial Regex MarkdownNoise();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
