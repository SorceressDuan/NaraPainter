using NaraPainter.Models.Blending;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using Xunit;

namespace NaraPainter.Tests;

public class AlphaCompositingTests
{
    private static readonly (byte R, byte G, byte B, byte A)[] Pixels =
    [
        (0, 0, 0, 0),
        (255, 255, 255, 255),
        (0, 0, 0, 255),
        (200, 60, 40, 255),
        (250, 200, 10, 255),
        (40, 180, 220, 200),
        (30, 200, 90, 128),
        (120, 120, 120, 100),
        (10, 20, 240, 64),
        (255, 0, 0, 1),
        (7, 7, 7, 250)
    ];

    [Fact]
    public void EveryModeMatchesTheSpecCompositeReference()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            foreach (var backdrop in Pixels)
            {
                foreach (var source in Pixels)
                {
                    PixelBuffer result = BlendCompositor.Composite(
                        TestPixels.One(backdrop.R, backdrop.G, backdrop.B, backdrop.A),
                        TestPixels.One(source.R, source.G, source.B, source.A),
                        mode);

                    (double R, double G, double B, double A) expected = BlendSpec.Composite(mode, Unit(backdrop), Unit(source));
                    (double R, double G, double B, double A) actual = TestPixels.Unit(result);
                    string what = $"{mode} {backdrop} over {source}";

                    TestPixels.AssertClose(expected.R, actual.R, $"{what} red");
                    TestPixels.AssertClose(expected.G, actual.G, $"{what} green");
                    TestPixels.AssertClose(expected.B, actual.B, $"{what} blue");
                    TestPixels.AssertClose(expected.A, actual.A, $"{what} alpha");
                }
            }
        }
    }

    [Fact]
    public void ATransparentBackdropLeavesTheSourceAlone()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(200, 100, 50, 255), mode);
            Assert.Equal(new Rgba32(200, 100, 50, 255), result[0, 0]);
        }
    }

    [Fact]
    public void ATransparentSourceLeavesTheBackdropAlone()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            PixelBuffer result = BlendCompositor.Composite(TestPixels.One(200, 100, 50, 255), TestPixels.One(10, 220, 30, 0), mode);
            Assert.Equal(new Rgba32(200, 100, 50, 255), result[0, 0]);
        }
    }

    [Fact]
    public void ColorKeepsItsValueWhenOnlyTheAlphaChanges()
    {
        PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(200, 100, 50, 128), BlendMode.Normal);

        // Premultiplying would drag the channels down towards 100, 50 and 25.
        Assert.Equal(new Rgba32(200, 100, 50, 128), result[0, 0]);
    }

    [Fact]
    public void ZeroOpacityLeavesTheBackdropAlone()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 255, 255), TestPixels.One(255, 0, 0, 255), mode, 0);
            Assert.Equal(new Rgba32(0, 0, 255, 255), result[0, 0]);
        }
    }

    [Fact]
    public void HalfOpacityHalvesTheAlphaOverATransparentBackdrop()
    {
        PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(255, 0, 0, 255), BlendMode.Normal, 0.5);
        Assert.Equal(new Rgba32(255, 0, 0, 128), result[0, 0]);
    }

    [Fact]
    public void HalfOpacityMeetsAnOpaqueBackdropInTheMiddle()
    {
        PixelBuffer result = BlendCompositor.Composite(TestPixels.One(255, 255, 255, 255), TestPixels.One(0, 0, 0, 255), BlendMode.Normal, 0.5);
        Assert.Equal(new Rgba32(128, 128, 128, 255), result[0, 0]);
    }

    [Fact]
    public void HalfOpacityOnAHalfTransparentSourceOnlyMovesTheAlpha()
    {
        PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(255, 0, 0, 128), BlendMode.Normal, 0.5);
        Assert.Equal(new Rgba32(255, 0, 0, 64), result[0, 0]);
    }

    [Fact]
    public void CoverageReachesTheAlphaWithoutTouchingTheColor()
    {
        PixelBuffer hidden = BlendCompositor.Composite(TestPixels.One(0, 0, 255, 255), TestPixels.One(255, 0, 0, 255), BlendMode.Normal, 1, [0]);
        PixelBuffer full = BlendCompositor.Composite(TestPixels.One(0, 0, 255, 255), TestPixels.One(255, 0, 0, 255), BlendMode.Normal, 1, [255]);
        PixelBuffer half = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(255, 0, 0, 255), BlendMode.Normal, 1, [128]);

        Assert.Equal(new Rgba32(0, 0, 255, 255), hidden[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 255), full[0, 0]);
        Assert.Equal(new Rgba32(255, 0, 0, 128), half[0, 0]);
    }

    [Fact]
    public void CoverageMultipliesTheSourceAlpha()
    {
        PixelBuffer result = BlendCompositor.Composite(TestPixels.One(0, 0, 0, 0), TestPixels.One(255, 0, 0, 128), BlendMode.Normal, 1, [128]);

        // 128/255 from the source times 128/255 from the mask is a quarter, so the output alpha drops
        // to 64 while the straight-alpha color stays put.
        Assert.Equal(new Rgba32(255, 0, 0, 64), result[0, 0]);
    }

    [Fact]
    public void CoverageIsPerPixel()
    {
        var backdrop = TestPixels.Solid(2, 1, 0, 0, 255, 255);
        var source = TestPixels.Solid(2, 1, 255, 0, 0, 255);

        PixelBuffer result = BlendCompositor.Composite(backdrop, source, BlendMode.Normal, 1, [255, 0]);

        Assert.Equal(new Rgba32(255, 0, 0, 255), result[0, 0]);
        Assert.Equal(new Rgba32(0, 0, 255, 255), result[1, 0]);
    }

    [Fact]
    public void FullyTransparentInputOnBothSidesComesOutTransparent()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            PixelBuffer result = BlendCompositor.Composite(TestPixels.One(90, 90, 90, 0), TestPixels.One(200, 10, 10, 0), mode);
            Assert.Equal(Rgba32.Transparent, result[0, 0]);
        }
    }

    [Fact]
    public void BufferSizesAndCoverageLengthsAreChecked()
    {
        var backdrop = new PixelBuffer(2, 2);
        var source = new PixelBuffer(3, 1);

        Assert.Throws<ArgumentException>(() => BlendCompositor.Composite(backdrop, source, BlendMode.Normal));
        Assert.Throws<ArgumentException>(() => BlendCompositor.Composite(backdrop, backdrop, BlendMode.Normal, 1, [0, 0, 0]));
        Assert.Throws<ArgumentNullException>(() => BlendCompositor.Composite(null!, backdrop, BlendMode.Normal));
        Assert.Throws<ArgumentNullException>(() => BlendCompositor.Composite(backdrop, null!, BlendMode.Normal));
    }

    private static (double R, double G, double B, double A) Unit((byte R, byte G, byte B, byte A) pixel) =>
        (pixel.R / 255.0, pixel.G / 255.0, pixel.B / 255.0, pixel.A / 255.0);
}
