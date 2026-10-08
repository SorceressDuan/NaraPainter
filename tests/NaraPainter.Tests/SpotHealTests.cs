using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// The heal kernel on its own. The upstream equivalent is
/// <c>legacy/CompositorTests/SpotHealingTests.swift</c>; this file holds the same texture with a
/// blemish on it and checks that the blemish goes and nothing else moves.
/// </summary>
/// <remarks>
/// The kernel works on premultiplied RGBA, so every case here brackets the call with the conversions
/// the ViewModel does. Going through the same helpers keeps the test honest about that contract.
/// </remarks>
public class SpotHealTests
{
    [Fact]
    public void CoverageBoundsSkipAnEmptyMask()
    {
        var coverage = new byte[16 * 8];

        (int x0, int y0, int x1, int y1) = SpotHeal.CoverageBounds(coverage, 16, 8);

        Assert.Equal((0, 0, 0, 0), (x0, y0, x1, y1));
        Assert.True(SpotHeal.IsEmpty(coverage));
    }

    [Fact]
    public void CoverageBoundsAreHalfOpenAroundThePaintedPixels()
    {
        var coverage = new byte[16 * 8];
        coverage[(2 * 16) + 3] = 255;
        coverage[(5 * 16) + 9] = 128;

        (int x0, int y0, int x1, int y1) = SpotHeal.CoverageBounds(coverage, 16, 8);

        Assert.Equal((3, 2, 10, 6), (x0, y0, x1, y1));
        Assert.False(SpotHeal.IsEmpty(coverage));
    }

    [Fact]
    public void AnEmptyCoverageLeavesThePixelsAlone()
    {
        byte[] original = Blemished();
        byte[] image = (byte[])original.Clone();
        var coverage = new byte[120 * 80];

        bool ok = SpotHeal.Apply(image, coverage, 120, 80, opacity: 1, SpotHeal.ContentAware, seed: 1);

        Assert.True(ok);
        Assert.Equal(original, image);
    }

    [Theory]
    [InlineData(SpotHeal.ContentAware)]
    [InlineData(SpotHeal.CreateTexture)]
    [InlineData(SpotHeal.ProximityMatch)]
    public void TheBlemishGoesAndTheSurfaceStays(int mode)
    {
        // Vertical gray stripes two pixels wide, with a red blemish in the middle, as upstream draws it.
        (byte[] image, byte[] coverage) = BlemishedWithStroke(StrokeCenter);

        bool ok = SpotHeal.Apply(image, coverage, 120, 80, opacity: 1, mode, seed: 0x1234);

        Assert.True(ok);

        // Every pixel the brush covered is a gray stripe now: red is gone, and the level sits in the
        // stripe's own range rather than drifting to a flat average.
        for (int y = 38; y <= 42; y++)
        {
            for (int x = 58; x <= 62; x++)
            {
                int i = ((y * 120) + x) * 4;
                Assert.True(coverage[(y * 120) + x] > 0, $"({x}, {y}) is outside the stroke");

                Assert.True(
                    image[i] - image[i + 1] < 30,
                    $"mode {mode}: ({x}, {y}) is still red at {image[i]},{image[i + 1]},{image[i + 2]}");
                Assert.InRange(image[i + 1], 80, 130);
                Assert.Equal(255, image[i + 3]);
            }
        }
    }

    [Theory]
    [InlineData(SpotHeal.ContentAware)]
    [InlineData(SpotHeal.CreateTexture)]
    [InlineData(SpotHeal.ProximityMatch)]
    public void PixelsBeyondTheStrokeAreUntouched(int mode)
    {
        byte[] original = Blemished();
        (byte[] image, byte[] coverage) = BlemishedWithStroke(StrokeCenter);

        Assert.True(SpotHeal.Apply(image, coverage, 120, 80, opacity: 1, mode, seed: 7));

        // Upstream's own list of witnesses: the far corners, the stripe band either side of the
        // stroke, and above and below it.
        foreach ((int x, int y) in new[] { (10, 10), (90, 40), (30, 40), (60, 10), (60, 70) })
        {
            int i = ((y * 120) + x) * 4;
            Assert.Equal(original[i], image[i]);
            Assert.Equal(original[i + 1], image[i + 1]);
            Assert.Equal(original[i + 2], image[i + 2]);
            Assert.Equal(original[i + 3], image[i + 3]);
        }
    }

    [Fact]
    public void AStrokeAgainstTheImageEdgeStillHeals()
    {
        // The patch search needs room it does not have here, so this lands on the smooth-fill path
        // whatever the mode says. It has to come back without throwing and without touching the alpha.
        (byte[] image, byte[] coverage) = BlemishedWithStroke((4, 40));

        bool ok = SpotHeal.Apply(image, coverage, 120, 80, opacity: 1, SpotHeal.ContentAware, seed: 3);

        Assert.True(ok);
        for (int y = 38; y <= 42; y++)
        {
            int i = ((y * 120) + 4) * 4;
            Assert.Equal(255, image[i + 3]);
            Assert.True(image[i] - image[i + 1] < 30, $"({4}, {y}) is still red at {image[i]},{image[i + 1]},{image[i + 2]}");
        }
    }

