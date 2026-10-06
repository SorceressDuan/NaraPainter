using NaraDreamPainter.Imaging.Services;
using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Pixels;
using Xunit;

namespace NaraDreamPainter.Tests;

public class AdjustmentTests
{
    [Fact]
    public void BrightnessContrastAtZeroIsTheIdentityTable()
    {
        var settings = new BrightnessContrastSettings();

        Assert.True(settings.IsIdentity);

        byte[] table = settings.Table();
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal((byte)i, table[i]);
        }
    }

    [Fact]
    public void BrightnessLiftsEveryValueByTheSameAmount()
    {
        var settings = new BrightnessContrastSettings(25);

        Assert.False(settings.IsIdentity);
        Assert.Equal(64, settings.Map(0));
        Assert.Equal(192, settings.Map(128));
        Assert.Equal(255, settings.Map(255));
    }

    [Fact]
    public void FullBrightnessPushesEverythingToWhite()
    {
        var settings = new BrightnessContrastSettings(100);

        Assert.Equal(255, settings.Map(0));
        Assert.Equal(255, settings.Map(128));
        Assert.Equal(255, settings.Map(255));
    }

    [Fact]
    public void FullNegativeBrightnessPushesEverythingToBlack()
    {
        var settings = new BrightnessContrastSettings(-100);

        Assert.Equal(0, settings.Map(0));
        Assert.Equal(0, settings.Map(128));
        Assert.Equal(0, settings.Map(255));
    }

    [Fact]
    public void ContrastSpreadsValuesAwayFromMidGray()
    {
        var settings = new BrightnessContrastSettings(0, 100);

        // The slope is a hundred: values far from the 0.5 pivot bottom out or top out, and even a
        // single level above the pivot is multiplied by it.
        Assert.Equal(0, settings.Map(64));
        Assert.Equal(255, settings.Map(192));
        Assert.Equal(177, settings.Map(128));
    }

    [Fact]
    public void NegativeContrastFlattensEverythingToMidGray()
    {
        var settings = new BrightnessContrastSettings(0, -100);

        byte[] table = settings.Table();
        foreach (byte value in table)
        {
            Assert.Equal(128, value);
        }
    }

    [Fact]
    public void SettingsClampToTheSupportedRange()
    {
        var settings = new BrightnessContrastSettings(500, -500);

        Assert.Equal(BrightnessContrastSettings.MaxValue, settings.Brightness);
        Assert.Equal(BrightnessContrastSettings.MinValue, settings.Contrast);
    }

    [Fact]
    public void ARedPixelRotatesToGreenAndBlue()
    {
        Assert.Equal((0.0, 1.0, 0.0), Round(new HueSaturationSettings(120).Adjust(1, 0, 0)));
        Assert.Equal((0.0, 0.0, 1.0), Round(new HueSaturationSettings(-120).Adjust(1, 0, 0)));
    }

    [Fact]
    public void HueShiftWrapsAroundThreeSixty()
    {
        (double R, double G, double B) overTheTop = new HueSaturationSettings(180).Adjust(0, 0.6666667, 1);
        (double R, double G, double B) wrapped = new HueSaturationSettings().Adjust(1, 0.3333333, 0);

        // 200 degrees plus 180 lands back on 20, which is the color on the right.
        TestPixels.AssertClose(wrapped.R, overTheTop.R, "red");
        TestPixels.AssertClose(wrapped.G, overTheTop.G, "green");
        TestPixels.AssertClose(wrapped.B, overTheTop.B, "blue");
    }

    [Fact]
    public void SaturationMovesAroundTheOriginalLightness()
    {
        var settings = new HueSaturationSettings();

        Assert.Equal((0.55, 0.55, 0.55), Round(settings.With(ColorRange.Master, new RangeAdjustment(Saturation: -100)).Adjust(0.6, 0.5, 0.5)));
        Assert.Equal((1.0, 0.1, 0.1), Round(settings.With(ColorRange.Master, new RangeAdjustment(Saturation: 100)).Adjust(0.6, 0.5, 0.5)));
    }

    [Fact]
    public void GrayStaysGrayWhateverTheHueAndSaturationSay()
    {
        (double R, double G, double B) result = new HueSaturationSettings(90, 100).Adjust(0.4, 0.4, 0.4);

        Assert.Equal((0.4, 0.4, 0.4), Round(result));
    }

    [Theory]
    [InlineData(0.4, 0, 0.4)]
    [InlineData(0.4, 50, 0.8)]
    [InlineData(0.4, -50, 0.2)]
    [InlineData(0.4, 100, 1.0)]
    [InlineData(0.4, -100, 0.0)]
    [InlineData(0.0, 100, 0.0)]
    public void SaturationIsScaledMultiplicatively(double saturation, double amount, double expected)
    {
        Assert.Equal(expected, HueSaturationSettings.AdjustSaturation(saturation, amount), 10);
    }

    [Fact]
    public void BandWeightRampsAcrossTheShouldersAndIsFlatInTheMiddle()
    {
        Assert.Equal(1.0, HueSaturationSettings.BandWeight(ColorRange.Reds, 0), 10);
        Assert.Equal(0.5, HueSaturationSettings.BandWeight(ColorRange.Reds, 30), 10);
        Assert.Equal(0.0, HueSaturationSettings.BandWeight(ColorRange.Reds, 200), 10);
        Assert.Equal(1.0, HueSaturationSettings.BandWeight(ColorRange.Master, 200), 10);
    }

    [Fact]
    public void ResponseOnlyCountsTheRangesThatClaimTheHue()
    {
        var reds = new HueSaturationSettings(30, range: ColorRange.Reds);
        var master = new HueSaturationSettings(90);

        Assert.Equal(30, reds.Response(0).Hue, 10);
        Assert.Equal(0, reds.Response(200).Hue, 10);
        Assert.Equal(90, master.Response(0).Hue, 10);
        Assert.Equal(90, master.Response(200).Hue, 10);
    }

    [Fact]
    public void IdentityLevelsPassValuesThroughAndProduceIdentityTables()
    {
        var settings = new LevelsSettings(LevelRange.Identity);

        Assert.True(settings.IsIdentity);
        foreach (double value in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            Assert.Equal(value, settings.Map(value, LevelsChannel.Rgb), 10);
        }

        byte[][] tables = settings.Tables();
        foreach (byte[] table in tables)
        {
            for (int i = 0; i < 256; i++)
            {
                Assert.Equal((byte)i, table[i]);
            }
        }
    }

    [Fact]
    public void DefaultLevelsAreTheIdentityOnEveryChannel()
    {
        var settings = new LevelsSettings();
        LevelRange rgb = settings[LevelsChannel.Rgb];
        LevelRange identity = LevelRange.Identity;

        Assert.True(settings.IsIdentity,
            $"rgb=({rgb.Black}, {rgb.Gamma}, {rgb.White}, {rgb.OutputBlack}, {rgb.OutputWhite}) "
            + $"identity=({identity.Black}, {identity.Gamma}, {identity.White}, {identity.OutputBlack}, {identity.OutputWhite})");

        Assert.True(settings[LevelsChannel.Rgb].IsIdentity);
        Assert.True(settings[LevelsChannel.Red].IsIdentity);
        Assert.True(settings[LevelsChannel.Green].IsIdentity);
        Assert.True(settings[LevelsChannel.Blue].IsIdentity);

        // A range handed to the four-argument constructor has to be spelled out: new LevelRange()
        // and default are zero-initialized structs, which is not the identity range.
        Assert.True(new LevelsSettings(LevelRange.Identity).IsIdentity);
        Assert.False(default(LevelRange).IsIdentity);
        Assert.False(new LevelRange().IsIdentity);
    }

    [Fact]
    public void DefaultLevelsLeavePixelsAlone()
    {
        var source = new PixelBuffer(2, 2, [0, 0, 0, 255, 64, 128, 192, 128, 255, 255, 255, 0, 17, 34, 51, 200]);

        PixelBuffer result = new AdjustmentFilter().Apply(source, new LevelsSettings());

        Assert.Equal((IEnumerable<byte>)source.Data, result.Data);
    }

    [Fact]
    public void AWhitePointBelowTheBlackPointIsPushedAboveIt()
    {
        var settings = new LevelsSettings(new LevelRange(200, 1, 100));
        LevelRange range = settings[LevelsChannel.Rgb];

        Assert.Equal(200, range.Black);
        Assert.Equal(201, range.White);
        Assert.Equal(0.0, settings.Map(0, LevelsChannel.Rgb), 10);
        Assert.Equal(0.0, settings.Map(200 / 255.0, LevelsChannel.Rgb), 10);
    }

    [Fact]
    public void LevelLimitsClampToTheEndsOfTheRange()
    {
        LevelRange wide = new LevelRange(300, 1, 400).Normalized();
        LevelRange gamma = new LevelRange(0, 100, 255).Normalized();
        LevelRange flatGamma = new LevelRange(0, 0, 255).Normalized();

        Assert.Equal(254, wide.Black);
        Assert.Equal(255, wide.White);
        Assert.Equal(9.99, gamma.Gamma, 10);
        Assert.Equal(0.1, flatGamma.Gamma, 10);
    }

    [Fact]
    public void NonFiniteLevelLimitsFallBackToTheIdentity()
    {
        Assert.Equal(LevelRange.Identity, new LevelRange(double.NaN, double.NaN, double.NaN).Normalized());
    }

    [Fact]
    public void BlackAndWhitePointsStretchTheInputRange()
    {
        var lifted = new LevelsSettings(new LevelRange(50, 1, 255));

        Assert.Equal(0.0, lifted.Map(0, LevelsChannel.Rgb), 10);
        Assert.Equal(0.0, lifted.Map(49 / 255.0, LevelsChannel.Rgb), 10);
        Assert.Equal(1.0, lifted.Map(255 / 255.0, LevelsChannel.Rgb), 10);
    }

    [Fact]
    public void OutputLimitsCompressTheResult()
    {
        var dark = new LevelsSettings(new LevelRange(0, 1, 255, 64, 255));
        var bright = new LevelsSettings(new LevelRange(0, 1, 255, 0, 128));

        Assert.Equal(64 / 255.0, dark.Map(0, LevelsChannel.Rgb), 10);
        Assert.Equal(1.0, dark.Map(1, LevelsChannel.Rgb), 10);
        Assert.Equal(128 / 255.0, bright.Map(1, LevelsChannel.Rgb), 10);
    }

    [Fact]
    public void GammaBendsTheMidtonesInBothDirections()
    {
        Assert.Equal(0.5, new LevelRange(0, 2, 255).Map(0.25), 10);
        Assert.Equal(0.0625, new LevelRange(0, 0.5, 255).Map(0.25), 10);
    }

    [Fact]
    public void ChannelRangesRunBeforeTheCompositeRange()
    {
        var settings = new LevelsSettings(LevelRange.Identity, red: new LevelRange(0, 1, 255, 0, 0));
        byte[][] tables = settings.Tables();

        Assert.False(settings.IsIdentity);
        Assert.Equal(0.0, settings.Map(1, LevelsChannel.Red), 10);
        Assert.Equal(1.0, settings.Map(1, LevelsChannel.Green), 10);
        Assert.Equal(1.0, settings.Map(1, LevelsChannel.Blue), 10);
        Assert.All(tables[0], value => Assert.Equal((byte)0, value));
        for (int i = 0; i < 256; i++)
        {
            Assert.Equal((byte)i, tables[1][i]);
            Assert.Equal((byte)i, tables[2][i]);
        }
    }

    [Fact]
    public void LevelsTablesNeverFold()
    {
        LevelsSettings[] settings =
        [
            new(new LevelRange(40, 2.2, 210)),
            new(new LevelRange(0, 1, 255, 30, 220)),
            new(LevelRange.Identity, red: new LevelRange(10, 0.8, 240))
        ];

        foreach (LevelsSettings candidate in settings)
        {
            foreach (byte[] table in candidate.Tables())
            {
                AssertNonDecreasing(table);
            }
        }
    }

    [Fact]
    public void TheDefaultCurveIsTheIdentity()
    {
        var curves = new CurvesSettings();

        Assert.True(curves.IsIdentity);
        Assert.Equal(new[] { new CurvePoint(0, 0), new CurvePoint(255, 255) }, curves.Points(LevelsChannel.Rgb));

        byte[][] tables = curves.Tables();
        foreach (byte[] table in tables)
        {
            for (int i = 0; i < 256; i++)
            {
                Assert.Equal((byte)i, table[i]);
            }
        }
    }

    [Fact]
    public void ACurveThroughRisingHandlesNeverFolds()
    {
        var curves = new CurvesSettings().With(LevelsChannel.Red,
        [
            new CurvePoint(0, 0),
            new CurvePoint(64, 32),
            new CurvePoint(128, 200),
            new CurvePoint(192, 210),
            new CurvePoint(255, 255)
        ]);

        byte[] table = curves.Tables()[0];

        AssertNonDecreasing(table);
        Assert.Equal(0, table[0]);
        Assert.Equal(32, table[64]);
        Assert.Equal(200, table[128]);
        Assert.Equal(255, table[255]);
    }

    [Fact]
    public void AFlatStretchOfACurveStaysFlat()
    {
        var curves = new CurvesSettings().With(LevelsChannel.Green,
        [
            new CurvePoint(0, 0),
            new CurvePoint(100, 60),
            new CurvePoint(140, 60),
            new CurvePoint(255, 255)
        ]);

        byte[] table = curves.Tables()[1];

        AssertNonDecreasing(table);
        Assert.Equal(60, table[100]);
        Assert.Equal(60, table[120]);
        Assert.Equal(60, table[140]);
    }

    [Fact]
    public void TheCompositeCurveRunsAfterTheChannelCurve()
    {
        var curves = new CurvesSettings()
            .With(LevelsChannel.Red, [new CurvePoint(0, 255), new CurvePoint(255, 0)])
            .With(LevelsChannel.Rgb, [new CurvePoint(0, 0), new CurvePoint(255, 255)]);

        byte[][] tables = curves.Tables();

        Assert.Equal(255, tables[0][0]);
        Assert.Equal(155, tables[0][100]);
        Assert.Equal(0, tables[0][255]);
        Assert.Equal(100, tables[1][100]);
    }

    [Fact]
    public void CurvesRejectHandlesThatCannotBeDrawn()
    {
        var curves = new CurvesSettings();

        Assert.Throws<ArgumentException>(() => curves.With(LevelsChannel.Rgb, [new CurvePoint(0, 0)]));
        Assert.Throws<ArgumentException>(() => curves.With(LevelsChannel.Rgb, [new CurvePoint(0, 0), new CurvePoint(0, 50), new CurvePoint(255, 255)]));
        Assert.Throws<ArgumentException>(() => curves.With(LevelsChannel.Rgb, [new CurvePoint(10, 0), new CurvePoint(255, 255)]));
        Assert.Throws<ArgumentException>(() => curves.With(LevelsChannel.Rgb, [new CurvePoint(0, 0), new CurvePoint(200, 255)]));
        Assert.Throws<ArgumentNullException>(() => curves.With(LevelsChannel.Rgb, null!));
    }

    [Fact]
    public void TheFilterRunsTheTableOnColorChannelsAndLeavesAlphaAlone()
    {
        var source = new PixelBuffer(2, 1, [10, 20, 30, 40, 200, 150, 100, 250]);
        var settings = new BrightnessContrastSettings(25);

        PixelBuffer result = new AdjustmentFilter().Apply(source, settings);

        Assert.Equal(settings.Map(10), result[0, 0].R);
        Assert.Equal(settings.Map(20), result[0, 0].G);
        Assert.Equal(settings.Map(30), result[0, 0].B);
        Assert.Equal(40, result[0, 0].A);
        Assert.Equal(settings.Map(200), result[1, 0].R);
        Assert.Equal(settings.Map(150), result[1, 0].G);
        Assert.Equal(settings.Map(100), result[1, 0].B);
        Assert.Equal(250, result[1, 0].A);
    }

    [Fact]
    public void TheFilterRunsHueSaturationPerPixel()
    {
        PixelBuffer result = new AdjustmentFilter().Apply(TestPixels.One(255, 0, 0, 128), new HueSaturationSettings(120));

        Assert.Equal(new Rgba32(0, 255, 0, 128), result[0, 0]);
    }

    private static (double R, double G, double B) Round((double R, double G, double B) color) =>
        (Math.Round(color.R, 6), Math.Round(color.G, 6), Math.Round(color.B, 6));

    private static void AssertNonDecreasing(byte[] table)
    {
        for (int i = 1; i < table.Length; i++)
        {
            Assert.True(table[i] >= table[i - 1], $"table folds at {i}: {table[i - 1]} then {table[i]}");
        }
    }
}
