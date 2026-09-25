using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Content.Shared;

namespace Drammers.Infrastructure.Content;

/// <summary>
/// Wie de content bekijkt: een gast (niets) of een ingelogde gebruiker met zijn geldige rollen, en als hij een actief
/// lid is ook zijn lid-id en groepen (doelgroepen Groep en Lid, fase 8).
/// </summary>
public sealed record ContentViewer(IReadOnlyCollection<string> RoleCodes, string? MemberRef = null, IReadOnlyCollection<string>? GroupRefs = null)
{
    public const string MemberRole = "lid";

    public static readonly ContentViewer Guest = new([]);

    public bool IsMember => RoleCodes.Contains(MemberRole);
}

/// <summary>
/// Audience-filter (docs/07 §4) als EF-queryfilters: gepubliceerd (of gepland en het moment is bereikt), binnen het
/// publicatievenster, en Public / Members (rol Lid) / Restricted (een rol, groep of het lid zelf in de audience).
/// </summary>
public static class ContentVisibilityFilters
{
    public static IQueryable<Event> VisibleTo(this IQueryable<Event> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var groups = (viewer.GroupRefs ?? []).ToList();
        var memberRef = viewer.MemberRef;
        var isMember = viewer.IsMember;
        return query.Where(e =>
            (e.Status == PublicationStatus.Published || (e.Status == PublicationStatus.Scheduled && e.PublishAt <= now))
            && (e.PublishAt == null || e.PublishAt <= now)
            && (e.Visibility == ContentVisibility.Public
                || (e.Visibility == ContentVisibility.Members && isMember)
                || (e.Visibility == ContentVisibility.Restricted
                    && e.Audiences.Any(a => (a.AudienceType == AudienceType.Role && roles.Contains(a.AudienceRef))
                        || (a.AudienceType == AudienceType.Group && groups.Contains(a.AudienceRef))
                        || (a.AudienceType == AudienceType.Member && a.AudienceRef == memberRef)))));
    }

    public static IQueryable<NewsItem> VisibleTo(this IQueryable<NewsItem> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var groups = (viewer.GroupRefs ?? []).ToList();
        var memberRef = viewer.MemberRef;
        var isMember = viewer.IsMember;
        return query.Where(n =>
            (n.Status == PublicationStatus.Published || (n.Status == PublicationStatus.Scheduled && n.PublishAt <= now))
            && (n.PublishAt == null || n.PublishAt <= now)
            && (n.ExpireAt == null || n.ExpireAt > now)
            && (n.Visibility == ContentVisibility.Public
                || (n.Visibility == ContentVisibility.Members && isMember)
                || (n.Visibility == ContentVisibility.Restricted
                    && n.Audiences.Any(a => (a.AudienceType == AudienceType.Role && roles.Contains(a.AudienceRef))
                        || (a.AudienceType == AudienceType.Group && groups.Contains(a.AudienceRef))
                        || (a.AudienceType == AudienceType.Member && a.AudienceRef == memberRef)))));
    }

    public static IQueryable<PhotoAlbum> VisibleTo(this IQueryable<PhotoAlbum> query, ContentViewer viewer, DateTime now)
    {
        var roles = viewer.RoleCodes.ToList();
        var groups = (viewer.GroupRefs ?? []).ToList();
        var memberRef = viewer.MemberRef;
        var isMember = viewer.IsMember;
        return query.Where(a =>
            (a.Status == PublicationStatus.Published || (a.Status == PublicationStatus.Scheduled && a.PublishAt <= now))
            && (a.PublishAt == null || a.PublishAt <= now)
            && (a.Visibility == ContentVisibility.Public
                || (a.Visibility == ContentVisibility.Members && isMember)
                || (a.Visibility == ContentVisibility.Restricted
                    && a.Audiences.Any(x => (x.AudienceType == AudienceType.Role && roles.Contains(x.AudienceRef))
                        || (x.AudienceType == AudienceType.Group && groups.Contains(x.AudienceRef))
                        || (x.AudienceType == AudienceType.Member && x.AudienceRef == memberRef)))));
    }
}
