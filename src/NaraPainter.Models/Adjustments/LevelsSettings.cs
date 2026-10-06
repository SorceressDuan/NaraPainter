namespace NaraPainter.Models.Adjustments;

public enum LevelsChannel
{
    Rgb,
    Red,
    Green,
    Blue
}

/// <summary>
/// One channel's input and output range, in 0-255 as the Levels dialog shows them.
/// </summary>
/// <remarks>
/// A record struct's primary-constructor defaults only apply when the constructor is actually called.
/// <c>new LevelRange()</c>, <c>default</c> and zero-initialized arrays all produce Gamma 0 and White 0,
/// which flattens the input range to nothing and maps everything to black. That is why
/// <see cref="Identity"/> spells the values out and why <see cref="LevelsSettings"/> fills its four
/// ranges instead of leaving them zeroed.
/// </remarks>
public readonly record struct LevelRange(
    double Black = 0,
    double Gamma = 1,
    double White = 255,
    double OutputBlack = 0,
    double OutputWhite = 255)
{
    public static LevelRange Identity => new(0, 1, 255, 0, 255);

    public bool IsIdentity => Normalized() == Identity;

    /// <summary>Pulls the values back into Photoshop's limits; white always stays above black.</summary>
    public LevelRange Normalized()
    {
        double black = Clamp(Black, 0, 254, 0);
        double white = Clamp(White, black + 1, 255, 255);
        return new LevelRange(
            black,
            Clamp(Gamma, 0.1, 9.99, 1),
            white,
            Clamp(OutputBlack, 0, 255, 0),
            Clamp(OutputWhite, 0, 255, 255));
    }

    /// <summary>Maps one 0-1 channel value; input is stretched between black and white, then gamma bends it.</summary>
    public double Map(double value)
    {
        var range = Normalized();
        double input = Math.Clamp(((value * 255) - range.Black) / (range.White - range.Black), 0, 1);
        double output = range.OutputBlack + (Math.Pow(input, 1 / range.Gamma) * (range.OutputWhite - range.OutputBlack));
        return Math.Clamp(output / 255, 0, 1);
    }

    private static double Clamp(double value, double low, double high, double fallback) =>
        double.IsFinite(value) ? Math.Min(high, Math.Max(low, value)) : fallback;
}

/// <summary>
/// Levels. Red, green and blue are adjusted first and the composite RGB range runs on the result,
/// which is the order Photoshop applies them in.
/// </summary>
public sealed class LevelsSettings : AdjustmentSettings
{
    private readonly LevelRange[] _ranges = new LevelRange[4];

    /// <summary>Every channel starts at its identity range, not at a zeroed struct.</summary>
    public LevelsSettings()
    {
        Fill(LevelRange.Identity);
    }

    public LevelsSettings(LevelRange rgb, LevelRange? red = null, LevelRange? green = null, LevelRange? blue = null)
    {
        Fill(LevelRange.Identity);
        _ranges[(int)LevelsChannel.Rgb] = rgb;
        _ranges[(int)LevelsChannel.Red] = red ?? LevelRange.Identity;
        _ranges[(int)LevelsChannel.Green] = green ?? LevelRange.Identity;
        _ranges[(int)LevelsChannel.Blue] = blue ?? LevelRange.Identity;
    }

    private void Fill(LevelRange range)
    {
        for (int i = 0; i < _ranges.Length; i++) _ranges[i] = range;
    }

    public override string DisplayName => "Levels";

    public LevelRange this[LevelsChannel channel] => _ranges[(int)channel].Normalized();

    public bool IsIdentity => _ranges[(int)LevelsChannel.Rgb].IsIdentity
        && _ranges[(int)LevelsChannel.Red].IsIdentity
        && _ranges[(int)LevelsChannel.Green].IsIdentity
        && _ranges[(int)LevelsChannel.Blue].IsIdentity;

    public LevelsSettings With(LevelsChannel channel, LevelRange range)
    {
        var copy = (LevelsSettings)Clone();
        copy._ranges[(int)channel] = range.Normalized();
        return copy;
    }

    /// <summary>Maps one 0-1 value for a single channel: that channel's range, then the composite range.</summary>
    public double Map(double value, LevelsChannel channel)
    {
        double channelValue = channel == LevelsChannel.Rgb
            ? value
            : _ranges[(int)channel].Normalized().Map(value);
        return _ranges[(int)LevelsChannel.Rgb].Map(channelValue);
    }

    /// <summary>The per-channel 256-entry curves this adjustment applies.</summary>
    public byte[][] Tables()
    {
        var composite = _ranges[(int)LevelsChannel.Rgb].Normalized();
        var tables = new byte[3][];
        for (int c = 0; c < 3; c++)
        {
            var channelRange = _ranges[c + 1].Normalized();
            var table = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                double value = channelRange.Map(i / 255.0);
                value = composite.Map(value);
                table[i] = value <= 0 ? (byte)0 : value >= 1 ? (byte)255 : (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);
            }
            tables[c] = table;
        }
        return tables;
    }

    public override AdjustmentSettings Clone()
    {
        var copy = new LevelsSettings();
        Array.Copy(_ranges, copy._ranges, 4);
        return copy;
    }
}
