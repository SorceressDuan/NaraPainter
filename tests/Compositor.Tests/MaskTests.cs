using Compositor.Imaging.Services;
using Xunit;

namespace Compositor.Tests;

public class MaskTests
{
    [Fact]
    public void AStrokePaintsThroughTheGapBetweenTwoPoints()
    {
        const int width = 100;
        var mask = new byte[width * 40];

        MaskService.Paint(mask, width, 40, [(10, 20), (60, 20)], radius: 10, hardness: 1, opacity: 1, erase: false);

        Assert.Equal(255, mask[(20 * width) + 35]);
        Assert.Equal(255, mask[(11 * width) + 35]);
        Assert.Equal(0, mask[(5 * width) + 35]);
        Assert.Equal(0, mask[0]);
    }

    [Fact]
    public void AStampCoversTheRadiusAndStopsAtIt()
    {
        var mask = new byte[100 * 100];

        MaskService.Paint(mask, 100, 100, [(50, 50)], radius: 10, hardness: 1, opacity: 1, erase: false);

        Assert.Equal(255, mask[(50 * 100) + 59]);
        Assert.Equal(0, mask[(50 * 100) + 61]);
    }

    [Fact]
    public void ASoftBrushRampsFromTheCentreToTheEdge()
    {
        var mask = new byte[100 * 100];

        MaskService.Paint(mask, 100, 100, [(50, 50)], radius: 20, hardness: 0, opacity: 1, erase: false);

        Assert.Equal(255, mask[(50 * 100) + 50]);
        Assert.InRange(mask[(50 * 100) + 60], 100, 155);
        Assert.Equal(0, mask[(50 * 100) + 71]);
    }

    [Fact]
    public void OpacityScalesThePaintedCoverage()
    {
        var mask = new byte[100 * 100];

        MaskService.Paint(mask, 100, 100, [(50, 50)], radius: 10, hardness: 1, opacity: 0.5, erase: false);

        Assert.InRange(mask[(50 * 100) + 50], 126, 129);
    }

    [Fact]
    public void EraseClearsCoverage()
    {
        var mask = new byte[100 * 100];
        MaskService.Fill(mask, 255);

        MaskService.Paint(mask, 100, 100, [(50, 50)], radius: 10, hardness: 1, opacity: 1, erase: true);

        Assert.Equal(0, mask[(50 * 100) + 50]);
        Assert.Equal(255, mask[(10 * 100) + 10]);
    }

    [Fact]
    public void EraseAtHalfOpacityLeavesHalfTheCoverage()
    {
        var mask = new byte[100 * 100];
        MaskService.Fill(mask, 255);

        MaskService.Paint(mask, 100, 100, [(50, 50)], radius: 10, hardness: 1, opacity: 0.5, erase: true);

        Assert.InRange(mask[(50 * 100) + 50], 120, 135);
    }

    [Fact]
    public void PaintingWithNoPointsChangesNothing()
    {
        var mask = new byte[16 * 16];
        MaskService.Fill(mask, 42);

        MaskService.Paint(mask, 16, 16, [], radius: 4, hardness: 1, opacity: 1, erase: false);

        Assert.All(mask, value => Assert.Equal(42, value));
    }

    [Fact]
    public void AStampHangingOverTheEdgeIsClippedInsteadOfThrowing()
    {
        var mask = new byte[5 * 5];

        MaskService.Paint(mask, 5, 5, [(0, 0)], radius: 10, hardness: 1, opacity: 1, erase: false);

        Assert.Equal(255, mask[0]);
        Assert.Equal(255, mask[(4 * 5) + 4]);
    }

    [Fact]
    public void FeatherKeepsTheInteriorAndSoftensTheEdge()
    {
        const int size = 200;
        var mask = new byte[size * size];
        for (int y = 60; y < 140; y++)
        {
            for (int x = 60; x < 140; x++)
            {
                mask[(y * size) + x] = 255;
            }
        }

        MaskService.Feather(mask, size, size, 8);

        Assert.Equal(255, mask[(100 * size) + 100]);
        Assert.InRange(mask[(100 * size) + 62], 1, 254);
        Assert.InRange(mask[(100 * size) + 58], 1, 254);
        Assert.Equal(0, mask[(100 * size) + 10]);
        Assert.True(mask[(100 * size) + 62] > mask[(100 * size) + 58], "the ramp must still fall off outwards");
    }

