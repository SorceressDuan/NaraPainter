using System.Diagnostics;
using NaraDreamPainter.Imaging.Services;
using NaraDreamPainter.Models.Pixels;
using NaraDreamPainter.Models.Services;
using Xunit;

namespace NaraDreamPainter.Tests;

public class ImageCodecTests
{
    private static readonly ImageCodec Codec = new();

    [Fact]
    public void PngRoundTripKeepsEveryPixel()
    {
        PixelBuffer original = Patterned(32, 16);
        string path = Scratch("roundtrip.png");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Png));
            PixelBuffer decoded = Codec.Read(path);

            AssertSamePixels(original, decoded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TiffRoundTripKeepsEveryPixel()
    {
        PixelBuffer original = Patterned(24, 12);
        string path = Scratch("roundtrip.tiff");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Tiff));
            PixelBuffer decoded = Codec.Read(path);

            AssertSamePixels(original, decoded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void JpegRoundTripStaysCloseAtQualityNinety()
    {
        PixelBuffer original = Gradient(64, 48, opaque: true);
        string path = Scratch("roundtrip.jpg");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Jpeg, Quality: 90));
            PixelBuffer decoded = Codec.Read(path);

            Assert.Equal(original.Width, decoded.Width);
            Assert.Equal(original.Height, decoded.Height);
            Assert.Equal(255, decoded[0, 0].A);

            double error = MeanColorError(original, decoded);
            Assert.True(error < 8, $"JPEG quality 90 came back {error:0.##} levels off on average");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WebPRoundTripIsReadable()
    {
        PixelBuffer original = Gradient(48, 32, opaque: true);
        string path = Scratch("roundtrip.webp");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.WebP, Quality: 90));
            PixelBuffer decoded = Codec.Read(path);

            Assert.Equal(original.Width, decoded.Width);
            Assert.Equal(original.Height, decoded.Height);

            double error = MeanColorError(original, decoded);
            Assert.True(error < 16, $"WebP came back {error:0.##} levels off on average");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void BmpRoundTripKeepsOpaquePixels()
    {
        PixelBuffer original = Gradient(20, 20, opaque: true);
        string path = Scratch("roundtrip.bmp");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Bmp));
            PixelBuffer decoded = Codec.Read(path);

            AssertSamePixels(original, decoded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void FormatFromPathCoversTheSupportedExtensions()
    {
        Assert.Equal(ImageFileFormat.Png, Codec.FormatFromPath("photo.PNG"));
        Assert.Equal(ImageFileFormat.Jpeg, Codec.FormatFromPath("photo.jpg"));
        Assert.Equal(ImageFileFormat.Jpeg, Codec.FormatFromPath("photo.jpeg"));
        Assert.Equal(ImageFileFormat.WebP, Codec.FormatFromPath("photo.webp"));
        Assert.Equal(ImageFileFormat.Bmp, Codec.FormatFromPath("photo.bmp"));
        Assert.Equal(ImageFileFormat.Tiff, Codec.FormatFromPath("photo.tif"));
        Assert.Equal(ImageFileFormat.Tiff, Codec.FormatFromPath("photo.tiff"));

        Assert.Contains("png", Codec.SupportedReadExtensions);
        Assert.Contains("webp", Codec.SupportedWriteExtensions);
        Assert.Throws<NotSupportedException>(() => Codec.FormatFromPath("animation.gif"));
        Assert.Throws<ArgumentException>(() => Codec.FormatFromPath("   "));
    }

    [Fact]
    public void WriteRefusesWhenThePathAndTheOptionsDisagree()
    {
        PixelBuffer pixels = Patterned(4, 4);
        string path = Scratch("mismatch.png");

        try
        {
            Assert.Throws<ArgumentException>(() => Codec.Write(pixels, path, new ImageSaveOptions(ImageFileFormat.Jpeg)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadRefusesFilesThatAreNotThere()
    {
        Assert.Throws<FileNotFoundException>(() => Codec.Read(Scratch("missing.png")));
        Assert.Throws<ArgumentException>(() => Codec.Read("  "));
    }

    [Fact]
    public void ARejectedFileDoesNotComeBackAsAnEmptyImage()
    {
        string path = Scratch("garbage.png");
        File.WriteAllBytes(path, [1, 2, 3, 4, 5, 6, 7, 8]);

        try
        {
            Assert.Throws<InvalidDataException>(() => Codec.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AFourThousandByThreeThousandPngDecodesWithinEightHundredMilliseconds()
    {
        var original = new PixelBuffer(4000, 3000);
        for (int y = 0; y < original.Height; y++)
        {
            for (int x = 0; x < original.Width; x++)
            {
                int i = original.Offset(x, y);
                original.Data[i] = (byte)(x % 256);
                original.Data[i + 1] = (byte)(y % 256);
                original.Data[i + 2] = (byte)(((x + y) / 2) % 256);
                original.Data[i + 3] = 255;
            }
        }

        string small = Scratch("warmup.png");
        string path = Scratch("large.png");

        try
        {
            Codec.Write(TestPixels.Solid(2, 2, 10, 20, 30, 255), small, new ImageSaveOptions(ImageFileFormat.Png));
            Codec.Read(small);
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Png));

            var stopwatch = Stopwatch.StartNew();
            PixelBuffer decoded = Codec.Read(path);
            stopwatch.Stop();

            Assert.Equal(4000, decoded.Width);
            Assert.Equal(3000, decoded.Height);
            Assert.True(stopwatch.ElapsedMilliseconds < 800, $"decoding took {stopwatch.ElapsedMilliseconds} ms");
        }
        finally
        {
            File.Delete(small);
            File.Delete(path);
        }
    }

    private static void AssertSamePixels(PixelBuffer expected, PixelBuffer actual)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        for (int i = 0; i < expected.Data.Length; i++)
        {
            if (expected.Data[i] != actual.Data[i])
            {
                Assert.Fail($"byte {i} of pixel {i / 4} (channel {i % 4}) differs: {expected.Data[i]} vs {actual.Data[i]}");
            }
        }
    }

    private static PixelBuffer Patterned(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = buffer.Offset(x, y);
                buffer.Data[i] = (byte)((x * 9) % 256);
                buffer.Data[i + 1] = (byte)((y * 17) % 256);
                buffer.Data[i + 2] = (byte)((x + y) % 256);
                buffer.Data[i + 3] = (byte)((x * 31 + y * 13) % 256);
            }
        }
        return buffer;
    }

    private static PixelBuffer Gradient(int width, int height, bool opaque)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = buffer.Offset(x, y);
                buffer.Data[i] = (byte)((x * 255) / Math.Max(1, width - 1));
                buffer.Data[i + 1] = (byte)((y * 255) / Math.Max(1, height - 1));
                buffer.Data[i + 2] = (byte)(128 + ((x + y) % 64));
                buffer.Data[i + 3] = opaque ? (byte)255 : (byte)128;
            }
        }
        return buffer;
    }

    private static double MeanColorError(PixelBuffer expected, PixelBuffer actual)
    {
        long total = 0;
        long count = 0;
        for (int i = 0; i < expected.Data.Length; i += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                total += Math.Abs(expected.Data[i + channel] - actual.Data[i + channel]);
                count++;
            }
        }
        return (double)total / count;
    }

    private static string Scratch(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "codec-scratch");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
}
