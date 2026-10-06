using NaraDreamPainter.Models.Layers;

namespace NaraDreamPainter.Tests;

/// <summary>
/// The blend maths from the W3C compositing spec, written out a second time here on purpose: the
/// tests compare the implementation against the spec, so a mistake in one copy cannot hide behind
/// the other. VividLight, LinearLight, PinLight, HardMix, Subtract and Divide are Photoshop's, not
/// the spec's, and follow the formulas Photoshop documents.
/// </summary>
internal static class BlendSpec
{
    internal static readonly BlendMode[] NonSeparable =
        [BlendMode.Hue, BlendMode.Saturation, BlendMode.Color, BlendMode.Luminosity];

    internal static bool IsSeparable(BlendMode mode) => !NonSeparable.Contains(mode);

    internal static double Channel(BlendMode mode, double cb, double cs) => mode switch
    {
        BlendMode.Normal => cs,
        BlendMode.Darken => Math.Min(cb, cs),
        BlendMode.Multiply => cb * cs,
        BlendMode.ColorBurn => ColorBurn(cb, cs),
        BlendMode.LinearBurn => Clamp(cb + cs - 1),
        BlendMode.Lighten => Math.Max(cb, cs),
        BlendMode.Screen => cb + cs - (cb * cs),
        BlendMode.ColorDodge => ColorDodge(cb, cs),
        BlendMode.LinearDodge => Clamp(cb + cs),
        BlendMode.Overlay => HardLight(cs, cb),
        BlendMode.SoftLight => SoftLight(cb, cs),
        BlendMode.HardLight => HardLight(cb, cs),
        BlendMode.VividLight => VividLight(cb, cs),
        BlendMode.LinearLight => Clamp(cb + (2 * cs) - 1),
        BlendMode.PinLight => PinLight(cb, cs),
        BlendMode.HardMix => VividLight(cb, cs) < 0.5 ? 0 : 1,
        BlendMode.Difference => Math.Abs(cb - cs),
        BlendMode.Exclusion => cb + cs - (2 * cb * cs),
        BlendMode.Subtract => Clamp(cb - cs),
        BlendMode.Divide => Divide(cb, cs),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a separable blend mode.")
    };

    internal static (double R, double G, double B) Color(
        BlendMode mode,
        (double R, double G, double B) backdrop,
        (double R, double G, double B) source) => mode switch
    {
        BlendMode.Hue => SetLum(SetSat(source, Sat(backdrop)), Lum(backdrop)),
        BlendMode.Saturation => SetLum(SetSat(backdrop, Sat(source)), Lum(backdrop)),
        BlendMode.Color => SetLum(source, Lum(backdrop)),
        BlendMode.Luminosity => SetLum(backdrop, Lum(source)),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Not a non-separable blend mode.")
    };

    /// <summary>
    /// Source-over with blending, from the spec's general formula: the blend is applied in place,
    /// then weighted by the alphas, and the result is divided back out to straight alpha.
    /// </summary>
    internal static (double R, double G, double B, double A) Composite(
        BlendMode mode,
        (double R, double G, double B, double A) backdrop,
        (double R, double G, double B, double A) source,
        double opacity = 1,
        double coverage = 1)
    {
        double sourceAlpha = source.A * opacity * coverage;
        double backdropAlpha = backdrop.A;

        double blendedR, blendedG, blendedB;
        if (IsSeparable(mode))
        {
            blendedR = Channel(mode, backdrop.R, source.R);
            blendedG = Channel(mode, backdrop.G, source.G);
            blendedB = Channel(mode, backdrop.B, source.B);
        }
        else
        {
            (blendedR, blendedG, blendedB) = Color(mode, (backdrop.R, backdrop.G, backdrop.B), (source.R, source.G, source.B));
        }

        double alpha = sourceAlpha + (backdropAlpha * (1 - sourceAlpha));
        if (alpha <= 0) return (0, 0, 0, 0);

        return (
            Weighted(backdrop.R, source.R, blendedR, backdropAlpha, sourceAlpha) / alpha,
            Weighted(backdrop.G, source.G, blendedG, backdropAlpha, sourceAlpha) / alpha,
            Weighted(backdrop.B, source.B, blendedB, backdropAlpha, sourceAlpha) / alpha,
            alpha);
    }

