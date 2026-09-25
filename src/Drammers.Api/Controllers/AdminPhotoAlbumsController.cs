using System.ComponentModel.DataAnnotations;
using Drammers.Api.Authorization;
using Drammers.Api.Content;
using Drammers.Infrastructure.Content;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Photos;
using Drammers.SharedKernel.Authorization;
using Drammers.SharedKernel.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Api.Controllers;

/// <summary>Fotoalbums en foto's beheren: uploaden (meerdere tegelijk), volgorde, verbergen (docs/05 §6).</summary>
[ApiController]
[Route("api/v1/admin")]
[RequirePermission(Permissions.PhotoManage)]
public sealed class AdminPhotoAlbumsController(DrammersDbContext db, ContentAdministration content, ContentUrls urls) : ControllerBase
{
    public const int MaxPhotosPerUpload = 20;

    [HttpGet("photo-albums")]
    [ProducesResponseType<IReadOnlyList<AdminAlbumSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IReadOnlyList<AdminAlbumSummaryResponse>> GetAll(CancellationToken cancellationToken) =>
        await db.PhotoAlbums.AsNoTracking().OrderByDescending(a => a.AlbumDate).ThenByDescending(a => a.CreatedAt)
            .Select(a => new AdminAlbumSummaryResponse(a.Id, a.Title, a.AlbumDate, a.Visibility, a.Status, db.Photos.Count(p => p.AlbumId == a.Id)))
            .ToListAsync(cancellationToken);

    [HttpGet("photo-albums/{id:guid}")]
    [ProducesResponseType<AdminAlbumResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<AdminAlbumResponse> Get(Guid id, CancellationToken cancellationToken)
    {
        var album = await db.PhotoAlbums.AsNoTracking().Include(a => a.Audiences).SingleOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new DomainException(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
        var photos = await db.Photos.AsNoTracking().Where(p => p.AlbumId == id).OrderBy(p => p.SortOrder).ToListAsync(cancellationToken);
        var items = new List<AdminPhotoResponse>();
        foreach (var p in photos)
        {
            items.Add(new AdminPhotoResponse(p.Id, p.ProcessingStatus, p.Hidden, p.Caption, p.Photographer,
                await urls.ForAsync(FileContainers.PhotosDerived, p.ThumbnailBlobPath, cancellationToken)));
        }

        return new AdminAlbumResponse(album.Id, album.Title, album.AlbumDate, album.Description, album.EventId, album.CoverPhotoId,
            PublicationResponse.From(album.Visibility, album.Audiences.Select(a => (a.AudienceType, a.AudienceRef)), album.Status, album.PublishAt), items);
    }

    [HttpPost("photo-albums")]
    [ProducesResponseType<CreatedResponse>(StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedResponse>> Create(AlbumRequest request, CancellationToken cancellationToken)
    {
        var id = await content.CreateAlbumAsync(request.ToInput(), cancellationToken);
        return Created($"/api/v1/admin/photo-albums/{id}", new CreatedResponse(id));
    }

    [HttpPut("photo-albums/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, AlbumRequest request, CancellationToken cancellationToken)
    {
        await content.UpdateAlbumAsync(id, request.ToInput(), cancellationToken);
        return NoContent();
    }

    [HttpDelete("photo-albums/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await content.DeleteAlbumAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Foto's uploaden (maximaal 20 per keer); ze verschijnen zodra de verwerking klaar is.</summary>
    [HttpPost("photo-albums/{id:guid}/photos")]
    [RequestSizeLimit(MaxPhotosPerUpload * ContentFiles.MaxPhotoBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxPhotosPerUpload * ContentFiles.MaxPhotoBytes)]
    [ProducesResponseType<IReadOnlyList<CreatedResponse>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<CreatedResponse>>> Upload(Guid id, IFormFileCollection files, CancellationToken cancellationToken)
    {
        if (files.Count is 0 or > MaxPhotosPerUpload)
        {
            throw new DomainException(ErrorCodes.Validation, $"Upload 1 tot {MaxPhotosPerUpload} foto's per keer.");
        }

        var ids = await content.UploadPhotosAsync(id, [.. files.Select(f => new UploadedFile(f.FileName, f.Length, f.OpenReadStream))], cancellationToken);
        return Created($"/api/v1/admin/photo-albums/{id}", ids.Select(x => new CreatedResponse(x)).ToList());
    }

    [HttpPut("photo-albums/{id:guid}/photo-order")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetOrder(Guid id, PhotoOrderRequest request, CancellationToken cancellationToken)
    {
        await content.SetPhotoOrderAsync(id, request.PhotoIds, cancellationToken);
        return NoContent();
    }

    [HttpPut("photo-albums/{id:guid}/cover/{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetCover(Guid id, Guid photoId, CancellationToken cancellationToken)
    {
        await content.SetCoverAsync(id, photoId, cancellationToken);
        return NoContent();
    }

    [HttpPut("photos/{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdatePhoto(Guid photoId, PhotoUpdateRequest request, CancellationToken cancellationToken)
    {
        await content.UpdatePhotoAsync(photoId, request.Hidden, request.Caption, request.Photographer, cancellationToken);
        return NoContent();
    }

    [HttpDelete("photos/{photoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeletePhoto(Guid photoId, CancellationToken cancellationToken)
    {
        await content.DeletePhotoAsync(photoId, cancellationToken);
        return NoContent();
    }
}

public sealed record PhotoOrderRequest([Required] IReadOnlyList<Guid> PhotoIds);

public sealed record PhotoUpdateRequest(bool Hidden, [StringLength(500)] string? Caption, [StringLength(100)] string? Photographer);

public sealed record AdminAlbumSummaryResponse(
    Guid Id, string Title, DateOnly? AlbumDate, Modules.Content.Shared.ContentVisibility Visibility, Modules.Content.Shared.PublicationStatus Status, int PhotoCount);

public sealed record AdminAlbumResponse(
    Guid Id, string Title, DateOnly? AlbumDate, string? Description, Guid? EventId, Guid? CoverPhotoId, PublicationResponse Publication,
    IReadOnlyList<AdminPhotoResponse> Photos);

public sealed record AdminPhotoResponse(Guid Id, PhotoProcessingStatus ProcessingStatus, bool Hidden, string? Caption, string? Photographer, string? ThumbnailUrl);
