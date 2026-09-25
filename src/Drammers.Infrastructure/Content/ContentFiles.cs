using Drammers.Infrastructure.Files;
using Drammers.SharedKernel.Auditing;
using Drammers.SharedKernel.Errors;
using Drammers.SharedKernel.Identifiers;

namespace Drammers.Infrastructure.Content;

public sealed record StoredFile(string Path, string ContentType, long SizeBytes, FileKind Kind);

/// <summary>
/// Upload-pijplijn voor event- en nieuwsbestanden (ADR-008): typecontrole op inhoud → quarantaine → malwarescan →
/// promoten naar <c>content</c>. Afbeeldingen worden daarbij herschaald en zonder metadata opgeslagen.
/// </summary>
public sealed class ContentFiles(IFileStore files, IMalwareScanner scanner, IAuditLogger audit)
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxAttachmentBytes = 10 * 1024 * 1024;
    public const long MaxPhotoBytes = 25 * 1024 * 1024;

    public Task<StoredFile> StoreImageAsync(Stream upload, long length, string folder, CancellationToken cancellationToken) =>
        StoreAsync(upload, length, folder, FileTypeInspector.Images, MaxImageBytes, sanitizeImage: true, cancellationToken);

    public Task<StoredFile> StoreAttachmentAsync(Stream upload, long length, string folder, CancellationToken cancellationToken) =>
        StoreAsync(upload, length, folder, FileTypeInspector.Attachments, MaxAttachmentBytes, sanitizeImage: false, cancellationToken);

    /// <summary>Controleert type en grootte en zet het bestand in quarantaine; geeft het quarantainepad terug.</summary>
    public async Task<(string QuarantinePath, FileKind Kind)> QuarantineAsync(
        Stream upload, long length, FileKind[] allowed, long maxBytes, CancellationToken cancellationToken)
    {
        if (length <= 0 || length > maxBytes)
        {
            throw new DomainException(ErrorCodes.FileTooLarge, $"Het bestand is leeg of groter dan {maxBytes / (1024 * 1024)} MB.");
        }

        await using var buffer = new MemoryStream();
        await upload.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        var kind = await FileTypeInspector.DetectAsync(buffer, cancellationToken);
        if (!allowed.Contains(kind))
        {
            throw new DomainException(ErrorCodes.FileTypeNotAllowed,
                $"Dit bestandstype is niet toegestaan. Toegestaan: {string.Join(", ", allowed.Select(k => k.ToString().ToUpperInvariant()))}.");
        }

        var path = $"{IdGenerator.NewId():N}{FileTypeInspector.Extension(kind)}";
        await files.UploadAsync(FileContainers.Quarantine, path, buffer, FileTypeInspector.ContentType(kind), cancellationToken);
        return (path, kind);
    }

    /// <summary>Scant een quarantainebestand; bij malware wordt het verwijderd, geaudit en geweigerd.</summary>
    public async Task EnsureCleanAsync(string quarantinePath, CancellationToken cancellationToken)
    {
        if (await scanner.ScanAsync(FileContainers.Quarantine, quarantinePath, cancellationToken) == ScanResult.Clean)
        {
            return;
        }

        await files.DeleteAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);
        await audit.WriteAsync(new AuditEntry("file.rejected-malware", "File", quarantinePath), cancellationToken);
        throw new DomainException(ErrorCodes.FileInfected, "Het bestand is geweigerd door de virusscan.");
    }

    private async Task<StoredFile> StoreAsync(
        Stream upload, long length, string folder, FileKind[] allowed, long maxBytes, bool sanitizeImage, CancellationToken cancellationToken)
    {
        var (quarantinePath, kind) = await QuarantineAsync(upload, length, allowed, maxBytes, cancellationToken);
        await EnsureCleanAsync(quarantinePath, cancellationToken);

        var isImage = sanitizeImage || FileTypeInspector.Images.Contains(kind);
        var targetKind = isImage ? FileKind.Jpeg : kind;
        var target = $"{folder}/{IdGenerator.NewId():N}{FileTypeInspector.Extension(targetKind)}";
        long size;
        if (isImage)
        {
            await using var original = await files.OpenReadAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);
            await using var output = new MemoryStream();
            await ImageProcessor.SanitizeAsync(original, output, ImageProcessor.DisplaySize, cancellationToken);
            size = output.Length;
            await files.UploadAsync(FileContainers.Content, target, output, FileTypeInspector.ContentType(targetKind), cancellationToken);
        }
        else
        {
            await files.CopyAsync(FileContainers.Quarantine, quarantinePath, FileContainers.Content, target, cancellationToken);
            size = length;
        }

        await files.DeleteAsync(FileContainers.Quarantine, quarantinePath, cancellationToken);
        return new StoredFile(target, FileTypeInspector.ContentType(targetKind), size, targetKind);
    }
}
