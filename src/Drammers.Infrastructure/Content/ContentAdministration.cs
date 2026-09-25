using System.Text.Json;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Events;
using Drammers.Modules.Content.News;
using Drammers.Modules.Content.Photos;
using Drammers.Modules.Content.Shared;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;
using Drammers.SharedKernel.Messaging;
using Drammers.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Content;

public sealed record PublicationInput(ContentVisibility Visibility, IReadOnlyCollection<string> AudienceRoles, PublicationStatus Status, DateTime? PublishAt);

public sealed record EventInput(
    int CategoryId, string Title, string? Summary, string? Description, DateTime StartAt, DateTime? EndAt, bool AllDay,
    string? LocationName, string? LocationAddress, decimal? Latitude, decimal? Longitude, bool IsHighlight, string? BadgeText,
    PublicationInput Publication);

public sealed record NewsInput(string Title, string? Summary, string Body, string? Category, DateTime? ExpireAt, PublicationInput Publication);

public sealed record AlbumInput(string Title, DateOnly? AlbumDate, string? Description, Guid? EventId, PublicationInput Publication);

public sealed record UploadedFile(string FileName, long Length, Func<Stream> Open);

/// <summary>
/// Contentbeheer (fase 5): events, nieuws en fotoalbums. Publiceren, archiveren en verwijderen worden geaudit; bestanden
/// lopen via de upload-pijplijn (<see cref="ContentFiles"/>).
/// </summary>
public sealed class ContentAdministration(
    DrammersDbContext db, ContentFiles contentFiles, IFileStore files, IOutbox outbox, IAuditLogger audit, ICurrentActor actor, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // ----- Events --------------------------------------------------------------------------------------------------

    public async Task<Guid> CreateEventAsync(EventInput input, CancellationToken cancellationToken)
    {
        var yearId = await ActiveYearIdAsync(cancellationToken);
        var e = new Event { Id = IdGenerator.NewId(), CarnivalYearId = yearId, Title = input.Title };
        await ApplyAsync(e, input, cancellationToken);
        db.Events.Add(e);
        await SaveWithAuditAsync("event.created", "Event", e.Id, input, cancellationToken);
        return e.Id;
    }

    public async Task UpdateEventAsync(Guid id, EventInput input, CancellationToken cancellationToken)
    {
        var e = await db.Events.Include(x => x.Audiences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var wasPublished = e.Status == PublicationStatus.Published;
        await ApplyAsync(e, input, cancellationToken);
        var action = !wasPublished && e.Status == PublicationStatus.Published ? "event.published" : "event.updated";
        await SaveWithAuditAsync(action, "Event", e.Id, input, cancellationToken);
    }

    public async Task DeleteEventAsync(Guid id, CancellationToken cancellationToken)
    {
        var e = await db.Events.Include(x => x.Attachments).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var blobs = e.Attachments.Select(a => a.BlobPath).Append(e.ImageBlobPath).OfType<string>().ToList();
        db.Events.Remove(e);
        await SaveWithAuditAsync("event.deleted", "Event", id, new { e.Title }, cancellationToken);
        await DeleteBlobsAsync(FileContainers.Content, blobs, cancellationToken);
    }

    public async Task SetEventImageAsync(Guid id, UploadedFile file, CancellationToken cancellationToken)
    {
        var e = await db.Events.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        await using var stream = file.Open();
        var stored = await contentFiles.StoreImageAsync(stream, file.Length, $"events/{id:N}", cancellationToken);
        var previous = e.ImageBlobPath;
        e.ImageBlobPath = stored.Path;
        await db.SaveChangesAsync(cancellationToken);
        await DeleteBlobsAsync(FileContainers.Content, [previous], cancellationToken);
    }

    public async Task<Guid> AddEventAttachmentAsync(Guid id, UploadedFile file, CancellationToken cancellationToken)
    {
        if (!await db.Events.AnyAsync(x => x.Id == id, cancellationToken))
        {
            throw NotFound();
        }

        await using var stream = file.Open();
        var stored = await contentFiles.StoreAttachmentAsync(stream, file.Length, $"events/{id:N}/attachments", cancellationToken);
        var attachment = new EventAttachment
        {
            Id = IdGenerator.NewId(),
            EventId = id,
            FileName = SafeFileName(file.FileName, stored.Kind),
            BlobPath = stored.Path,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
        };
        db.EventAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);
        return attachment.Id;
    }

    public async Task DeleteEventAttachmentAsync(Guid id, Guid attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await db.EventAttachments.SingleOrDefaultAsync(a => a.Id == attachmentId && a.EventId == id, cancellationToken) ?? throw NotFound();
        db.EventAttachments.Remove(attachment);
        await db.SaveChangesAsync(cancellationToken);
        await DeleteBlobsAsync(FileContainers.Content, [attachment.BlobPath], cancellationToken);
    }

    // ----- Nieuws --------------------------------------------------------------------------------------------------

    public async Task<Guid> CreateNewsAsync(NewsInput input, CancellationToken cancellationToken)
    {
        var n = new NewsItem { Id = IdGenerator.NewId(), Title = input.Title, Body = input.Body, AuthorUserId = actor.UserId };
        Apply(n, input);
        db.News.Add(n);
        await SaveWithAuditAsync("news.created", "News", n.Id, input, cancellationToken);
        return n.Id;
    }

    public async Task UpdateNewsAsync(Guid id, NewsInput input, CancellationToken cancellationToken)
    {
        var n = await db.News.Include(x => x.Audiences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var wasPublished = n.Status == PublicationStatus.Published;
        Apply(n, input);
        var action = !wasPublished && n.Status == PublicationStatus.Published ? "news.published" : "news.updated";
        await SaveWithAuditAsync(action, "News", n.Id, input, cancellationToken);
    }

    public async Task SetNewsStatusAsync(Guid id, PublicationStatus status, DateTime? publishAt, CancellationToken cancellationToken)
    {
        var n = await db.News.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        n.Status = status;
        n.PublishAt = status switch
        {
            PublicationStatus.Scheduled => publishAt ?? throw new DomainException(ErrorCodes.Validation, "Kies een publicatiemoment."),
            PublicationStatus.Published => n.PublishAt is { } at && at <= clock.UtcNow.UtcDateTime ? at : clock.UtcNow.UtcDateTime,
            _ => n.PublishAt,
        };
        var action = status switch
        {
            PublicationStatus.Published => "news.published",
            PublicationStatus.Scheduled => "news.scheduled",
            PublicationStatus.Archived => "news.archived",
            _ => "news.updated",
        };
        await SaveWithAuditAsync(action, "News", id, new { status, n.PublishAt }, cancellationToken);
    }

    public async Task DeleteNewsAsync(Guid id, CancellationToken cancellationToken)
    {
        var n = await db.News.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        db.News.Remove(n);
        await SaveWithAuditAsync("news.deleted", "News", id, new { n.Title }, cancellationToken);
        await DeleteBlobsAsync(FileContainers.Content, [n.ImageBlobPath], cancellationToken);
    }

    public async Task SetNewsImageAsync(Guid id, UploadedFile file, CancellationToken cancellationToken)
    {
        var n = await db.News.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        await using var stream = file.Open();
        var stored = await contentFiles.StoreImageAsync(stream, file.Length, $"news/{id:N}", cancellationToken);
        var previous = n.ImageBlobPath;
        n.ImageBlobPath = stored.Path;
        await db.SaveChangesAsync(cancellationToken);
        await DeleteBlobsAsync(FileContainers.Content, [previous], cancellationToken);
    }

    // ----- Fotoalbums ----------------------------------------------------------------------------------------------

    public async Task<Guid> CreateAlbumAsync(AlbumInput input, CancellationToken cancellationToken)
    {
        var album = new PhotoAlbum { Id = IdGenerator.NewId(), Title = input.Title, CarnivalYearId = await ActiveYearIdOrNullAsync(cancellationToken) };
        Apply(album, input);
        db.PhotoAlbums.Add(album);
        await SaveWithAuditAsync("photo-album.created", "PhotoAlbum", album.Id, input, cancellationToken);
        return album.Id;
    }

    public async Task UpdateAlbumAsync(Guid id, AlbumInput input, CancellationToken cancellationToken)
    {
        var album = await db.PhotoAlbums.Include(x => x.Audiences).SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var wasPublished = album.Status == PublicationStatus.Published;
        Apply(album, input);
        var action = !wasPublished && album.Status == PublicationStatus.Published ? "photo-album.published" : "photo-album.updated";
        await SaveWithAuditAsync(action, "PhotoAlbum", id, input, cancellationToken);
    }

    public async Task DeleteAlbumAsync(Guid id, CancellationToken cancellationToken)
    {
        var album = await db.PhotoAlbums.SingleOrDefaultAsync(x => x.Id == id, cancellationToken) ?? throw NotFound();
        var photos = await db.Photos.Where(p => p.AlbumId == id).ToListAsync(cancellationToken);
        db.PhotoAlbums.Remove(album);
        await SaveWithAuditAsync("photo-album.deleted", "PhotoAlbum", id, new { album.Title, photos = photos.Count }, cancellationToken);
        foreach (var photo in photos)
        {
            await DeletePhotoBlobsAsync(photo, cancellationToken);
        }
    }

    /// <summary>Zet foto's in quarantaine en plant de verwerking; de foto's verschijnen zodra ze verwerkt zijn.</summary>
    public async Task<IReadOnlyList<Guid>> UploadPhotosAsync(Guid albumId, IReadOnlyList<UploadedFile> uploads, CancellationToken cancellationToken)
    {
        if (!await db.PhotoAlbums.AnyAsync(a => a.Id == albumId, cancellationToken))
        {
            throw NotFound();
        }

        var nextOrder = (await db.Photos.Where(p => p.AlbumId == albumId).MaxAsync(p => (int?)p.SortOrder, cancellationToken) ?? 0) + 1;
        var ids = new List<Guid>();
        foreach (var upload in uploads)
        {
            await using var stream = upload.Open();
            var (quarantinePath, _) = await contentFiles.QuarantineAsync(stream, upload.Length, FileTypeInspector.Images, ContentFiles.MaxPhotoBytes, cancellationToken);
            var photo = new Photo
            {
                Id = IdGenerator.NewId(),
                AlbumId = albumId,
                OriginalBlobPath = quarantinePath,
                ProcessingStatus = PhotoProcessingStatus.Pending,
                SortOrder = nextOrder++,
                UploadedBy = actor.UserId,
                UploadedAt = clock.UtcNow.UtcDateTime,
            };
            db.Photos.Add(photo);
            outbox.Enqueue(PhotoProcessingHandler.MessageType, new PhotoProcessingHandler.PhotoMessage(photo.Id));
            ids.Add(photo.Id);
        }

        await db.SaveChangesAsync(cancellationToken);
        return ids;
    }

    public async Task SetPhotoOrderAsync(Guid albumId, IReadOnlyList<Guid> photoIds, CancellationToken cancellationToken)
    {
        var photos = await db.Photos.Where(p => p.AlbumId == albumId).ToListAsync(cancellationToken);
        for (var i = 0; i < photoIds.Count; i++)
        {
            var photo = photos.SingleOrDefault(p => p.Id == photoIds[i]) ?? throw NotFound();
            photo.SortOrder = i + 1;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdatePhotoAsync(Guid photoId, bool hidden, string? caption, string? photographer, CancellationToken cancellationToken)
    {
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken) ?? throw NotFound();
        var hiddenChanged = photo.Hidden != hidden;
        (photo.Hidden, photo.Caption, photo.Photographer) = (hidden, caption, photographer);
        if (hiddenChanged)
        {
            await SaveWithAuditAsync(hidden ? "photo.hidden" : "photo.unhidden", "Photo", photoId, new { hidden }, cancellationToken);
        }
        else
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeletePhotoAsync(Guid photoId, CancellationToken cancellationToken)
    {
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken) ?? throw NotFound();
        db.Photos.Remove(photo);
        await db.PhotoAlbums.Where(a => a.CoverPhotoId == photoId).ExecuteUpdateAsync(s => s.SetProperty(a => a.CoverPhotoId, (Guid?)null), cancellationToken);
        await SaveWithAuditAsync("photo.deleted", "Photo", photoId, null, cancellationToken);
        await DeletePhotoBlobsAsync(photo, cancellationToken);
    }

    public async Task SetCoverAsync(Guid albumId, Guid photoId, CancellationToken cancellationToken)
    {
        var album = await db.PhotoAlbums.SingleOrDefaultAsync(a => a.Id == albumId, cancellationToken) ?? throw NotFound();
        if (!await db.Photos.AnyAsync(p => p.Id == photoId && p.AlbumId == albumId, cancellationToken))
        {
            throw NotFound();
        }

        album.CoverPhotoId = photoId;
        await db.SaveChangesAsync(cancellationToken);
    }

    // ----- Hulpfuncties -------------------------------------------------------------------------------------------

    private async Task ApplyAsync(Event e, EventInput input, CancellationToken cancellationToken)
    {
        if (!await db.EventCategories.AnyAsync(c => c.Id == input.CategoryId, cancellationToken))
        {
            throw new DomainException(ErrorCodes.CategoryNotFound, "Onbekende categorie.");
        }

        if (input.EndAt is { } end && end < input.StartAt)
        {
            throw new DomainException(ErrorCodes.Validation, "De eindtijd ligt vóór de begintijd.");
        }

        (e.CategoryId, e.Title, e.Summary, e.Description, e.StartAt, e.EndAt, e.AllDay) =
            (input.CategoryId, input.Title, input.Summary, input.Description, input.StartAt, input.EndAt, input.AllDay);
        (e.LocationName, e.LocationAddress, e.Latitude, e.Longitude, e.IsHighlight, e.BadgeText) =
            (input.LocationName, input.LocationAddress, input.Latitude, input.Longitude, input.IsHighlight, input.BadgeText);
        (e.Visibility, e.Status, e.PublishAt) = Publication(input.Publication);
        e.Audiences.Clear();
        e.Audiences.AddRange(Audiences(input.Publication).Select(r => new EventAudience { EventId = e.Id, AudienceType = AudienceType.Role, AudienceRef = r }));
    }

    private void Apply(NewsItem n, NewsInput input)
    {
        (n.Title, n.Summary, n.Body, n.Category, n.ExpireAt) = (input.Title, input.Summary, input.Body, input.Category, input.ExpireAt);
        (n.Visibility, n.Status, n.PublishAt) = Publication(input.Publication);
        n.Audiences.Clear();
        n.Audiences.AddRange(Audiences(input.Publication).Select(r => new NewsAudience { NewsId = n.Id, AudienceType = AudienceType.Role, AudienceRef = r }));
    }

    private void Apply(PhotoAlbum a, AlbumInput input)
    {
        (a.Title, a.AlbumDate, a.Description, a.EventId) = (input.Title, input.AlbumDate, input.Description, input.EventId);
        (a.Visibility, a.Status, a.PublishAt) = Publication(input.Publication);
        a.Audiences.Clear();
        a.Audiences.AddRange(Audiences(input.Publication).Select(r => new PhotoAlbumAudience { AlbumId = a.Id, AudienceType = AudienceType.Role, AudienceRef = r }));
    }

    private (ContentVisibility, PublicationStatus, DateTime?) Publication(PublicationInput p)
    {
        if (p.Visibility == ContentVisibility.Restricted && p.AudienceRoles.Count == 0)
        {
            throw new DomainException(ErrorCodes.Validation, "Kies bij 'Beperkt' ten minste één rol als doelgroep.");
        }

        var now = clock.UtcNow.UtcDateTime;
        return p.Status switch
        {
            PublicationStatus.Scheduled when p.PublishAt is null =>
                throw new DomainException(ErrorCodes.Validation, "Kies een publicatiemoment."),
            PublicationStatus.Published => (p.Visibility, p.Status, p.PublishAt is { } at && at <= now ? at : now),
            _ => (p.Visibility, p.Status, p.PublishAt),
        };
    }

    private static IEnumerable<string> Audiences(PublicationInput p) =>
        p.Visibility == ContentVisibility.Restricted ? p.AudienceRoles.Distinct() : [];

    private async Task SaveWithAuditAsync(string action, string entityType, Guid id, object? values, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEntry(action, entityType, id.ToString(), null, values is null ? null : JsonSerializer.Serialize(values, Json)), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private async Task DeletePhotoBlobsAsync(Photo photo, CancellationToken cancellationToken)
    {
        var originalContainer = photo.ProcessingStatus == PhotoProcessingStatus.Pending ? FileContainers.Quarantine : FileContainers.PhotosOriginal;
        await DeleteBlobsAsync(originalContainer, [photo.OriginalBlobPath], cancellationToken);
        await DeleteBlobsAsync(FileContainers.PhotosDerived, [photo.DisplayBlobPath, photo.ThumbnailBlobPath], cancellationToken);
    }

    private async Task DeleteBlobsAsync(string container, IEnumerable<string?> paths, CancellationToken cancellationToken)
    {
        foreach (var path in paths.OfType<string>())
        {
            await files.DeleteAsync(container, path, cancellationToken);
        }
    }

    private async Task<int> ActiveYearIdAsync(CancellationToken cancellationToken) =>
        await ActiveYearIdOrNullAsync(cancellationToken)
        ?? throw new DomainException(ErrorCodes.NoActiveCarnivalYear, "Er is geen actief carnavalsjaar.");

    private Task<int?> ActiveYearIdOrNullAsync(CancellationToken cancellationToken) =>
        db.CarnivalYears.Where(y => y.Active).Select(y => (int?)y.Id).SingleOrDefaultAsync(cancellationToken);

    private static string SafeFileName(string fileName, FileKind kind)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var safe = new string([.. name.Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').Take(100)]).Trim();
        return $"{(safe.Length == 0 ? "bijlage" : safe)}{FileTypeInspector.Extension(kind)}";
    }

    private static DomainException NotFound() => new(ErrorCodes.ContentNotFound, "Niet gevonden.", DomainErrorKind.NotFound);
}
