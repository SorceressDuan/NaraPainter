using System.Buffers.Binary;
using System.Security.Cryptography;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using Xunit;

namespace NaraPainter.Tests;

public class TiffExportTests
{
    private static readonly ImageCodec Codec = new();

    [Theory]
    [InlineData(TiffCompression.None)]
    [InlineData(TiffCompression.Lzw)]
    [InlineData(TiffCompression.Deflate)]
    [InlineData(TiffCompression.PackBits)]
    public void TiffRoundTripKeepsEveryPixelForEveryScheme(TiffCompression compression)
    {
        PixelBuffer original = Patterned(24, 12);
        string path = Scratch($"export-{compression}.tif");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: compression));

            AssertSamePixels(original, Codec.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(TiffCompression.None, 1)]
    [InlineData(TiffCompression.Lzw, 5)]
    [InlineData(TiffCompression.Deflate, 8)]
    [InlineData(TiffCompression.PackBits, 32773)]
    public void TiffCompressionLandsInTheCompressionTag(TiffCompression compression, int libtiffScheme)
    {
        string path = Scratch($"export-tag-{compression}.tif");

        try
        {
            Codec.Write(Patterned(24, 12), path, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: compression));

            Assert.Equal(libtiffScheme, ShortTag(File.ReadAllBytes(path), 259));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASaveWithoutCompressionStillWritesLzw()
    {
        PixelBuffer pixels = Patterned(24, 12);
        string bare = Scratch("export-bare.tif");
        string lzw = Scratch("export-explicit-lzw.tif");
        string none = Scratch("export-explicit-none.tif");

        try
        {
            Codec.Write(pixels, bare, new ImageSaveOptions(ImageFileFormat.Tiff));
            Codec.Write(pixels, lzw, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: TiffCompression.Lzw));
            Codec.Write(pixels, none, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: TiffCompression.None));

            Assert.Equal(5, ShortTag(File.ReadAllBytes(bare), 259));
            Assert.Equal(Hash(File.ReadAllBytes(bare)), Hash(File.ReadAllBytes(lzw)));
            Assert.True(
                new FileInfo(none).Length > new FileInfo(bare).Length,
                "uncompressed TIFF should be larger than the LZW one, otherwise the tag is not being honoured");
        }
        finally
        {
            File.Delete(bare);
            File.Delete(lzw);
            File.Delete(none);
        }
    }

    [Fact]
    public void AnExportedPngAssetSurvivesEveryTiffScheme()
    {
        string source = Path.Combine(AppContext.BaseDirectory, "testimages", "shapes.png");
        Assert.True(File.Exists(source), $"the test asset is missing from {source}");

        PixelBuffer original = Codec.Read(source);
        foreach (TiffCompression compression in Enum.GetValues<TiffCompression>())
        {
            string path = Scratch($"export-asset-{compression}.tif");

            try
            {
                Codec.Write(original, path, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: compression));

                AssertSamePixels(original, Codec.Read(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void PngAndBmpStillRoundTripExactlyWithAlpha()
    {
        PixelBuffer original = Patterned(32, 16);

        AssertSamePixels(original, RoundTrip(original, ImageFileFormat.Png, "png"));
        AssertSamePixels(original, RoundTrip(original, ImageFileFormat.Bmp, "bmp"));
    }

    [Fact]
    public void JpegAndWebPStayWithinTheirLossyTolerance()
    {
        PixelBuffer original = Gradient(64, 48);

        PixelBuffer jpeg = RoundTrip(original, ImageFileFormat.Jpeg, "jpg");
        Assert.Equal(original.Width, jpeg.Width);
        Assert.Equal(255, jpeg[0, 0].A);
        Assert.True(MeanColorError(original, jpeg) < 8, "JPEG quality 90 drifted further than before");

        PixelBuffer webP = RoundTrip(original, ImageFileFormat.WebP, "webp");
        Assert.Equal(original.Width, webP.Width);
        Assert.True(MeanColorError(original, webP) < 16, "WebP quality 90 drifted further than before");
    }

    [Fact]
    public void CapabilitiesMatchWhatTheEncodersDo()
    {
        PixelBuffer pixels = Patterned(24, 12);

        foreach (ImageFormatCapabilities format in Codec.WriteFormats)
        {
            string low = Scratch($"export-{format.Extension}-q20.{format.Extension}");
            string high = Scratch($"export-{format.Extension}-q95.{format.Extension}");

            try
            {
                Codec.Write(pixels, low, new ImageSaveOptions(format.Format, Quality: 20));
                Codec.Write(pixels, high, new ImageSaveOptions(format.Format, Quality: 95));

                bool qualityChangesTheFile = Hash(File.ReadAllBytes(low)) != Hash(File.ReadAllBytes(high));
                Assert.Equal(format.SupportsQuality, qualityChangesTheFile);
                Assert.Equal(format.SupportsQuality, format.DefaultQuality > 0);
                Assert.Equal(format.Format, Codec.FormatFromPath("photo." + format.Extension));
                Assert.Contains(format.Extension, Codec.SupportedWriteExtensions);
                Assert.Equal(format.Format == ImageFileFormat.Tiff, format.SupportsCompression);

                PixelBuffer decoded = Codec.Read(high);
                Assert.Equal(format.SupportsAlpha, AlphaChannelMatches(pixels, decoded));
            }
            finally
            {
                File.Delete(low);
                File.Delete(high);
            }
        }
    }

    [Fact]
    public void CompressionIsIgnoredWhereTheFormatHasNoScheme()
    {
        PixelBuffer pixels = Patterned(24, 12);
        ImageFormatCapabilities[] withoutSchemes = Codec.WriteFormats.Where(f => !f.SupportsCompression).ToArray();
        Assert.Equal(4, withoutSchemes.Length);

        foreach (ImageFormatCapabilities format in withoutSchemes)
        {
            string none = Scratch($"export-{format.Extension}-ignored-none.{format.Extension}");
            string deflate = Scratch($"export-{format.Extension}-ignored-deflate.{format.Extension}");

            try
            {
                Codec.Write(pixels, none, new ImageSaveOptions(format.Format, Compression: TiffCompression.None));
                Codec.Write(pixels, deflate, new ImageSaveOptions(format.Format, Compression: TiffCompression.Deflate));

                Assert.Equal(Hash(File.ReadAllBytes(none)), Hash(File.ReadAllBytes(deflate)));
            }
            finally
            {
                File.Delete(none);
                File.Delete(deflate);
            }
        }
    }

    [Fact]
    public void AnUnknownSchemeOrFormatIsRejected()
    {
        string path = Scratch("export-unknown.tif");

        try
        {
            Assert.Throws<NotSupportedException>(() => Codec.Write(
                Patterned(4, 4), path, new ImageSaveOptions(ImageFileFormat.Tiff, Compression: (TiffCompression)7)));
            Assert.Throws<NotSupportedException>(() => Codec.Capabilities((ImageFileFormat)99));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static PixelBuffer RoundTrip(PixelBuffer original, ImageFileFormat format, string extension)
    {
        string path = Scratch($"export-regression-{extension}.{extension}");

        try
        {
            Codec.Write(original, path, new ImageSaveOptions(format, Quality: 90));
            return Codec.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool AlphaChannelMatches(PixelBuffer expected, PixelBuffer actual)
    {
        if (expected.Width != actual.Width || expected.Height != actual.Height) return false;

        for (int i = 3; i < expected.Data.Length; i += 4)
        {
            if (expected.Data[i] != actual.Data[i]) return false;
        }

        return true;
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

    private static int ShortTag(byte[] bytes, int wanted)
    {
        bool little = bytes[0] == 0x49;
        int offset = (int)(little
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))
            : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)));
        int entries = little
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset))
            : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset));

        for (int e = 0; e < entries; e++)
        {
            int entry = offset + 2 + (e * 12);
            int tag = little
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry))
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry));
            if (tag != wanted) continue;

            int type = little
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 2))
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry + 2));
            Assert.Equal(3, type);

            return little
                ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(entry + 8))
                : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(entry + 8));
        }

        Assert.Fail($"the TIFF carries no tag {wanted}");
        return -1;
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

    private static PixelBuffer Gradient(int width, int height)
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
                buffer.Data[i + 3] = 255;
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

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static string Scratch(string name)
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "export-scratch");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, name);
    }
}
