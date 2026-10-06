using Compositor.Imaging.Services;
using Compositor.Models.Pixels;
using Xunit;

namespace Compositor.Tests;

public class ContentAwareFillTests
{
    private static readonly Rgba32 Surroundings = new(0, 180, 0, 255);
    private static readonly Rgba32 Removed = new(255, 0, 255, 128);
    private static readonly Rgba32 FarSide = new(200, 0, 0, 255);

    [Fact]
    public void FillReplacesTheHoleWithItsSurroundings()
    {
        (PixelBuffer image, byte[] mask) = ImageWithAHole();

        PixelBuffer filled = ContentAwareFill.Fill(image, mask);

        Rgba32 center = filled[20, 20];
        Assert.True(Distance(center, Surroundings) < Distance(center, Removed),
            $"the hole center {center} is still closer to the color that was removed");
        Assert.True(Distance(center, Surroundings) < 40,
            $"the hole center {center} is {Distance(center, Surroundings):0.#} away from the surroundings");
        Assert.Equal(FarSide, filled[50, 10]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void BothInpaintingMethodsFavorTheSurroundings(int method)
    {
        (PixelBuffer image, byte[] mask) = ImageWithAHole();

        PixelBuffer filled = ContentAwareFill.Fill(image, mask, 3, method);

        Assert.True(Distance(filled[20, 20], Surroundings) < Distance(filled[20, 20], Removed),
            $"method {method} left {filled[20, 20]} in the hole");
    }

    [Fact]
    public void FillOnlyTouchesTheMaskedPixels()
    {
        (PixelBuffer image, byte[] mask) = ImageWithAHole();

        PixelBuffer filled = ContentAwareFill.Fill(image, mask);

        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (mask[(y * image.Width) + x] != 0) continue;
                Assert.Equal(image[x, y], filled[x, y]);
            }
        }
    }

    [Fact]
    public void FillKeepsTheAlphaChannel()
    {
        var image = new PixelBuffer(32, 32);
        var mask = new byte[32 * 32];
        for (int y = 0; y < 32; y++)
        {
            for (int x = 0; x < 32; x++)
            {
                Set(image, x, y, new Rgba32(40, 90, 160, 200));
            }
        }

        for (int y = 10; y < 20; y++)
        {
            for (int x = 10; x < 20; x++)
            {
                Set(image, x, y, new Rgba32(250, 250, 250, 77));
                mask[(y * 32) + x] = 255;
            }
        }

        PixelBuffer filled = ContentAwareFill.Fill(image, mask);

        for (int i = 3; i < filled.Data.Length; i += 4)
        {
            Assert.Equal(image.Data[i], filled.Data[i]);
        }

        Assert.Equal(77, filled[15, 15].A);
    }

    [Fact]
    public void AFeatheredMaskStillCountsAsAHole()
    {
        (PixelBuffer image, byte[] mask) = ImageWithAHole(holeValue: 128);

        PixelBuffer filled = ContentAwareFill.Fill(image, mask);

        Assert.True(Distance(filled[20, 20], Surroundings) < Distance(filled[20, 20], Removed),
            $"a partial hole was left as {filled[20, 20]}");
    }

    [Fact]
    public void AnEmptyMaskLeavesTheImageAlone()
    {
        PixelBuffer image = SplitImage();
        var mask = new byte[image.Width * image.Height];

        PixelBuffer filled = ContentAwareFill.Fill(image, mask);

        Assert.Equal(image.Data, filled.Data);
        Assert.NotSame(image, filled);
    }

    [Fact]
    public void ExtendGrowsTheCanvasAndInventsTheBorder()
    {
        PixelBuffer source = SizedImage();

        PixelBuffer grown = ContentAwareFill.Extend(source, 1, 1, 1, 1);

        Assert.Equal(4, grown.Width);
        Assert.Equal(4, grown.Height);
        Assert.Equal(source[0, 0], grown[1, 1]);
        Assert.Equal(source[1, 0], grown[2, 1]);
        Assert.Equal(source[0, 1], grown[1, 2]);
        Assert.Equal(source[1, 1], grown[2, 2]);

        int invented = 0;
        for (int y = 0; y < grown.Height; y++)
        {
            for (int x = 0; x < grown.Width; x++)
            {
                if (x is 1 or 2 && y is 1 or 2) continue;

                Rgba32 pixel = grown[x, y];
                Assert.True(pixel.A > 0, $"the invented border at {x},{y} is transparent");
                if (pixel.R + pixel.G + pixel.B > 0) invented++;
            }
        }

        Assert.Equal(12, invented);
    }

    [Fact]
    public void AZeroExtensionCopiesTheImage()
    {
        PixelBuffer source = SizedImage();

        PixelBuffer same = ContentAwareFill.Extend(source, 0, 0, 0, 0);

        Assert.True(same.SameSizeAs(source));
        Assert.NotSame(source, same);
        Assert.Equal(source.Data, same.Data);
    }

    [Fact]
    public void FillRejectsArgumentsItCannotUse()
    {
        var image = new PixelBuffer(4, 4);

        Assert.Throws<ArgumentException>(() => ContentAwareFill.Fill(image, new byte[1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Fill(image, new byte[16], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Fill(image, new byte[16], 3, 7));
    }

    [Fact]
    public void ExtendRejectsNegativeBorders()
    {
        PixelBuffer source = SizedImage();

        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Extend(source, -1, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Extend(source, 0, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Extend(source, 0, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ContentAwareFill.Extend(source, 0, 0, 0, -1));
    }

    private static (PixelBuffer Image, byte[] Mask) ImageWithAHole(byte holeValue = 255)
    {
        PixelBuffer image = SplitImage();
        var mask = new byte[image.Width * image.Height];
        for (int y = 14; y <= 26; y++)
        {
            for (int x = 14; x <= 26; x++)
            {
                Set(image, x, y, Removed);
                mask[(y * image.Width) + x] = holeValue;
            }
        }

        return (image, mask);
    }

    private static PixelBuffer SplitImage()
    {
        const int size = 64;
        var image = new PixelBuffer(size, size);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Set(image, x, y, x < size / 2 ? Surroundings : FarSide);
            }
        }

        return image;
    }

    private static PixelBuffer SizedImage()
    {
        var image = new PixelBuffer(2, 2);
        Set(image, 0, 0, new Rgba32(255, 0, 0, 255));
        Set(image, 1, 0, new Rgba32(0, 255, 0, 255));
        Set(image, 0, 1, new Rgba32(0, 0, 255, 255));
        Set(image, 1, 1, new Rgba32(255, 255, 255, 255));
        return image;
    }

    private static void Set(PixelBuffer buffer, int x, int y, Rgba32 pixel)
    {
        int i = buffer.Offset(x, y);
        buffer.Data[i] = pixel.R;
        buffer.Data[i + 1] = pixel.G;
        buffer.Data[i + 2] = pixel.B;
        buffer.Data[i + 3] = pixel.A;
    }

    private static double Distance(Rgba32 a, Rgba32 b) =>
        Math.Sqrt(Math.Pow(a.R - b.R, 2) + Math.Pow(a.G - b.G, 2) + Math.Pow(a.B - b.B, 2));
}
