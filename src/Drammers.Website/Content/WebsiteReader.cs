using System.Security.Cryptography;
using System.Text;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Content.Website;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Website.Content;

public sealed record Hero(string? Eyebrow, string Title, string? Subtitle, string ImageUrl, HeroButton? Primary, HeroButton? Secondary);

public sealed record HeroButton(string Label, string Href);

public sealed record EventCard(
    Guid Id, string Title, string? Summary, DateTime StartAt, DateTime? EndAt, bool AllDay, string? Location, string Category, string CategoryCode,
    bool IsHighlight, string? Badge, string? ImageUrl);

public sealed record NewsCard(Guid Id, string Slug, string Title, string? Summary, DateTime PublishedAt, string? ImageUrl, string? Category);

public sealed record NewsArticle(NewsCard Card, string? BodyHtml, string? WebsiteBodyHtml);

public sealed record PageContent(string Slug, string Title, string? Intro, string BodyHtml, string? ImageUrl);

public sealed record KaderPerson(string Name, string? Function, string? PhotoUrl);

public sealed record CommitteeView(string Name, string Slug, IReadOnlyList<KaderPerson> Members);

public sealed record PrinceCard(Guid Id, int Year, string PrinceName, string? Name, string? Motto, string? PhotoUrl);

public sealed record AwardCard(AwardType Type, int Year, string Recipient, string Slug, string? Excerpt, string? BodyHtml, string? PhotoUrl);

public sealed record AlbumCard(Guid Id, string Title, DateOnly? Date, PhotoCategory Category, int PhotoCount, string? CoverUrl, string? Description);

public sealed record PhotoView(Guid Id, string ThumbnailUrl, string DisplayUrl, int? Width, int? Height, string? Caption, string? Photographer);

public sealed record ParadeView(
    string Name, DateOnly Date, TimeOnly StartTime, string? StartLocation, string? RouteDescription, DateTime RegistrationOpensAt,
    DateTime RegistrationClosesAt, bool RegistrationOpen, bool ArrivalTimesPublished, string? InfoHtml);

public sealed record MenuPage(string Slug, string Title);

/// <summary>
/// Leest de openbare inhoud voor de website (fase 21c): alleen wat voor een gast zichtbaar is (openbaar, gepubliceerd,
/// niet verlopen). Afbeeldingen gaan via het eigen media-adres (<see cref="MediaUrls"/>), niet via SAS-links.
/// </summary>
public sealed class WebsiteReader(DrammersDbContext db, IClock clock)
{
    /// <summary>Vaste pagina's die in het menu Vereniging staan (als ze online zijn), in deze volgorde.</summary>
    public static readonly string[] AssociationPages = ["over-ons", "dansgarde", "historie", "loillands"];

    private DateTime Now => clock.UtcNow.UtcDateTime;

    public async Task<WebsiteSettings> SettingsAsync(CancellationToken cancellationToken) =>
        await db.WebsiteSettings.AsNoTracking().SingleAsync(cancellationToken);

    public async Task<Hero> HeroAsync(CancellationToken cancellationToken)
    {
        var s = await SettingsAsync(cancellationToken);
        return new Hero(s.HeroEyebrow, s.HeroTitle, s.HeroSubtitle,
            MediaUrls.For("hero", Guid.Empty, s.HeroImageBlobPath) ?? "/_content/Drammers.Website/img/hero-optocht.jpg",
            s.HeroPrimaryLabel is null ? null : new HeroButton(s.HeroPrimaryLabel, WebsiteLinks.Href(s.HeroPrimaryLink!.Value)),
            s.HeroSecondaryLabel is null ? null : new HeroButton(s.HeroSecondaryLabel, WebsiteLinks.Href(s.HeroSecondaryLink!.Value)));
    }

    public async Task<IReadOnlyList<MenuPage>> MenuPagesAsync(CancellationToken cancellationToken)
    {
        var pages = await db.WebsitePages.AsNoTracking().Where(p => p.IsPublished && AssociationPages.Contains(p.Slug))
            .Select(p => new MenuPage(p.Slug, p.Title)).ToListAsync(cancellationToken);
        return [.. pages.OrderBy(p => Array.IndexOf(AssociationPages, p.Slug))];
    }

    // ----- Agenda --------------------------------------------------------------------------------------------------

    public async Task<IReadOnlyList<EventCard>> EventsAsync(int? take, string? categoryCode, CancellationToken cancellationToken)
    {
        var now = Now;
        var query = db.Events.AsNoTracking().VisibleTo(ContentViewer.Guest, now).Where(e => (e.EndAt ?? e.StartAt) >= now.Date);
        var rows = await query.OrderBy(e => e.StartAt)
            .Join(db.EventCategories, e => e.CategoryId, c => c.Id, (e, c) => new { e, c })
            .Where(x => categoryCode == null || x.c.Code == categoryCode)
            .Take(take ?? 200)
            .ToListAsync(cancellationToken);
        return [.. rows.Select(x => new EventCard(x.e.Id, x.e.Title, x.e.Summary, x.e.StartAt, x.e.EndAt, x.e.AllDay, x.e.LocationName,
            x.c.Name, x.c.Code, x.e.IsHighlight, x.e.BadgeText, MediaUrls.For("event", x.e.Id, x.e.ImageBlobPath)))];
    }

