using Drammers.Api.Content;
using Drammers.Api.Contracts;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Photos;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Fotoalbums (docs/05 §2): alleen verwerkte, niet-verborgen foto's.</summary>
[ApiController]
[AllowAnonymous]
[Route("api/v1/photo-albums")]
public sealed class PhotoAlbumsController(DrammersDbContext db, ContentViewerResolver viewers, ContentUrls urls, IClock clock) : ControllerBase
{
    [HttpGet]
    [PublicCache]
    [ProducesResponseType<PagedResult<PhotoAlbumResponse>>(StatusCodes.Status200OK)]
    public async Task<PagedResult<PhotoAlbumResponse>> Search([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        var (p, size) = PagedResult<PhotoAlbumResponse>.Normalize(page, pageSize);
        var query = db.PhotoAlbums.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime);
        var total = await query.CountAsync(cancellationToken);
        var albums = await query.OrderByDescending(a => a.AlbumDate).ThenByDescending(a => a.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(cancellationToken);
        var items = new List<PhotoAlbumResponse>();
        foreach (var album in albums)
        {
            items.Add(await ToResponseAsync(album, cancellationToken));
        }

        return new PagedResult<PhotoAlbumResponse>(items, p, size, total);
    }

    [HttpGet("{id:guid}")]
    [PublicCache]
    [ProducesResponseType<PhotoAlbumResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<PhotoAlbumResponse> Get(Guid id, CancellationToken cancellationToken) =>
        await ToResponseAsync(await FindVisibleAsync(id, cancellationToken), cancellationToken);

    [HttpGet("{id:guid}/photos")]
    [PublicCache]
    [ProducesResponseType<IReadOnlyList<PhotoResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IReadOnlyList<PhotoResponse>> Photos(Guid id, CancellationToken cancellationToken)
    {
        await FindVisibleAsync(id, cancellationToken);
        var photos = await Visible(db.Photos.AsNoTracking()).Where(p => p.AlbumId == id).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var result = new List<PhotoResponse>();
        foreach (var photo in photos)
        {
            result.Add(new PhotoResponse(photo.Id,
                (await urls.ForAsync(FileContainers.PhotosDerived, photo.ThumbnailBlobPath, cancellationToken))!,
                (await urls.ForAsync(FileContainers.PhotosDerived, photo.DisplayBlobPath, cancellationToken))!,
                photo.Width, photo.Height, photo.Caption, photo.Photographer, photo.TakenAt));
        }

        return result;
    }

    private async Task<PhotoAlbum> FindVisibleAsync(Guid id, CancellationToken cancellationToken) =>
        await db.PhotoAlbums.AsNoTracking().VisibleTo(await viewers.ResolveAsync(), clock.UtcNow.UtcDateTime).SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
        ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);

    private static IQueryable<Photo> Visible(IQueryable<Photo> photos) =>
        photos.Where(p => p.ProcessingStatus == PhotoProcessingStatus.Ready && !p.Hidden);

    private async Task<PhotoAlbumResponse> ToResponseAsync(PhotoAlbum album, CancellationToken cancellationToken)
    {
        var visible = Visible(db.Photos.AsNoTracking()).Where(p => p.AlbumId == album.Id);
        var count = await visible.CountAsync(cancellationToken);
        var cover = await visible.Where(p => album.CoverPhotoId == null || p.Id == album.CoverPhotoId)
            .OrderBy(p => p.SortOrder).Select(p => p.ThumbnailBlobPath).FirstOrDefaultAsync(cancellationToken);
        return new PhotoAlbumResponse(album.Id, album.Title, album.AlbumDate, album.Description, count,
            await urls.ForAsync(FileContainers.PhotosDerived, cover, cancellationToken));
    }
}

public sealed record PhotoAlbumResponse(Guid Id, string Title, DateOnly? AlbumDate, string? Description, int PhotoCount, string? CoverUrl);

public sealed record PhotoResponse(Guid Id, string ThumbnailUrl, string DisplayUrl, int? Width, int? Height, string? Caption, string? Photographer, DateTime? TakenAt);
