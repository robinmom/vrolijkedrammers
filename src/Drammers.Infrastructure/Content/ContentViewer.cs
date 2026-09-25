using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Content.Shared;

namespace Drammers.Infrastructure.Content;

/// <summary>Wie de content bekijkt: een gast (geen rollen) of een ingelogde gebruiker met zijn geldige rollen.</summary>
public sealed record ContentViewer(IReadOnlyCollection<string> RoleCodes)
{
    public const string MemberRole = "lid";

    public static readonly ContentViewer Guest = new([]);

    public bool IsMember => RoleCodes.Contains(MemberRole);
}

/// <summary>
/// Audience-filter (docs/07 §4) als EF-queryfilters: gepubliceerd (of gepland en het moment is bereikt), binnen het
/// publicatievenster, en Public / Members (rol Lid) / Restricted (een van de rollen in de audience).
/// </summary>
public static class ContentVisibilityFilters
{
    public static IQueryable<Event> VisibleTo(this IQueryable<Event> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var isMember = viewer.IsMember;
        return query.Where(e =>
            (e.Status == PublicationStatus.Published || (e.Status == PublicationStatus.Scheduled && e.PublishAt <= now))
            && (e.PublishAt == null || e.PublishAt <= now)
            && (e.Visibility == ContentVisibility.Public
                || (e.Visibility == ContentVisibility.Members && isMember)
                || (e.Visibility == ContentVisibility.Restricted
                    && e.Audiences.Any(a => a.AudienceType == AudienceType.Role && roles.Contains(a.AudienceRef)))));
    }

    public static IQueryable<NewsItem> VisibleTo(this IQueryable<NewsItem> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var isMember = viewer.IsMember;
        return query.Where(n =>
            (n.Status == PublicationStatus.Published || (n.Status == PublicationStatus.Scheduled && n.PublishAt <= now))
            && (n.PublishAt == null || n.PublishAt <= now)
            && (n.ExpireAt == null || n.ExpireAt > now)
            && (n.Visibility == ContentVisibility.Public
                || (n.Visibility == ContentVisibility.Members && isMember)
                || (n.Visibility == ContentVisibility.Restricted
                    && n.Audiences.Any(a => a.AudienceType == AudienceType.Role && roles.Contains(a.AudienceRef)))));
    }

    public static IQueryable<PhotoAlbum> VisibleTo(this IQueryable<PhotoAlbum> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var isMember = viewer.IsMember;
        return query.Where(a =>
            (a.Status == PublicationStatus.Published || (a.Status == PublicationStatus.Scheduled && a.PublishAt <= now))
            && (a.PublishAt == null || a.PublishAt <= now)
            && (a.Visibility == ContentVisibility.Public
                || (a.Visibility == ContentVisibility.Members && isMember)
                || (a.Visibility == ContentVisibility.Restricted
                    && a.Audiences.Any(x => x.AudienceType == AudienceType.Role && roles.Contains(x.AudienceRef)))));
    }
}
