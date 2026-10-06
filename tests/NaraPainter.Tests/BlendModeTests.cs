using NaraPainter.Models.Blending;
using NaraPainter.Models.Layers;
using Xunit;

namespace NaraPainter.Tests;

public class BlendModeTests
{
    private static readonly BlendMode[] SeparableModes = [.. Enum.GetValues<BlendMode>().Where(BlendSpec.IsSeparable)];

    private static readonly (double R, double G, double B)[] Colors =
    [
        (0, 0, 0),
        (1, 1, 1),
        (0.5, 0.5, 0.5),
        (0.25, 0.5, 0.75),
        (0.2, 0.4, 0.6),
        (1, 0, 0),
        (0, 1, 0),
        (0, 0, 1),
        (1, 1, 0),
        (0, 1, 1),
        (1, 0, 1),
        (0.75, 0.25, 0.9),
        (0.1, 0.9, 0.35),
        (0.6, 0.6, 0.1)
    ];

    [Theory]
    // Multiply: Cb x Cs
    [InlineData(BlendMode.Multiply, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.Multiply, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.Multiply, 1.0, 0.0, 0.0)]
    [InlineData(BlendMode.Multiply, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.Multiply, 0.5, 0.5, 0.25)]
    [InlineData(BlendMode.Multiply, 0.5, 1.0, 0.5)]
    [InlineData(BlendMode.Multiply, 1.0, 0.5, 0.5)]
    [InlineData(BlendMode.Multiply, 0.0, 0.5, 0.0)]
    [InlineData(BlendMode.Multiply, 0.5, 0.0, 0.0)]
    // Screen: Cb + Cs - Cb x Cs
    [InlineData(BlendMode.Screen, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.Screen, 0.0, 1.0, 1.0)]
    [InlineData(BlendMode.Screen, 1.0, 0.0, 1.0)]
    [InlineData(BlendMode.Screen, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.Screen, 0.5, 0.5, 0.75)]
    [InlineData(BlendMode.Screen, 0.5, 1.0, 1.0)]
    [InlineData(BlendMode.Screen, 1.0, 0.5, 1.0)]
    [InlineData(BlendMode.Screen, 0.0, 0.5, 0.5)]
    [InlineData(BlendMode.Screen, 0.5, 0.0, 0.5)]
    // Overlay: HardLight(Cs, Cb), so it pivots on the backdrop
    [InlineData(BlendMode.Overlay, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.Overlay, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.Overlay, 1.0, 0.0, 1.0)]
    [InlineData(BlendMode.Overlay, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.Overlay, 0.5, 0.5, 0.5)]
    [InlineData(BlendMode.Overlay, 0.25, 0.5, 0.25)]
    [InlineData(BlendMode.Overlay, 0.75, 0.5, 0.75)]
    [InlineData(BlendMode.Overlay, 0.5, 0.0, 0.0)]
    [InlineData(BlendMode.Overlay, 0.5, 1.0, 1.0)]
    // SoftLight: the spec's two branches, including D(Cb) on both sides of 0.25
    [InlineData(BlendMode.SoftLight, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.SoftLight, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.SoftLight, 0.5, 0.5, 0.5)]
    [InlineData(BlendMode.SoftLight, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.SoftLight, 1.0, 0.0, 1.0)]
    [InlineData(BlendMode.SoftLight, 0.25, 0.5, 0.25)]
    [InlineData(BlendMode.SoftLight, 0.5, 0.25, 0.375)]
    [InlineData(BlendMode.SoftLight, 0.25, 0.75, 0.375)]
    [InlineData(BlendMode.SoftLight, 0.5, 0.75, 0.6035533905932738)]
    [InlineData(BlendMode.SoftLight, 0.25, 0.0, 0.0625)]
    // ColorBurn: 1 - min(1, (1 - Cb) / Cs)
    [InlineData(BlendMode.ColorBurn, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.ColorBurn, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.ColorBurn, 0.5, 0.5, 0.0)]
    [InlineData(BlendMode.ColorBurn, 0.5, 1.0, 0.5)]
    [InlineData(BlendMode.ColorBurn, 1.0, 0.5, 1.0)]
    [InlineData(BlendMode.ColorBurn, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.ColorBurn, 0.75, 0.5, 0.5)]
    [InlineData(BlendMode.ColorBurn, 0.25, 0.5, 0.0)]
    [InlineData(BlendMode.ColorBurn, 0.5, 0.25, 0.0)]
    // ColorDodge: min(1, Cb / (1 - Cs))
    [InlineData(BlendMode.ColorDodge, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.ColorDodge, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.ColorDodge, 0.5, 0.5, 1.0)]
    [InlineData(BlendMode.ColorDodge, 0.5, 1.0, 1.0)]
    [InlineData(BlendMode.ColorDodge, 1.0, 0.5, 1.0)]
    [InlineData(BlendMode.ColorDodge, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.ColorDodge, 0.25, 0.5, 0.5)]
    [InlineData(BlendMode.ColorDodge, 0.75, 0.0, 0.75)]
    [InlineData(BlendMode.ColorDodge, 0.5, 0.25, 0.6666666666666666)]
    // HardMix: VividLight below 0.5 collapses to black, at or above it to white
    [InlineData(BlendMode.HardMix, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.HardMix, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.HardMix, 0.5, 0.5, 1.0)]
    [InlineData(BlendMode.HardMix, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.HardMix, 1.0, 0.0, 1.0)]
    [InlineData(BlendMode.HardMix, 0.25, 0.5, 0.0)]
    [InlineData(BlendMode.HardMix, 0.5, 0.0, 0.0)]
    [InlineData(BlendMode.HardMix, 0.5, 0.25, 0.0)]
    [InlineData(BlendMode.HardMix, 0.75, 1.0, 1.0)]
    // Divide: Cb / Cs, with Photoshop's floors for a black backdrop or a black source
    [InlineData(BlendMode.Divide, 0.0, 0.0, 0.0)]
    [InlineData(BlendMode.Divide, 0.0, 1.0, 0.0)]
    [InlineData(BlendMode.Divide, 1.0, 1.0, 1.0)]
    [InlineData(BlendMode.Divide, 0.5, 0.5, 1.0)]
    [InlineData(BlendMode.Divide, 0.5, 1.0, 0.5)]
    [InlineData(BlendMode.Divide, 1.0, 0.5, 1.0)]
    [InlineData(BlendMode.Divide, 0.25, 0.5, 0.5)]
    [InlineData(BlendMode.Divide, 1.0, 0.0, 1.0)]
    [InlineData(BlendMode.Divide, 0.5, 0.0, 1.0)]
    public void SeparableModesMatchHandComputedValues(BlendMode mode, double backdrop, double source, double expected)
    {
        Assert.Equal(expected, BlendFunctions.Channel(mode, backdrop, source), TestPixels.Tolerance);
    }

