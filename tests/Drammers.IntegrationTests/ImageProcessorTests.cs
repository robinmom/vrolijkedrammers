using Drammers.Infrastructure.Files;
using Drammers.IntegrationTests.Infrastructure;
using SkiaSharp;

namespace Drammers.IntegrationTests;

/// <summary>Beeldverwerking met SkiaSharp: draaien volgens EXIF, verkleinen, metadata weg en de opnamedatum lezen.</summary>
public class ImageProcessorTests
{
    private static async Task<(SKBitmap Display, ProcessedImage Result, byte[] Bytes)> ProcessAsync(byte[] jpeg)
    {
        using var display = new MemoryStream();
        using var thumbnail = new MemoryStream();
        var result = await ImageProcessor.CreateDerivativesAsync(new MemoryStream(jpeg), display, thumbnail, CancellationToken.None);
        var bytes = display.ToArray();
        return (SKBitmap.Decode(bytes), result, bytes);
    }

    [Fact]
    public async Task Leest_de_opnamedatum_en_haalt_alle_metadata_weg()
    {
        var original = TestImages.JpegWithGps();
        Assert.True(TestImages.HasExif(original));
        var (display, result, bytes) = await ProcessAsync(original);
        using (display)
        {
            Assert.Equal((1600, 1067), (result.Width, result.Height));
            Assert.Equal(new DateTime(2027, 2, 6, 14, 11, 11), result.TakenAt!.Value.ToLocalTime());
            Assert.False(TestImages.HasExif(bytes));
        }
    }

    [Theory]
    [InlineData((ushort)1, 300, 200)]
    [InlineData((ushort)6, 200, 300)]
    [InlineData((ushort)8, 200, 300)]
    [InlineData((ushort)3, 300, 200)]
    public async Task Draait_volgens_de_EXIF_orientatie(ushort orientation, int width, int height)
    {
        var (display, result, _) = await ProcessAsync(TestImages.JpegWithOrientation(orientation));
        using (display)
        {
            Assert.Equal((width, height), (result.Width, result.Height));

            // Links rood, rechts blauw in het origineel: na 90° met de klok mee (6) staat rood boven, tegen de klok in (8)
            // onder, en na 180° (3) rechts.
            bool IsRed(int x, int y) => display.GetPixel(x, y).Red > 200 && display.GetPixel(x, y).Blue < 80;
            var expectRedAt = orientation switch { 6 => (width / 2, 20), 8 => (width / 2, height - 20), 3 => (width - 20, height / 2), _ => (20, height / 2) };
            Assert.True(IsRed(expectRedAt.Item1, expectRedAt.Item2), $"Rood verwacht op {expectRedAt} bij oriëntatie {orientation}");
        }
    }

    [Fact]
    public async Task Transparantie_komt_op_wit()
    {
        using var bitmap = new SKBitmap(new SKImageInfo(10, 10, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        bitmap.Erase(SKColors.Transparent);
        using var png = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        using var output = new MemoryStream();
        await ImageProcessor.SanitizeAsync(new MemoryStream(png.ToArray()), output, 1600, CancellationToken.None);
        using var result = SKBitmap.Decode(output.ToArray());
        Assert.True(result.GetPixel(5, 5).Red > 240 && result.GetPixel(5, 5).Green > 240);
    }
}
