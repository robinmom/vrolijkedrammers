using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Drammers.Infrastructure.Files;

/// <summary>Afmetingen van de weergaveversie en de opnamedatum uit EXIF (vóór het strippen).</summary>
public sealed record ProcessedImage(int Width, int Height, DateTime? TakenAt);

/// <summary>
/// Maakt de derivaten van een foto (fase 5): 1600 px en een thumbnail van 400 px, als JPEG, gedraaid volgens EXIF en
/// zonder metadata (EXIF, GPS, IPTC, XMP). Het origineel blijft privé bewaard. Met SkiaSharp: de JPEG-encoder schrijft
/// geen metadata, dus alles verdwijnt vanzelf. Transparantie komt op wit.
/// </summary>
public static class ImageProcessor
{
    public const int DisplaySize = 1600;
    public const int ThumbnailSize = 400;

    private const int Quality = 85;

    private static readonly SKSamplingOptions Sampling = new(SKCubicResampler.Mitchell);

    public static async Task<ProcessedImage> CreateDerivativesAsync(Stream original, Stream display, Stream thumbnail, CancellationToken cancellationToken)
    {
        var bytes = await ReadAllAsync(original, cancellationToken);
        using var image = DecodeOriented(bytes);
        var size = await SaveResizedAsync(image, DisplaySize, display, cancellationToken);
        await SaveResizedAsync(image, ThumbnailSize, thumbnail, cancellationToken);
        return new ProcessedImage(size.Width, size.Height, ExifDate.TakenAt(bytes));
    }

    /// <summary>Alleen herschalen en metadata strippen (bijv. voor event- en nieuwsafbeeldingen).</summary>
    public static async Task<ProcessedImage> SanitizeAsync(Stream original, Stream output, int maxSize, CancellationToken cancellationToken)
    {
        var bytes = await ReadAllAsync(original, cancellationToken);
        using var image = DecodeOriented(bytes);
        var size = await SaveResizedAsync(image, maxSize, output, cancellationToken);
        return new ProcessedImage(size.Width, size.Height, null);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy, cancellationToken);
        return copy.ToArray();
    }

    /// <summary>Decodeert en draait volgens de EXIF-oriëntatie (zoals het toestel de foto bedoelde).</summary>
    private static SKBitmap DecodeOriented(byte[] bytes)
    {
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw new InvalidDataException("Geen geldige afbeelding.");
        var decoded = SKBitmap.Decode(codec) ?? throw new InvalidDataException("De afbeelding kan niet worden gelezen.");
        var origin = codec.EncodedOrigin;
        if (origin == SKEncodedOrigin.TopLeft)
        {
            return decoded;
        }

        using (decoded)
        {
            var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var (w, h) = swap ? (decoded.Height, decoded.Width) : (decoded.Width, decoded.Height);
            var rotated = new SKBitmap(new SKImageInfo(w, h, decoded.ColorType, decoded.AlphaType));
            using var canvas = new SKCanvas(rotated);
            canvas.SetMatrix(OriginMatrix(origin, decoded.Width, decoded.Height));
            using var source = SKImage.FromBitmap(decoded);
            canvas.DrawImage(source, 0, 0, Sampling);
            return rotated;
        }
    }

    /// <summary>Transformatie van de opgeslagen pixels naar de bedoelde stand (EXIF-oriëntatie 2–8).</summary>
    private static SKMatrix OriginMatrix(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity,
    };

    /// <returns>De afmetingen van het opgeslagen (verkleinde) beeld.</returns>
    private static async Task<SKSizeI> SaveResizedAsync(SKBitmap image, int maxSize, Stream output, CancellationToken cancellationToken)
    {
        var scale = Math.Min(1d, Math.Min((double)maxSize / image.Width, (double)maxSize / image.Height));
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));

        // Op een witte, ondoorzichtige achtergrond: JPEG kent geen transparantie.
        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        surface.Canvas.Clear(SKColors.White);
        using (var source = SKImage.FromBitmap(image))
        {
            surface.Canvas.DrawImage(source, new SKRect(0, 0, width, height), Sampling);
        }

        using var snapshot = surface.Snapshot();
        using var jpeg = snapshot.Encode(SKEncodedImageFormat.Jpeg, Quality);
        await output.WriteAsync(jpeg.ToArray(), cancellationToken);
        output.Position = 0;
        return new SKSizeI(width, height);
    }
}

/// <summary>Leest alleen de opnamedatum (DateTimeOriginal) uit het EXIF-blok van een JPEG; verder niets.</summary>
internal static class ExifDate
{
    private const ushort ExifIfdPointer = 0x8769;
    private const ushort DateTimeOriginal = 0x9003;

    public static DateTime? TakenAt(ReadOnlySpan<byte> jpeg)
    {
        try
        {
            return Find(jpeg) is { } text
                && DateTime.TryParseExact(text, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var taken)
                ? DateTime.SpecifyKind(taken, DateTimeKind.Unspecified).ToUniversalTime()
                : null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null; // kapot EXIF-blok: geen datum
        }
    }

    private static string? Find(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
        {
            return null;
        }

        var position = 2;
        while (position + 4 <= jpeg.Length && jpeg[position] == 0xFF)
        {
            var marker = jpeg[position + 1];
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg[(position + 2)..]);
            if (marker == 0xE1 && length > 8 && jpeg.Slice(position + 4, 6).SequenceEqual("Exif\0\0"u8))
            {
                return FromTiff(jpeg.Slice(position + 10, length - 8));
            }

            if (marker == 0xDA)
            {
                return null; // begin van de beelddata: geen EXIF
            }

            position += 2 + length;
        }

        return null;
    }

    private static string? FromTiff(ReadOnlySpan<byte> tiff)
    {
        var little = tiff[0] == (byte)'I';
        if (Entry(tiff, little, (int)U32(tiff, little, 4), ExifIfdPointer) is not { } exif
            || Entry(tiff, little, (int)exif.Value, DateTimeOriginal) is not { } date)
        {
            return null;
        }

        return Encoding.ASCII.GetString(tiff.Slice((int)date.Value, (int)Math.Min(date.Count, 19)));
    }

    private static (uint Count, uint Value)? Entry(ReadOnlySpan<byte> tiff, bool little, int ifd, ushort tag)
    {
        var count = U16(tiff, little, ifd);
        for (var i = 0; i < count; i++)
        {
            var at = ifd + 2 + (i * 12);
            if (U16(tiff, little, at) == tag)
            {
                return (U32(tiff, little, at + 4), U32(tiff, little, at + 8));
            }
        }

        return null;
    }

    private static ushort U16(ReadOnlySpan<byte> tiff, bool little, int at) =>
        little ? BinaryPrimitives.ReadUInt16LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt16BigEndian(tiff[at..]);

    private static uint U32(ReadOnlySpan<byte> tiff, bool little, int at) =>
        little ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[at..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[at..]);
}