    internal static double Lum((double R, double G, double B) c) => (0.3 * c.R) + (0.59 * c.G) + (0.11 * c.B);

    internal static double Sat((double R, double G, double B) c) =>
        Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));

    internal static (double R, double G, double B) SetLum((double R, double G, double B) c, double lum)
    {
        double d = lum - Lum(c);
        return ClipColor((c.R + d, c.G + d, c.B + d));
    }

    internal static (double R, double G, double B) SetSat((double R, double G, double B) c, double sat)
    {
        double low = Math.Min(c.R, Math.Min(c.G, c.B));
        double high = Math.Max(c.R, Math.Max(c.G, c.B));
        if (high <= low) return (0, 0, 0);

        double scale = sat / (high - low);
        return ((c.R - low) * scale, (c.G - low) * scale, (c.B - low) * scale);
    }

    // The spec clips low and high in sequence, scaling around the luminosity of the shifted colour.
    private static (double R, double G, double B) ClipColor((double R, double G, double B) c)
    {
        double lum = Lum(c);
        double low = Math.Min(c.R, Math.Min(c.G, c.B));
        if (low < 0) c = Around(c, lum, lum / (lum - low));

        double high = Math.Max(c.R, Math.Max(c.G, c.B));
        if (high > 1) c = Around(c, lum, (1 - lum) / (high - lum));

        return (Clamp(c.R), Clamp(c.G), Clamp(c.B));
    }

    private static (double R, double G, double B) Around((double R, double G, double B) c, double pivot, double factor) =>
        (pivot + ((c.R - pivot) * factor), pivot + ((c.G - pivot) * factor), pivot + ((c.B - pivot) * factor));

    private static double Weighted(double cb, double cs, double blended, double backdropAlpha, double sourceAlpha)
    {
        double mixed = ((1 - backdropAlpha) * cs) + (backdropAlpha * blended);
        return (sourceAlpha * mixed) + ((1 - sourceAlpha) * backdropAlpha * cb);
    }

    private static double ColorDodge(double cb, double cs)
    {
        if (cb <= 0) return 0;
        if (cs >= 1) return 1;
        return Math.Min(1, cb / (1 - cs));
    }

    private static double ColorBurn(double cb, double cs)
    {
        if (cb >= 1) return 1;
        if (cs <= 0) return 0;
        return 1 - Math.Min(1, (1 - cb) / cs);
    }

    private static double HardLight(double cb, double cs) => cs <= 0.5
        ? cb * (2 * cs)
        : 1 - (2 * (1 - cs) * (1 - cb));

    private static double SoftLight(double cb, double cs)
    {
        if (cs <= 0.5) return cb - ((1 - (2 * cs)) * cb * (1 - cb));

        double d = cb <= 0.25 ? (((16 * cb) - 12) * cb + 4) * cb : Math.Sqrt(cb);
        return cb + (((2 * cs) - 1) * (d - cb));
    }

    private static double VividLight(double cb, double cs) => cs <= 0.5
        ? ColorBurn(cb, 2 * cs)
        : ColorDodge(cb, (2 * cs) - 1);

    private static double PinLight(double cb, double cs) => cs <= 0.5
        ? Math.Min(cb, 2 * cs)
        : Math.Max(cb, (2 * cs) - 1);

    private static double Divide(double cb, double cs)
    {
        if (cb <= 0) return 0;
        if (cs <= 0) return 1;
        return Math.Min(1, cb / cs);
    }

    private static double Clamp(double value) => Math.Min(1, Math.Max(0, value));
}
