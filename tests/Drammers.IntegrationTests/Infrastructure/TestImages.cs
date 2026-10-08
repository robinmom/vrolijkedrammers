using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Drammers.IntegrationTests.Infrastructure;

public static class TestImages
{
    /// <summary>JPEG van 3000×2000 met EXIF-GPS-coördinaten (Loil) en opnamedatum.</summary>
    public static byte[] JpegWithGps() => WithExif(Jpeg(3000, 2000, (_, _) => new SKColor(237, 0, 18)), orientation: null);

    /// <summary>
    /// JPEG van 300×200: links rood, rechts blauw, met EXIF-oriëntatie (6 = 90° met de klok mee gedraaid weergeven).
    /// </summary>
    public static byte[] JpegWithOrientation(ushort orientation) =>
        WithExif(Jpeg(300, 200, (x, _) => x < 150 ? SKColors.Red : SKColors.Blue), orientation);

    /// <summary>Een "uitvoerbaar bestand" (MZ-header) dat zich voordoet als foto.</summary>
    public static byte[] ExecutableDisguisedAsJpeg() => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];

    /// <summary>Of een JPEG een EXIF-blok (APP1 "Exif") heeft.</summary>
    public static bool HasExif(byte[] jpeg) => jpeg.AsSpan().IndexOf("Exif\0\0"u8) >= 0;

    private static byte[] Jpeg(int width, int height, Func<int, int, SKColor> color)
    {
        using var bitmap = new SKBitmap(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                bitmap.SetPixel(x, y, color(x, y));
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    /// <summary>Zet een EXIF-blok (little-endian TIFF) direct na de SOI-marker: oriëntatie, opnamedatum en GPS.</summary>
    private static byte[] WithExif(byte[] jpeg, ushort? orientation)
    {
        var tiff = new List<byte>();
        void U16(ushort v) => tiff.AddRange(BitConverter.GetBytes(v));
        void U32(uint v) => tiff.AddRange(BitConverter.GetBytes(v));
        void Entry(ushort tag, ushort type, uint count, uint value) { U16(tag); U16(type); U32(count); U32(value); }

        var ifd0Entries = orientation is null ? 2 : 3;
        var ifd0Size = 2 + (ifd0Entries * 12) + 4;
        var exifIfd = 8 + ifd0Size;
        var date = exifIfd + 2 + 12 + 4;
        var gpsIfd = date + 20;
        var latitude = gpsIfd + 2 + (4 * 12) + 4;
        var longitude = latitude + 24;

        tiff.AddRange("II"u8.ToArray());
        U16(42);
        U32(8);
        U16((ushort)ifd0Entries);
        if (orientation is { } o)
        {
            Entry(0x0112, 3, 1, o); // Orientation (SHORT, in de waarde)
        }

        Entry(0x8769, 4, 1, (uint)exifIfd); // Exif IFD
        Entry(0x8825, 4, 1, (uint)gpsIfd); // GPS IFD
        U32(0);
        U16(1);
        Entry(0x9003, 2, 20, (uint)date); // DateTimeOriginal
        U32(0);
        tiff.AddRange(Encoding.ASCII.GetBytes("2027:02:06 14:11:11\0"));
        U16(4);
        Entry(0x0001, 2, 2, 'N'); // GPSLatitudeRef
        Entry(0x0002, 5, 3, (uint)latitude);
        Entry(0x0003, 2, 2, 'E'); // GPSLongitudeRef
        Entry(0x0004, 5, 3, (uint)longitude);
        U32(0);
        foreach (var (n, d) in new (uint, uint)[] { (51, 1), (56, 1), (0, 1), (6, 1), (8, 1), (0, 1) })
        {
            U32(n);
            U32(d);
        }

        var app1 = new byte[4 + 6 + tiff.Count];
        app1[0] = 0xFF;
        app1[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(app1.AsSpan(2), (ushort)(2 + 6 + tiff.Count));
        "Exif\0\0"u8.CopyTo(app1.AsSpan(4));
        tiff.CopyTo(app1, 10);
        return [.. jpeg[..2], .. app1, .. jpeg[2..]];
    }
}
