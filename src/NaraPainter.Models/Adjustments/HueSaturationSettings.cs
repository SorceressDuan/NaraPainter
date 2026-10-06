namespace NaraPainter.Models.Adjustments;

/// <summary>
/// The six targeted color ranges plus Master, as in Photoshop's Hue/Saturation.
/// </summary>
public enum ColorRange
{
    Master,
    Reds,
    Yellows,
    Greens,
    Cyans,
    Blues,
    Magentas
}

/// <summary>Hue in degrees, saturation and lightness in -100 to 100.</summary>
public readonly record struct RangeAdjustment(double Hue = 0, double Saturation = 0, double Lightness = 0);

/// <summary>
/// Hue/Saturation. Every range keeps its own adjustment and they all contribute, weighted by how
/// strongly each claims the pixel's original hue, so a pixel on the boundary between reds and
/// yellows gets a blend of both rather than a hard switch.
/// </summary>
public sealed class HueSaturationSettings : AdjustmentSettings
{
    public const int RangeCount = 7;

    private readonly RangeAdjustment[] _ranges = new RangeAdjustment[RangeCount];

    public HueSaturationSettings()
    {
    }

    public HueSaturationSettings(double hue, double saturation = 0, double lightness = 0, ColorRange range = ColorRange.Master)
    {
        _ranges[(int)range] = new RangeAdjustment(
            Math.Clamp(hue, -180, 180),
            Math.Clamp(saturation, -100, 100),
            Math.Clamp(lightness, -100, 100));
    }

    public bool Colorize { get; init; }

    public override string DisplayName => "Hue/Saturation";

    public RangeAdjustment this[ColorRange range] => _ranges[(int)range];

    public bool IsIdentity
    {
        get
        {
            foreach (var range in _ranges)
            {
                if (range != default) return false;
            }
            return true;
        }
    }

    /// <summary>Returns a copy with one range replaced; settings are immutable once built.</summary>
    public HueSaturationSettings With(ColorRange range, RangeAdjustment adjustment)
    {
        var copy = (HueSaturationSettings)Clone();
        return copy.Set(range, adjustment);
    }

    /// <summary>
    /// Photoshop's band for a range: full strength between the middle pair, fading to nothing across
    /// each shoulder. Degrees, wrapping at 360.
    /// </summary>
    public static (double FalloffStart, double RangeStart, double RangeEnd, double FalloffEnd) DefaultBand(ColorRange range) => range switch
    {
        ColorRange.Master => (0, 0, 360, 360),
        ColorRange.Reds => (315, 345, 15, 45),
        ColorRange.Yellows => (15, 45, 75, 105),
        ColorRange.Greens => (75, 105, 135, 165),
        ColorRange.Cyans => (135, 165, 195, 225),
        ColorRange.Blues => (195, 225, 255, 285),
        ColorRange.Magentas => (255, 285, 315, 345),
        _ => (0, 0, 360, 360)
    };

    /// <summary>How much a range applies at a given hue: 1 inside the band, 0 outside, ramped between.</summary>
    public static double BandWeight(ColorRange range, double hue)
    {
        if (range == ColorRange.Master) return 1;

        var (falloffStart, rangeStart, rangeEnd, falloffEnd) = DefaultBand(range);
        double span = Forward(falloffStart, falloffEnd);
        if (span <= 0) return 1;

        double position = Forward(falloffStart, hue);
        if (position > span) return 0;

        double rampIn = Forward(falloffStart, rangeStart);
        if (position < rampIn) return rampIn > 0 ? position / rampIn : 1;

        double plateauEnd = Forward(falloffStart, rangeEnd);
        if (position <= plateauEnd) return 1;

        double rampOut = span - plateauEnd;
        return rampOut > 0 ? (span - position) / rampOut : 1;
    }

