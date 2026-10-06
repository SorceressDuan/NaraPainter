using NaraPainter.Models.Adjustments;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;
using OpenCvSharp;

namespace NaraPainter.Imaging.Services;

public sealed class AdjustmentFilter : IAdjustmentFilter
{
    public PixelBuffer Apply(PixelBuffer source, AdjustmentSettings settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        return settings switch
        {
            BrightnessContrastSettings brightness => brightness.IsIdentity
                ? source.Clone()
                : ApplyTables(source, [brightness.Table(), brightness.Table(), brightness.Table()]),
            LevelsSettings levels => levels.IsIdentity
                ? source.Clone()
                : ApplyTables(source, levels.Tables()),
            CurvesSettings curves => curves.IsIdentity
                ? source.Clone()
                : ApplyTables(source, curves.Tables()),
            HueSaturationSettings hueSaturation => hueSaturation.IsIdentity
                ? source.Clone()
                : ApplyHueSaturation(source, hueSaturation),
            _ => throw new NotSupportedException($"No filter is registered for {settings.GetType().Name}.")
        };
    }

    public PixelBuffer ApplyAll(PixelBuffer source, IEnumerable<AdjustmentSettings> settings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(settings);

        PixelBuffer result = source;
        foreach (AdjustmentSettings setting in settings)
        {
            result = Apply(result, setting);
        }

        // Nothing ran, but the caller still gets its own buffer rather than the one it passed in.
        return ReferenceEquals(result, source) ? source.Clone() : result;
    }

    /// <summary>
    /// Runs one 256-entry table per colour channel. The tables come out of the settings in RGB order
    /// while the Mat is BGRA, so blue reads tables[2] and alpha is merged back untouched.
    /// </summary>
    private static PixelBuffer ApplyTables(PixelBuffer source, byte[][] tables)
    {
        using var src = OpenCvInterop.ToMat(source);
        Mat[] channels = Cv2.Split(src);
        try
        {
            // MIGRATION: CIColorControls / CILevels / CIColorCurves -> Cv2.LUT, one table per channel.
            Cv2.LUT(channels[0], tables[2], channels[0]);
            Cv2.LUT(channels[1], tables[1], channels[1]);
            Cv2.LUT(channels[2], tables[0], channels[2]);

            using var merged = new Mat();
            Cv2.Merge(channels, merged);
            return OpenCvInterop.ToPixelBuffer(merged);
        }
        finally
        {
            foreach (Mat channel in channels) channel.Dispose();
        }
    }

    private static PixelBuffer ApplyHueSaturation(PixelBuffer source, HueSaturationSettings settings)
    {
        // MIGRATION: CIHueAdjust -> HueSaturationSettings.Adjust. Photoshop's per-range falloff has no
        // single OpenCV call, so the maths stays in Models and runs per pixel over straight-alpha RGBA.
        var result = source.Clone();
        byte[] data = result.Data;
        for (int i = 0; i < data.Length; i += 4)
        {
            var (red, green, blue) = settings.Adjust(data[i] / 255.0, data[i + 1] / 255.0, data[i + 2] / 255.0);
            data[i] = ToByte(red);
            data[i + 1] = ToByte(green);
            data[i + 2] = ToByte(blue);
        }

        return result;
    }

    private static byte ToByte(double value) => value <= 0 ? (byte)0 : value >= 1 ? (byte)255 : (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
}
