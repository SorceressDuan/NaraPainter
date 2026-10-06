using Compositor.Models.Layers;

namespace Compositor.Models.Blending;

/// <summary>
/// Separable and non-separable blend functions, per the W3C compositing spec. Channels are
/// 0-1 and the backdrop is always the lower layer.
/// </summary>
public static class BlendFunctions
{
    public static bool IsSeparable(BlendMode mode) => mode switch
    {
        BlendMode.Hue or BlendMode.Saturation or BlendMode.Color or BlendMode.Luminosity => false,
        _ => true
    };

    public static double Channel(BlendMode mode, double backdrop, double source) => mode switch
    {
        BlendMode.Normal => source,
        BlendMode.Darken => Math.Min(backdrop, source),
        BlendMode.Multiply => backdrop * source,
        BlendMode.ColorBurn => ColorBurn(backdrop, source),
        BlendMode.LinearBurn => Math.Clamp(backdrop + source - 1, 0, 1),
        BlendMode.Lighten => Math.Max(backdrop, source),
        BlendMode.Screen => backdrop + source - (backdrop * source),
        BlendMode.ColorDodge => ColorDodge(backdrop, source),
        BlendMode.LinearDodge => Math.Clamp(backdrop + source, 0, 1),
        BlendMode.Overlay => HardLight(source, backdrop),
        BlendMode.SoftLight => SoftLight(backdrop, source),
        BlendMode.HardLight => HardLight(backdrop, source),
        BlendMode.VividLight => VividLight(backdrop, source),
        BlendMode.LinearLight => LinearLight(backdrop, source),
        BlendMode.PinLight => PinLight(backdrop, source),
        BlendMode.HardMix => HardMix(backdrop, source),
        BlendMode.Difference => Math.Abs(backdrop - source),
        BlendMode.Exclusion => backdrop + source - (2 * backdrop * source),
        BlendMode.Subtract => Math.Clamp(backdrop - source, 0, 1),
        BlendMode.Divide => Divide(backdrop, source),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a separable blend mode.")
    };

    /// <summary>Blends whole colors, for the modes that work on luminosity or saturation rather than a channel.</summary>
    public static (double R, double G, double B) Color(
        BlendMode mode, (double R, double G, double B) backdrop, (double R, double G, double B) source) => mode switch
    {
        BlendMode.Hue => SetLum(SetSat(source, Sat(backdrop)), Lum(backdrop)),
        BlendMode.Saturation => SetLum(SetSat(backdrop, Sat(source)), Lum(backdrop)),
        BlendMode.Color => SetLum(source, Lum(backdrop)),
        BlendMode.Luminosity => SetLum(backdrop, Lum(source)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a non-separable blend mode.")
    };

    private static double ColorBurn(double backdrop, double source)
    {
        if (backdrop >= 1) return 1;
        if (source <= 0) return 0;
        return 1 - Math.Min(1, (1 - backdrop) / source);
    }

    private static double ColorDodge(double backdrop, double source)
    {
        if (backdrop <= 0) return 0;
        if (source >= 1) return 1;
        return Math.Min(1, backdrop / (1 - source));
    }

    private static double SoftLight(double backdrop, double source)
    {
        if (source <= 0.5) return backdrop - ((1 - (2 * source)) * backdrop * (1 - backdrop));
        double d = backdrop <= 0.25
            ? (((16 * backdrop) - 12) * backdrop + 4) * backdrop
            : Math.Sqrt(backdrop);
        return backdrop + (((2 * source) - 1) * (d - backdrop));
    }

    private static double HardLight(double backdrop, double source) => source <= 0.5
        ? backdrop * (2 * source)
        : 1 - (2 * (1 - source) * (1 - backdrop));

    private static double VividLight(double backdrop, double source) => source <= 0.5
        ? ColorBurn(backdrop, 2 * source)
        : ColorDodge(backdrop, (2 * source) - 1);

    private static double LinearLight(double backdrop, double source) => source <= 0.5
        ? Math.Clamp(backdrop + (2 * source) - 1, 0, 1)
        : Math.Clamp(backdrop + (2 * (source - 0.5)), 0, 1);

    private static double PinLight(double backdrop, double source)
    {
        if (source <= 0.5) return Math.Min(backdrop, 2 * source);
        return Math.Max(backdrop, (2 * source) - 1);
    }

    private static double HardMix(double backdrop, double source) => VividLight(backdrop, source) < 0.5 ? 0 : 1;

    private static double Divide(double backdrop, double source)
    {
        if (backdrop <= 0) return 0;
        if (source <= 0) return 1;
        return Math.Min(1, backdrop / source);
    }

    private static double Lum((double R, double G, double B) c) => (0.3 * c.R) + (0.59 * c.G) + (0.11 * c.B);

    private static double Sat((double R, double G, double B) c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    private static (double R, double G, double B) SetLum((double R, double G, double B) c, double lum)
    {
        double d = lum - Lum(c);
        var shifted = (R: c.R + d, G: c.G + d, B: c.B + d);

        // ClipColor brings an out-of-gamut result back by pulling each channel towards the luminance,
        // scaled by how far that channel sits from the luminance on the other side. Each channel is
        // divided by its own distance (lum - n at the dark end, x - lum at the light end), which is
        // what keeps the hue intact; spreading by the saturation span instead visibly shifts it.
        //
        // Both branches run unconditionally. At lum == 0 the dark-end scale is 0 and the color collapses
        // to black, at lum == 1 the light end collapses to white - that is the definition, not a
        // degenerate case to guard against. Skipping either branch when its scale hits 0 leaves a
        // half-clipped color behind.
        double low = Math.Min(shifted.R, Math.Min(shifted.G, shifted.B));
        if (low < 0)
        {
            double denominator = lum - low;
            double scale = denominator > 0 ? lum / denominator : 0;
            shifted = (lum + ((shifted.R - lum) * scale), lum + ((shifted.G - lum) * scale), lum + ((shifted.B - lum) * scale));
        }

        double high = Math.Max(shifted.R, Math.Max(shifted.G, shifted.B));
        if (high > 1)
        {
            double denominator = high - lum;
            double scale = denominator > 0 ? (1 - lum) / denominator : 0;
            shifted = (lum + ((shifted.R - lum) * scale), lum + ((shifted.G - lum) * scale), lum + ((shifted.B - lum) * scale));
        }

        return (Math.Clamp(shifted.R, 0, 1), Math.Clamp(shifted.G, 0, 1), Math.Clamp(shifted.B, 0, 1));
    }

    private static (double R, double G, double B) SetSat((double R, double G, double B) c, double sat)
    {
        // W3C reorders to min/mid/max, scales the outer two away from the minimum, then puts the
        // channels back where they were. Returning gray for a flat color is the same thing here.
        double low = Math.Min(c.R, Math.Min(c.G, c.B));
        double high = Math.Max(c.R, Math.Max(c.G, c.B));
        if (high <= low) return (0, 0, 0);
        double scale = sat / (high - low);
        return ((c.R - low) * scale, (c.G - low) * scale, (c.B - low) * scale);
    }
}