    public async Task<IReadOnlyList<EventCategory>> EventCategoriesAsync(CancellationToken cancellationToken) =>
        await db.EventCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);

    public async Task<Event?> EventAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Events.AsNoTracking().VisibleTo(ContentViewer.Guest, Now).SingleOrDefaultAsync(e => e.Id == id, cancellationToken);

    // ----- Nieuws --------------------------------------------------------------------------------------------------

    private IQueryable<Modules.Content.News.NewsItem> WebsiteNews() =>
        db.News.AsNoTracking().VisibleTo(ContentViewer.Guest, Now).Where(n => n.ShowOnWebsite && n.Slug != null);

    public async Task<(IReadOnlyList<NewsCard> Items, int Total)> NewsAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = WebsiteNews();
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(n => n.PublishAt ?? n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return ([.. rows.Select(ToCard)], total);
    }

    public async Task<NewsArticle?> NewsArticleAsync(string slug, CancellationToken cancellationToken)
    {
        var n = await WebsiteNews().SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);
        return n is null ? null : new NewsArticle(ToCard(n), MarkdownRenderer.ToSafeHtml(n.Body), MarkdownRenderer.ToSafeHtml(n.WebsiteBody));
    }

    private static NewsCard ToCard(Modules.Content.News.NewsItem n) =>
        new(n.Id, n.Slug!, n.Title, n.Summary, n.PublishAt ?? n.CreatedAt, MediaUrls.For("news", n.Id, n.ImageBlobPath), n.Category);

    // ----- Pagina's, kader, prinsen en onderscheidingen ------------------------------------------------------------

    public async Task<PageContent?> PageAsync(string slug, CancellationToken cancellationToken)
    {
        var p = await db.WebsitePages.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.IsPublished, cancellationToken);
        return p is null ? null : new PageContent(p.Slug, p.Title, p.Intro, MarkdownRenderer.ToSafeHtml(p.Body) ?? "", MediaUrls.For("page", p.Id, p.ImageBlobPath));
    }

    public async Task<IReadOnlyList<CommitteeView>> KaderAsync(CancellationToken cancellationToken)
    {
        var committees = await db.Committees.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(cancellationToken);
        var members = await db.CommitteeMembers.AsNoTracking().OrderBy(m => m.SortOrder).ToListAsync(cancellationToken);
        return [.. committees.Select(c => new CommitteeView(c.Name, c.Slug,
            [.. members.Where(m => m.CommitteeId == c.Id).Select(m => new KaderPerson(m.Name, m.Function, MediaUrls.For("kader", m.Id, m.PhotoBlobPath)))]))
            .Where(c => c.Members.Count > 0)];
    }

    public async Task<IReadOnlyList<PrinceCard>> PrincesAsync(PrinceKind kind, CancellationToken cancellationToken) =>
        [.. (await db.Princes.AsNoTracking().Where(p => p.Kind == kind).OrderByDescending(p => p.Year).ThenBy(p => p.PrinceName).ToListAsync(cancellationToken))
            .Select(p => new PrinceCard(p.Id, p.Year, p.PrinceName, p.Name, p.Motto, MediaUrls.For("prince", p.Id, p.PhotoBlobPath)))];

    public async Task<IReadOnlyList<AwardCard>> AwardsAsync(AwardType? type, CancellationToken cancellationToken) =>
        [.. (await db.Awards.AsNoTracking().Where(a => a.IsPublished && (type == null || a.Type == type))
                .OrderByDescending(a => a.Year).ThenBy(a => a.Type).ThenBy(a => a.Recipient).ToListAsync(cancellationToken))
            .Select(a => new AwardCard(a.Type, a.Year, a.Recipient, a.Slug, Excerpt(a.Body), null, MediaUrls.For("award", a.Id, a.PhotoBlobPath)))];

    public async Task<AwardCard?> AwardAsync(string slug, CancellationToken cancellationToken)
    {
        var a = await db.Awards.AsNoTracking().SingleOrDefaultAsync(x => x.Slug == slug && x.IsPublished, cancellationToken);
        return a is null ? null : new AwardCard(a.Type, a.Year, a.Recipient, a.Slug, Excerpt(a.Body), MarkdownRenderer.ToSafeHtml(a.Body),
            MediaUrls.For("award", a.Id, a.PhotoBlobPath));
    }

    // ----- Foto's --------------------------------------------------------------------------------------------------

    private IQueryable<Photo> VisiblePhotos() => db.Photos.AsNoTracking().Where(p => p.ProcessingStatus == PhotoProcessingStatus.Ready && !p.Hidden);

    public async Task<IReadOnlyList<AlbumCard>> AlbumsAsync(PhotoCategory? category, CancellationToken cancellationToken)
    {
        var albums = await db.PhotoAlbums.AsNoTracking().VisibleTo(ContentViewer.Guest, Now)
            .Where(a => category == null || a.Category == category)
            .OrderByDescending(a => a.AlbumDate).ThenByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id,
                a.Title,
                a.AlbumDate,
                a.Category,
                a.Description,
                Count = VisiblePhotos().Count(p => p.AlbumId == a.Id),
                Cover = VisiblePhotos().Where(p => p.AlbumId == a.Id && (a.CoverPhotoId == null || p.Id == a.CoverPhotoId))
                    .OrderBy(p => p.SortOrder).Select(p => new { p.Id, p.ThumbnailBlobPath }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);
        return [.. albums.Where(a => a.Count > 0).Select(a => new AlbumCard(a.Id, a.Title, a.AlbumDate, a.Category, a.Count,
            a.Cover is null ? null : MediaUrls.For("photo-thumb", a.Cover.Id, a.Cover.ThumbnailBlobPath), a.Description))];
    }

    public async Task<(AlbumCard Album, IReadOnlyList<PhotoView> Photos)?> AlbumAsync(Guid id, CancellationToken cancellationToken)
    {
        var album = await db.PhotoAlbums.AsNoTracking().VisibleTo(ContentViewer.Guest, Now).SingleOrDefaultAsync(a => a.Id == id, cancellationToken);
        if (album is null)
        {
            return null;
        }

        var photos = await VisiblePhotos().Where(p => p.AlbumId == id).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var views = photos.Select(p => new PhotoView(p.Id, MediaUrls.For("photo-thumb", p.Id, p.ThumbnailBlobPath)!,
            MediaUrls.For("photo", p.Id, p.DisplayBlobPath)!, p.Width, p.Height, p.Caption, p.Photographer)).ToList();
        return (new AlbumCard(album.Id, album.Title, album.AlbumDate, album.Category, views.Count, views.FirstOrDefault()?.ThumbnailUrl, album.Description), views);
    }

    // ----- Optocht -------------------------------------------------------------------------------------------------

    public async Task<ParadeView?> ParadeAsync(CancellationToken cancellationToken)
    {
        var p = await db.Parades.AsNoTracking().Join(db.CarnivalYears.Where(y => y.Active), p => p.CarnivalYearId, y => y.Id, (p, y) => p)
            .OrderByDescending(p => p.ParadeDate).FirstOrDefaultAsync(cancellationToken);
        return p is null ? null : new ParadeView(p.Name, p.ParadeDate, p.StartTime, p.StartLocation, p.RouteDescription, p.RegistrationOpensAt,
            p.RegistrationClosesAt, p.IsRegistrationOpen(Now), p.ArrivalTimesPublishedAt is not null, MarkdownRenderer.ToSafeHtml(p.InfoText));
    }

    private static string? Excerpt(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return null;
        }

        var plain = new StringBuilder();
        foreach (var c in markdown)
        {
            if (c is not ('#' or '*' or '_' or '>' or '`'))
            {
                plain.Append(c is '\r' or '\n' ? ' ' : c);
            }
        }

        var text = plain.ToString().Trim();
        return text.Length <= 180 ? text : text[..text.LastIndexOf(' ', 180)] + "…";
    }
}

