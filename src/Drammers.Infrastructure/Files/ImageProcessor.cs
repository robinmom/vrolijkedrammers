using System.Globalization;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace Drammers.Infrastructure.Files;

/// <summary>Afmetingen van de weergaveversie en de opnamedatum uit EXIF (vóór het strippen).</summary>
public sealed record ProcessedImage(int Width, int Height, DateTime? TakenAt);

/// <summary>
/// Maakt de derivaten van een foto (fase 5): 1600 px en een thumbnail van 400 px, als JPEG, gedraaid volgens EXIF en
/// zonder metadata (EXIF, GPS, IPTC, XMP). Het origineel blijft privé bewaard.
/// </summary>
public static class ImageProcessor
{
    public const int DisplaySize = 1600;
    public const int ThumbnailSize = 400;

    private static readonly JpegEncoder Encoder = new() { Quality = 85 };

    public static async Task<ProcessedImage> CreateDerivativesAsync(Stream original, Stream display, Stream thumbnail, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(original, cancellationToken);
        var takenAt = ReadTakenAt(image);
        image.Mutate(x => x.AutoOrient());
        StripMetadata(image);

        var size = await SaveResizedAsync(image, DisplaySize, display, cancellationToken);
        await SaveResizedAsync(image, ThumbnailSize, thumbnail, cancellationToken);
        return new ProcessedImage(size.Width, size.Height, takenAt);
    }

    /// <summary>Alleen herschalen en metadata strippen (bijv. voor event- en nieuwsafbeeldingen).</summary>
    public static async Task<ProcessedImage> SanitizeAsync(Stream original, Stream output, int maxSize, CancellationToken cancellationToken)
    {
        using var image = await Image.LoadAsync(original, cancellationToken);
        image.Mutate(x => x.AutoOrient());
        StripMetadata(image);
        var size = await SaveResizedAsync(image, maxSize, output, cancellationToken);
        return new ProcessedImage(size.Width, size.Height, null);
    }

    /// <returns>De afmetingen van het opgeslagen (verkleinde) beeld.</returns>
    private static async Task<Size> SaveResizedAsync(Image image, int maxSize, Stream output, CancellationToken cancellationToken)
    {
        using var copy = image.Clone(x =>
        {
            if (image.Width > maxSize || image.Height > maxSize)
            {
                x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(maxSize, maxSize) });
            }
        });
        await copy.SaveAsJpegAsync(output, Encoder, cancellationToken);
        output.Position = 0;
        return copy.Size;
    }

    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.XmpProfile = null;
        }
    }

    private static DateTime? ReadTakenAt(Image image)
    {
        if (image.Metadata.ExifProfile?.TryGetValue(ExifTag.DateTimeOriginal, out var value) == true
            && DateTime.TryParseExact(value.Value, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var taken))
        {
            return DateTime.SpecifyKind(taken, DateTimeKind.Unspecified).ToUniversalTime();
        }

        return null;
    }
}
