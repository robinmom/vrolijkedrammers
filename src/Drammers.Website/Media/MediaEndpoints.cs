using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Photos;
using Drammers.SharedKernel.Time;
using Drammers.Website.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

namespace Drammers.Website.Media;

/// <summary>
/// Afbeeldingen voor de website (fase 21c) via <c>/media/{soort}/{id}</c>. Anders dan de SAS-links van de API (15 minuten)
/// zijn deze adressen te cachen. Elke aanvraag controleert of het item openbaar is; verborgen foto's en ledeninhoud
/// geven 404.
/// </summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapWebsiteMedia(this IEndpointRouteBuilder app)
    {
        app.MapGet("/media/{kind}/{id}", ServeAsync).AllowAnonymous().ExcludeFromDescription();
        return app;
    }

    private static async Task<IResult> ServeAsync(
        string kind, string id, HttpContext context, [FromServices] DrammersDbContext db, [FromServices] IFileStore files, [FromServices] IClock clock,
        CancellationToken cancellationToken)
    {
        var found = await ResolveAsync(kind, id, db, clock.UtcNow.UtcDateTime, cancellationToken);
        if (found is not { } media)
        {
            return Results.NotFound();
        }

        var etag = new EntityTagHeaderValue($"\"{MediaUrls.Version(media.Path)}\"");
        var response = context.Response;
        response.Headers.ETag = etag.ToString();
        // Foto's korter: een verborgen foto (portretrecht) moet snel van de site af zijn.
        var maxAge = media.Container == FileContainers.PhotosDerived ? 3600 : 86400;
        response.Headers.CacheControl = $"public, max-age={maxAge}";
        if (context.Request.Headers.IfNoneMatch.ToString().Contains(etag.Tag.ToString(), StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        var stream = await files.OpenReadAsync(media.Container, media.Path, cancellationToken);
        return Results.Stream(stream, media.Path.EndsWith(".png", StringComparison.Ordinal) ? "image/png" : "image/jpeg");
    }

    private sealed record Media(string Container, string Path);

    private static async Task<Media?> ResolveAsync(string kind, string id, DrammersDbContext db, DateTime now, CancellationToken cancellationToken)
    {
        if (kind == "hero")
        {
            var hero = await db.WebsiteSettings.AsNoTracking().Select(s => s.HeroImageBlobPath).SingleAsync(cancellationToken);
            return Content(hero);
        }

        if (!Guid.TryParseExact(id, "N", out var guid))
        {
            return null;
        }

        string? path;
        switch (kind)
        {
            case "news":
                path = await db.News.AsNoTracking().VisibleTo(ContentViewer.Guest, now).Where(n => n.Id == guid && n.ShowOnWebsite)
                    .Select(n => n.ImageBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "event":
                path = await db.Events.AsNoTracking().VisibleTo(ContentViewer.Guest, now).Where(e => e.Id == guid)
                    .Select(e => e.ImageBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "page":
                path = await db.WebsitePages.AsNoTracking().Where(p => p.Id == guid && p.IsPublished).Select(p => p.ImageBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "kader":
                path = await db.CommitteeMembers.AsNoTracking().Where(m => m.Id == guid).Select(m => m.PhotoBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "prince":
                path = await db.Princes.AsNoTracking().Where(p => p.Id == guid).Select(p => p.PhotoBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "award":
                path = await db.Awards.AsNoTracking().Where(a => a.Id == guid && a.IsPublished).Select(a => a.PhotoBlobPath).SingleOrDefaultAsync(cancellationToken);
                return Content(path);
            case "mailing":
                // Foto in een mailing (fase 27a): alleen als een mailing hem gebruikt.
                var upload = $"{UploadedImages.Folder}/{guid:N}.jpg";
                return await db.Mailings.AsNoTracking().AnyAsync(m => m.Blocks.Contains(upload), cancellationToken) ? Content(upload) : null;
            case "photo" or "photo-thumb":
                var albums = db.PhotoAlbums.AsNoTracking().VisibleTo(ContentViewer.Guest, now).Select(a => a.Id);
                var photo = await db.Photos.AsNoTracking()
                    .Where(p => p.Id == guid && p.ProcessingStatus == PhotoProcessingStatus.Ready && !p.Hidden && albums.Contains(p.AlbumId))
                    .Select(p => new { p.DisplayBlobPath, p.ThumbnailBlobPath }).SingleOrDefaultAsync(cancellationToken);
                path = kind == "photo" ? photo?.DisplayBlobPath : photo?.ThumbnailBlobPath;
                return path is null ? null : new Media(FileContainers.PhotosDerived, path);
            default:
                return null;
        }
    }

    private static Media? Content(string? path) => path is null ? null : new Media(FileContainers.Content, path);
}
