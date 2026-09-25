using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Drammers.IntegrationTests.Infrastructure;

public static class TestImages
{
    /// <summary>JPEG van 3000×2000 met EXIF-GPS-coördinaten (Loil) en opnamedatum.</summary>
    public static byte[] JpegWithGps()
    {
        using var image = new Image<Rgba32>(3000, 2000, new Rgba32(237, 0, 18));
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        exif.SetValue(ExifTag.GPSLatitude, [new Rational(51, 1), new Rational(56, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.GPSLongitudeRef, "E");
        exif.SetValue(ExifTag.GPSLongitude, [new Rational(6, 1), new Rational(8, 1), new Rational(0, 1)]);
        exif.SetValue(ExifTag.DateTimeOriginal, "2027:02:06 14:11:11");
        image.Metadata.ExifProfile = exif;
        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return stream.ToArray();
    }

    /// <summary>Een "uitvoerbaar bestand" (MZ-header) dat zich voordoet als foto.</summary>
    public static byte[] ExecutableDisguisedAsJpeg() => [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF];
}