    /// <summary>The combined hue shift, saturation and lightness for one hue, all ranges summed.</summary>
    public (double Hue, double Saturation, double Lightness) Response(double hue)
    {
        if (Colorize)
        {
            var master = _ranges[(int)ColorRange.Master];
            return (master.Hue, master.Saturation, master.Lightness);
        }

        double shift = 0, saturation = 0, lightness = 0;
        for (int i = 0; i < RangeCount; i++)
        {
            var adjustment = _ranges[i];
            if (adjustment == default) continue;
            double weight = BandWeight((ColorRange)i, hue);
            if (weight <= 0) continue;
            shift += adjustment.Hue * weight;
            saturation += adjustment.Saturation * weight;
            lightness += adjustment.Lightness * weight;
        }
        return (shift, saturation, lightness);
    }

    /// <summary>
    /// Applies the adjustment to one color. Kept in double precision and public so the pixel loops and
    /// the lookup table builder share exactly one implementation.
    /// </summary>
    public (double R, double G, double B) Adjust(double red, double green, double blue)
    {
        var (hue, saturation, lightness) = ToHsl(red, green, blue);
        double lightnessAmount;

        if (Colorize)
        {
            var master = _ranges[(int)ColorRange.Master];
            hue = Wrap(master.Hue);
            saturation = Math.Clamp(master.Saturation / 100, 0, 1);
            lightnessAmount = master.Lightness / 100;
        }
        else
        {
            var (shift, saturationAmount, lightnessValue) = Response(hue);
            lightnessAmount = lightnessValue / 100;
            hue = Wrap(hue + shift);
            saturation = AdjustSaturation(saturation, saturationAmount);
        }

        // Lightness pulls towards white above zero and towards black below, reaching either at 100.
        double amount = Math.Clamp(lightnessAmount, -1, 1);
        lightness = amount >= 0 ? lightness + ((1 - lightness) * amount) : lightness * (1 + amount);

        return ToRgb(hue, saturation, Math.Clamp(lightness, 0, 1));
    }

    /// <summary>
    /// Below zero this scales towards gray, above zero it divides by what is left, so +50 doubles the
    /// saturation and +100 takes any color all the way. Multiplicative both ways, so grays stay gray.
    /// </summary>
    public static double AdjustSaturation(double saturation, double amount)
    {
        double a = Math.Clamp(amount / 100, -1, 1);
        if (a <= 0) return Math.Max(0, saturation * (1 + a));
        return a >= 1 ? (saturation > 0 ? 1 : 0) : Math.Min(1, saturation / (1 - a));
    }

    public override AdjustmentSettings Clone()
    {
        var copy = new HueSaturationSettings { Colorize = Colorize };
        Array.Copy(_ranges, copy._ranges, RangeCount);
        return copy;
    }

    private HueSaturationSettings Set(ColorRange range, RangeAdjustment adjustment)
    {
        _ranges[(int)range] = new RangeAdjustment(
            Math.Clamp(adjustment.Hue, -180, 180),
            Math.Clamp(adjustment.Saturation, -100, 100),
            Math.Clamp(adjustment.Lightness, -100, 100));
        return this;
    }

    private static double Forward(double from, double to)
    {
        double delta = (to - from) % 360;
        return delta < 0 ? delta + 360 : delta;
    }

    private static double Wrap(double hue)
    {
        double remainder = hue % 360;
        return remainder < 0 ? remainder + 360 : remainder;
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(double red, double green, double blue)
    {
        double high = Math.Max(red, Math.Max(green, blue));
        double low = Math.Min(red, Math.Min(green, blue));
        double lightness = (high + low) / 2;
        double delta = high - low;
        if (delta <= 0) return (0, 0, lightness);

        double saturation = delta / (1 - Math.Abs((2 * lightness) - 1));
        double hue = high == red ? (green - blue) / delta
            : high == green ? ((blue - red) / delta) + 2
            : ((red - green) / delta) + 4;
        hue *= 60;
        if (hue < 0) hue += 360;
        return (hue, saturation, lightness);
    }

    private static (double R, double G, double B) ToRgb(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        double h = Wrap(hue) / 60;
        double x = c * (1 - Math.Abs((h % 2) - 1));

        var (r, g, b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        double m = lightness - (c / 2);
        return (Math.Clamp(r + m, 0, 1), Math.Clamp(g + m, 0, 1), Math.Clamp(b + m, 0, 1));
    }
}
