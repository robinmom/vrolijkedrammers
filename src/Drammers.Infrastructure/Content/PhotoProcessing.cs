using System.Text.Json;
using Drammers.Infrastructure.Files;
using Drammers.Infrastructure.Persistence;
using Drammers.Modules.Content.Photos;
using Drammers.SharedKernel.Auditing;
using Drammers.Worker.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Drammers.Infrastructure.Content;

/// <summary>
/// Verwerkt een geüploade foto (worker, via de outbox): malwarescan → origineel naar <c>photos-original</c> →
/// derivaten (1600 px en thumbnail, zonder EXIF/GPS) naar <c>photos-derived</c>. Idempotent: een foto die al klaar of
/// geweigerd is, wordt overgeslagen.
/// </summary>
public sealed class PhotoProcessingHandler(DrammersDbContext db, IFileStore files, IMalwareScanner scanner, IAuditLogger audit) : IOutboxMessageHandler
{
    public const string MessageType = "media.process-photo";

    public string Type => MessageType;

    public async Task HandleAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        var photoId = JsonSerializer.Deserialize<PhotoMessage>(message.Payload, JsonSerializerOptions.Web)!.PhotoId;
        var photo = await db.Photos.SingleOrDefaultAsync(p => p.Id == photoId, cancellationToken);
        if (photo is null || photo.ProcessingStatus != PhotoProcessingStatus.Pending)
        {
            return;
        }

        var quarantinePath = photo.OriginalBlobPath;
        if (await scanner.ScanAsync(FileContainers.Quarantine, quarantinePath, cancellationToken) != ScanResult.Clean)
        {
            await files.DeleteAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);
            photo.ProcessingStatus = PhotoProcessingStatus.Rejected;
            await db.SaveChangesAsync(cancellationToken);
            await audit.WriteAsync(new AuditEntry("file.rejected-malware", "Photo", photo.Id.ToString()), cancellationToken);
            return;
        }

        var baseName = $"{photo.AlbumId:N}/{photo.Id:N}";
        var originalPath = $"{baseName}{Path.GetExtension(quarantinePath)}";
        await files.CopyAsync(FileContainers.Quarantine, quarantinePath, FileContainers.PhotosOriginal, originalPath, cancellationToken);

        await using var original = await files.OpenReadAsync(FileContainers.PhotosOriginal, originalPath, cancellationToken);
        await using var display = new MemoryStream();
        await using var thumbnail = new MemoryStream();
        var processed = await ImageProcessor.CreateDerivativesAsync(original, display, thumbnail, cancellationToken);
        await files.UploadAsync(FileContainers.PhotosDerived, $"{baseName}-1600.jpg", display, "image/jpeg", cancellationToken);
        await files.UploadAsync(FileContainers.PhotosDerived, $"{baseName}-400.jpg", thumbnail, "image/jpeg", cancellationToken);
        await files.DeleteAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);

        photo.OriginalBlobPath = originalPath;
        photo.DisplayBlobPath = $"{baseName}-1600.jpg";
        photo.ThumbnailBlobPath = $"{baseName}-400.jpg";
        photo.Width = processed.Width;
        photo.Height = processed.Height;
        photo.TakenAt = processed.TakenAt;
        photo.ProcessingStatus = PhotoProcessingStatus.Ready;
        await db.SaveChangesAsync(cancellationToken);
    }

    public sealed record PhotoMessage(Guid PhotoId);
}