    [Fact]
    public void SeparableModesMatchTheSpecAcrossTheValueGrid()
    {
        double[] values = [0, 0.25, 0.5, 0.75, 1];

        foreach (BlendMode mode in SeparableModes)
        {
            foreach (double backdrop in values)
            {
                foreach (double source in values)
                {
                    double expected = BlendSpec.Channel(mode, backdrop, source);
                    double actual = BlendFunctions.Channel(mode, backdrop, source);
                    TestPixels.AssertClose(expected, actual, $"{mode}({backdrop}, {source})");
                }
            }
        }
    }

    [Fact]
    public void NonSeparableModesMatchTheSpecSetLumAndSetSatSemantics()
    {
        foreach (BlendMode mode in BlendSpec.NonSeparable)
        {
            foreach ((double R, double G, double B) backdrop in Colors)
            {
                foreach ((double R, double G, double B) source in Colors)
                {
                    (double R, double G, double B) expected = BlendSpec.Color(mode, backdrop, source);
                    (double R, double G, double B) actual = BlendFunctions.Color(mode, backdrop, source);
                    string what = $"{mode}(({backdrop.R}, {backdrop.G}, {backdrop.B}), ({source.R}, {source.G}, {source.B}))";

                    TestPixels.AssertClose(expected.R, actual.R, $"{what} red");
                    TestPixels.AssertClose(expected.G, actual.G, $"{what} green");
                    TestPixels.AssertClose(expected.B, actual.B, $"{what} blue");
                }
            }
        }
    }

    [Fact]
    public void OnlyTheFourNonSeparableModesReportThemselvesAsNonSeparable()
    {
        foreach (BlendMode mode in Enum.GetValues<BlendMode>())
        {
            Assert.Equal(BlendSpec.IsSeparable(mode), BlendFunctions.IsSeparable(mode));
        }
    }

    [Fact]
    public void ThePickerListsTwentyFourModesInTheDocumentedOrder()
    {
        string[] expected =
        [
            "Normal", "Darken", "Multiply", "ColorBurn", "LinearBurn", "Lighten", "Screen", "ColorDodge",
            "LinearDodge", "Overlay", "SoftLight", "HardLight", "VividLight", "LinearLight", "PinLight",
            "HardMix", "Difference", "Exclusion", "Subtract", "Divide", "Hue", "Saturation", "Color", "Luminosity"
        ];

        Assert.Equal(expected, Enum.GetNames<BlendMode>());
    }
}