/// <summary>
/// Media-adressen voor de website: <c>/media/{soort}/{id}?v={versie}</c>. De versie verandert als de afbeelding verandert,
/// zodat browsers en proxy's lang mogen cachen; het media-endpoint controleert zelf of de afbeelding openbaar is.
/// </summary>
public static class MediaUrls
{
    public static string? For(string kind, Guid id, string? blobPath) =>
        blobPath is null ? null : $"/media/{kind}/{(id == Guid.Empty ? "site" : id.ToString("N"))}?v={Version(blobPath)}";

    public static string Version(string blobPath) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(blobPath)))[..10];
}

/// <summary>Vaste bestemmingen van de heroknoppen op de website.</summary>
public static class WebsiteLinks
{
    public static string Href(WebsiteLink link) => link switch
    {
        WebsiteLink.Agenda => "/agenda",
        WebsiteLink.News => "/nieuws",
        WebsiteLink.Photos => "/fotos",
        WebsiteLink.Parade => "/optocht",
        WebsiteLink.ParadeRegistration => "/optocht/inschrijven",
        WebsiteLink.Membership => "/lid-worden/",
        WebsiteLink.Tickets => "/kaarten/",
        WebsiteLink.App => "/doe-mee#app",
        WebsiteLink.Contact => "/contact",
        _ => "/",
    };
}