    [Fact]
    public void FeatherLeavesAUniformMaskAlone()
    {
        var mask = new byte[64 * 64];
        MaskService.Fill(mask, 255);

        MaskService.Feather(mask, 64, 64, 5);

        Assert.All(mask, value => Assert.Equal(255, value));
    }

    [Fact]
    public void BlurSpreadsCoverageAcrossTheBoundary()
    {
        const int size = 100;
        var mask = new byte[size * size];
        for (int y = 30; y < 70; y++)
        {
            for (int x = 30; x < 70; x++)
            {
                mask[(y * size) + x] = 255;
            }
        }

        MaskService.Blur(mask, size, size, 4);

        Assert.Equal(255, mask[(50 * size) + 50]);
        Assert.InRange(mask[(50 * size) + 27], 1, 254);
        Assert.InRange(mask[(50 * size) + 33], 1, 254);
    }

    [Fact]
    public void BlurWithoutARadiusLeavesTheMaskAlone()
    {
        var mask = new byte[32 * 32];
        for (int y = 8; y < 24; y++)
        {
            for (int x = 8; x < 24; x++)
            {
                mask[(y * 32) + x] = 200;
            }
        }

        byte[] before = (byte[])mask.Clone();
        MaskService.Blur(mask, 32, 32, 0);

        Assert.Equal(before, mask);
    }

    [Fact]
    public void InvertFlipsEveryByte()
    {
        byte[] mask = [0, 1, 128, 254, 255];

        MaskService.Invert(mask);

        Assert.Equal(new byte[] { 255, 254, 127, 1, 0 }, mask);
    }

    [Fact]
    public void FillSetsEveryByte()
    {
        var mask = new byte[16 * 16];

        MaskService.Fill(mask, 17);

        Assert.All(mask, value => Assert.Equal(17, value));
    }

    [Fact]
    public void MasksThatDoNotMatchTheCanvasAreRejected()
    {
        Assert.Throws<ArgumentException>(() => MaskService.Paint(new byte[10], 10, 10, [(1, 1)], 2, 1, 1, false));
        Assert.Throws<ArgumentException>(() => MaskService.Blur(new byte[10], 4, 4, 2));
        Assert.Throws<ArgumentException>(() => MaskService.Feather(new byte[10], 4, 4, 2));
        Assert.Throws<ArgumentNullException>(() => MaskService.Invert(null!));
        Assert.Throws<ArgumentNullException>(() => MaskService.Paint(new byte[16], 4, 4, null!, 2, 1, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => MaskService.Paint(new byte[16], 4, 4, [(1, 1)], 0, 1, 1, false));
    }

    [Fact]
    public void InterpolateFillsTheGapBetweenPoints()
    {
        List<(int X, int Y)> samples = BrushStroke.Interpolate([(0, 0), (10, 0)], 2);

        Assert.Equal(6, samples.Count);
        Assert.Equal((0, 0), samples[0]);
        Assert.Equal((10, 0), samples[^1]);
        for (int i = 1; i < samples.Count; i++)
        {
            Assert.True(samples[i].X - samples[i - 1].X <= 2, $"sample {i} is more than 2 px away from the previous one");
        }
    }

    [Fact]
    public void InterpolateKeepsASinglePointAndDropsRepeats()
    {
        Assert.Equal(new[] { (5, 5) }, BrushStroke.Interpolate([(5, 5)], 2));
        Assert.Empty(BrushStroke.Interpolate(Array.Empty<(int X, int Y)>(), 2));
        Assert.Equal(new[] { (0, 0), (2, 0), (4, 0) }, BrushStroke.Interpolate([(0, 0), (0, 0), (4, 0)], 2));
    }

    [Fact]
    public void InterpolateTreatsASpacingBelowOnePixelAsOnePixel()
    {
        List<(int X, int Y)> samples = BrushStroke.Interpolate([(0, 0), (10, 0)], 0.25);

        Assert.Equal(11, samples.Count);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void InterpolateRejectsANonPositiveSpacing(double spacing)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BrushStroke.Interpolate([(0, 0), (10, 0)], spacing));
    }
}
