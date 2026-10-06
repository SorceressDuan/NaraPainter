using NaraDreamPainter.Imaging.Services;
using Xunit;

namespace NaraDreamPainter.Tests;

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
    public void ASoftBrushFollowsTheUpstreamFalloff()
    {
        const int size = 100;
        const int radius = 20;
        var mask = new byte[size * size];

        MaskService.Paint(mask, size, size, [(50, 50)], radius, hardness: 0, opacity: 1, erase: false);

        // Every distance lands on one of the tip gradient's own stops, so the expected bytes come
        // from the curve in legacy/Compositor/Document/BrushStroke.swift, not from this port.
        foreach (double distance in new[] { 0.0, 5.0, 10.0, 15.0 })
        {
            Assert.Equal(UpstreamCoverage(distance, radius, 0), mask[(50 * size) + 50 + (int)distance]);
        }

        Assert.Equal(0, mask[(50 * size) + 70]);
        Assert.Equal(0, mask[(50 * size) + 75]);
    }

    [Fact]
    public void AHalfHardTipKeepsItsCoreAndFadesToTheRim()
    {
        const int size = 100;
        const int radius = 20;
        var mask = new byte[size * size];

        MaskService.Paint(mask, size, size, [(50, 50)], radius, hardness: 0.5, opacity: 1, erase: false);

        foreach (int distance in new[] { 0, 5, 10 })
        {
            Assert.Equal(255, mask[(50 * size) + 50 + distance]);
        }

        // 15px is half way through the falloff band (10 to 20), so it sits on one of the tip
        // gradient's own stops and the byte is exact.
        Assert.Equal(UpstreamCoverage(15, radius, 0.5), mask[(50 * size) + 65]);

        // Between stops the macOS tip interpolates along a chord between 25 samples while the port
        // evaluates the curve per pixel, so allow that difference.
        foreach (int distance in new[] { 12, 17 })
        {
            byte expected = UpstreamCoverage(distance, radius, 0.5);
            Assert.InRange(mask[(50 * size) + 50 + distance], expected - 2, expected + 2);
        }

        Assert.Equal(0, mask[(50 * size) + 70]);
    }

    [Fact]
    public void HigherHardnessNeverPaintsLessAtTheSameDistance()
    {
        const int size = 100;
        double[] hardnesses = [0, 0.25, 0.5, 0.75, 1];

        foreach (int distance in new[] { 2, 6, 10, 14, 18, 22 })
        {
            int previous = 0;
            foreach (double hardness in hardnesses)
            {
                var mask = new byte[size * size];
                MaskService.Paint(mask, size, size, [(50, 50)], radius: 20, hardness, opacity: 1, erase: false);

                int coverage = mask[(50 * size) + 50 + distance];
                Assert.True(coverage >= previous, $"hardness {hardness} at {distance}px fell to {coverage} from {previous}");
                previous = coverage;
            }
        }
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

        // The tip is full strength at the centre, so half opacity has to leave 255 - round(0.5 * 255).
        Assert.InRange(mask[(50 * 100) + 50], 126, 128);
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

    // legacy/Compositor/Document/BrushStroke.swift draws the tip as a radial gradient with these
    // stops: a normalized Gaussian ramped from radius * hardness out to the rim, flat inside it.
    private static byte UpstreamCoverage(double distance, double radius, double hardness)
    {
        if (distance >= radius) return 0;

        double solid = radius * hardness;
        if (distance <= solid) return 255;

        const double k = 2.5;
        double u = (distance - solid) / (radius - solid);
        double value = (Math.Exp(-k * u * u) - Math.Exp(-k)) / (1 - Math.Exp(-k));
        return (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
    }
}