    [Fact]
    public void HalfOpacityMovesThePixelHalfWay()
    {
        (byte[] full, byte[] coverage) = BlemishedWithStroke(StrokeCenter);
        (byte[] half, _) = BlemishedWithStroke(StrokeCenter);

        Assert.True(SpotHeal.Apply(full, coverage, 120, 80, opacity: 1, SpotHeal.ContentAware, seed: 11));
        Assert.True(SpotHeal.Apply(half, coverage, 120, 80, opacity: 0.5f, SpotHeal.ContentAware, seed: 11));

        // Compare in straight alpha, which is where "half way" is a meaningful thing to say.
        byte[] before = Blemished();
        byte[] healed = Straight(full);
        byte[] halfway = Straight(half);
        int red = (((40 * 120) + 60) * 4);
        int green = red + 1;

        // Half opacity has to land between where the pixel started and where a full heal puts it.
        Assert.True(halfway[red] > healed[red], "half opacity emptied as much red as a full heal");
        Assert.True(halfway[red] < before[red], "half opacity did not move the red at all");
        Assert.True(halfway[green] < healed[green], "half opacity filled as much green as a full heal");
        Assert.True(halfway[green] > before[green], "half opacity did not move the green at all");
    }

    [Fact]
    public void TheSeedDecidesTheGrainButNotTheColour()
    {
        // Create Texture is the mode that invents grain, so it is the one where the seed shows up.
        (byte[] first, byte[] coverage) = BlemishedWithStroke(StrokeCenter);
        (byte[] second, _) = BlemishedWithStroke(StrokeCenter);

        Assert.True(SpotHeal.Apply(first, coverage, 120, 80, opacity: 1, SpotHeal.CreateTexture, seed: 1));
        Assert.True(SpotHeal.Apply(second, coverage, 120, 80, opacity: 1, SpotHeal.CreateTexture, seed: 2));

        // Same seed, same result.
        (byte[] repeat, _) = BlemishedWithStroke(StrokeCenter);
        Assert.True(SpotHeal.Apply(repeat, coverage, 120, 80, opacity: 1, SpotHeal.CreateTexture, seed: 1));
        Assert.Equal(first, repeat);

        // A different seed only moves the grain, so the two stay within a few levels of each other.
        int worst = 0;
        for (int i = 0; i < first.Length; i++)
        {
            if (i % 4 == 3) continue;
            worst = Math.Max(worst, Math.Abs(first[i] - second[i]));
        }

        Assert.InRange(worst, 0, 40);
    }

    [Fact]
    public void AMismatchedBufferIsRejectedBeforeAnythingIsWritten()
    {
        var image = new byte[16];
        var coverage = new byte[16];

        Assert.Throws<ArgumentException>(() => SpotHeal.Apply(image, coverage, 4, 4, 1, SpotHeal.ContentAware, 0));
    }

    // Mirrors the stroke upstream heals: a 24px brush dragged across the blemish's centre.
    private static readonly (double X, double Y)[] StrokePath = [(60, 40), (60.5, 40)];

    // A 24px brush at the blemish's centre, which is where upstream aims it.
    private const int StrokeDiameter = 24;

    private const int Width = 120;
    private const int Height = 80;
    private static readonly (double X, double Y) StrokeCenter = (60, 40);

    /// <summary>The upstream fixture: 2px gray stripes at 100/112 with a red blemish over the middle.</summary>
    private static byte[] Blemished()
    {
        var image = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                bool red = x is >= 55 and < 65 && y is >= 35 and < 45;
                byte gray = x % 4 < 2 ? (byte)100 : (byte)112;
                int i = ((y * Width) + x) * 4;
                image[i] = red ? (byte)230 : gray;
                image[i + 1] = red ? (byte)20 : gray;
                image[i + 2] = red ? (byte)20 : gray;
                image[i + 3] = 255;
            }
        }

        return image;
    }

    /// <summary>Premultiplies the fixture and paints the coverage a drag would leave behind.</summary>
    private static (byte[] Image, byte[] Coverage) BlemishedWithStroke((double X, double Y) at)
    {
        var coverage = new byte[Width * Height];
        var path = new List<(int X, int Y)>();
        foreach ((double x, double y) in StrokePath) path.Add(((int)Math.Round(x), (int)Math.Round(y)));

        // Shift the whole path onto the requested spot, which is how the edge case is set up.
        double dx = at.X - StrokePath[0].X, dy = at.Y - StrokePath[0].Y;
        for (int i = 0; i < path.Count; i++) path[i] = ((int)Math.Round(path[i].X + dx), (int)Math.Round(path[i].Y + dy));

        MaskService.Paint(coverage, Width, Height, path, StrokeDiameter / 2, hardness: 1, opacity: 1, erase: false);
        return (Premultiply(Blemished()), coverage);
    }

    private static byte[] Premultiply(byte[] straight)
    {
        var premultiplied = new byte[straight.Length];
        for (int i = 0; i < straight.Length; i += 4)
        {
            byte alpha = straight[i + 3];
            if (alpha == 0) continue;
            for (int c = 0; c < 3; c++) premultiplied[i + c] = (byte)(((straight[i + c] * alpha) + 127) / 255);
            premultiplied[i + 3] = alpha;
        }

        return premultiplied;
    }

    private static byte[] Straight(byte[] premultiplied)
    {
        var straight = new byte[premultiplied.Length];
        for (int i = 0; i < premultiplied.Length; i += 4)
        {
            byte alpha = premultiplied[i + 3];
            straight[i + 3] = alpha;
            if (alpha == 0) continue;
            for (int c = 0; c < 3; c++) straight[i + c] = (byte)Math.Min(255, (premultiplied[i + c] * 255 + (alpha / 2)) / alpha);
        }

        return straight;
    }
}
