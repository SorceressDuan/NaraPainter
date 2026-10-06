namespace NaraDreamPainter.Models.Adjustments;

/// <summary>
/// Brightness and contrast, both applied around mid-gray: contrast pivots on 0.5 so it darkens the
/// shadows as much as it lifts the highlights, and brightness adds after that. Both span -100 to 100.
/// </summary>
public sealed class BrightnessContrastSettings : AdjustmentSettings
{
    public const double MinValue = -100;
    public const double MaxValue = 100;

    public BrightnessContrastSettings(double brightness = 0, double contrast = 0)
    {
        Brightness = Math.Clamp(brightness, MinValue, MaxValue);
        Contrast = Math.Clamp(contrast, MinValue, MaxValue);
    }

    public double Brightness { get; }

    public double Contrast { get; }

    public override string DisplayName => "Brightness/Contrast";

    public bool IsIdentity => Brightness == 0 && Contrast == 0;

    /// <summary>Maps one 0-255 channel value; the pixel loops go through the table this builds.</summary>
    public byte Map(byte value)
    {
        double v = value / 255.0;
        double c = Math.Clamp(Contrast, MinValue, MaxValue) / 100.0;
        double b = Math.Clamp(Brightness, MinValue, MaxValue) / 100.0;

        // Contrast is a slope through 0.5. Negative contrast compresses towards gray instead of
        // inverting, which is why it takes the plain 1 + c form rather than 1 / (1 - c).
        double slope = c >= 0 ? 1 / Math.Max(0.01, 1 - c) : 1 + c;
        return ToByte(((v - 0.5) * slope) + 0.5 + b);
    }

    public byte[] Table()
    {
        var table = new byte[256];
        for (int i = 0; i < 256; i++) table[i] = Map((byte)i);
        return table;
    }

    public override AdjustmentSettings Clone() => new BrightnessContrastSettings(Brightness, Contrast);

    private static byte ToByte(double value) => value <= 0 ? (byte)0 : value >= 1 ? (byte)255 : (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
}
