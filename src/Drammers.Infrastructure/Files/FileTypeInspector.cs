namespace Drammers.Infrastructure.Files;

public enum FileKind
{
    Unknown,
    Jpeg,
    Png,
    WebP,
    Pdf,
}

/// <summary>
/// Bepaalt het bestandstype aan de inhoud (magic bytes), niet aan de naam of de opgegeven content-type (ADR-008).
/// SVG, HTML en uitvoerbare bestanden worden daardoor nooit als afbeelding geaccepteerd.
/// </summary>
public static class FileTypeInspector
{
    public static readonly FileKind[] Images = [FileKind.Jpeg, FileKind.Png, FileKind.WebP];
    public static readonly FileKind[] Attachments = [FileKind.Pdf, FileKind.Jpeg, FileKind.Png];

    public static async Task<FileKind> DetectAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[12];
        var read = 0;
        while (read < header.Length)
        {
            var n = await stream.ReadAsync(header.AsMemory(read), cancellationToken);
            if (n == 0)
            {
                break;
            }

            read += n;
        }

        stream.Position = 0;
        return Detect(header.AsSpan(0, read));
    }

    public static FileKind Detect(ReadOnlySpan<byte> header) => header switch
    {
        [0xFF, 0xD8, 0xFF, ..] => FileKind.Jpeg,
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => FileKind.Png,
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => FileKind.WebP,
        [0x25, 0x50, 0x44, 0x46, ..] => FileKind.Pdf,
        _ => FileKind.Unknown,
    };

    public static string ContentType(FileKind kind) => kind switch
    {
        FileKind.Jpeg => "image/jpeg",
        FileKind.Png => "image/png",
        FileKind.WebP => "image/webp",
        FileKind.Pdf => "application/pdf",
        _ => "application/octet-stream",
    };

    public static string Extension(FileKind kind) => kind switch
    {
        FileKind.Jpeg => ".jpg",
        FileKind.Png => ".png",
        FileKind.WebP => ".webp",
        FileKind.Pdf => ".pdf",
        _ => ".bin",
    };
}
