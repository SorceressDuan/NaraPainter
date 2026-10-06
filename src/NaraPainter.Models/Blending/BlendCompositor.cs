using System.Collections.Concurrent;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;

namespace NaraPainter.Models.Blending;

/// <summary>
/// Composites one buffer over another with a blend mode, opacity and optional coverage.
/// This is the reference implementation; the GPU path in NaraPainter.Compositing evaluates the
/// same BlendFunctions formulas, so the two agree pixel for pixel.
/// </summary>
public static class BlendCompositor
{
    private static readonly ConcurrentDictionary<BlendMode, byte[]> Tables = new();

    /// <summary>
    /// Writes <paramref name="source"/> over <paramref name="backdrop"/> and returns the result.
    /// Either buffer may be fully or partly transparent; alpha is straight, not premultiplied.
    /// </summary>
    public static PixelBuffer Composite(PixelBuffer backdrop, PixelBuffer source, BlendMode mode, double opacity = 1)
        => Composite(backdrop, source, mode, opacity, null);

    /// <summary>
    /// <paramref name="coverage"/> is a per-pixel 0-255 multiplier on the source alpha, used for
    /// layer masks and selections. It must match the backdrop's size when given.
    /// </summary>
    public static PixelBuffer Composite(PixelBuffer backdrop, PixelBuffer source, BlendMode mode, double opacity, byte[]? coverage)
    {
        ArgumentNullException.ThrowIfNull(backdrop);
        ArgumentNullException.ThrowIfNull(source);
        if (!backdrop.SameSizeAs(source)) throw new ArgumentException("Buffers must be the same size.", nameof(source));
        if (coverage is not null && coverage.Length != backdrop.PixelCount)
            throw new ArgumentException("Coverage must match the backdrop size.", nameof(coverage));

        byte[]? table = BlendFunctions.IsSeparable(mode) ? TableFor(mode) : null;
        bool nonSeparable = !BlendFunctions.IsSeparable(mode);
        double layerAlpha = Math.Clamp(opacity, 0, 1);

        var result = new PixelBuffer(backdrop.Width, backdrop.Height);
        byte[] dst = result.Data;
        byte[] under = backdrop.Data;
        byte[] over = source.Data;

        int count = backdrop.PixelCount;
        for (int p = 0; p < count; p++)
        {
            int i = p * 4;
            double sourceAlpha = over[i + 3] / 255.0 * layerAlpha;
            if (coverage is not null) sourceAlpha *= coverage[p] / 255.0;

            double backdropAlpha = under[i + 3] / 255.0;

            double br = under[i] / 255.0, bg = under[i + 1] / 255.0, bb = under[i + 2] / 255.0;
            double sr = over[i] / 255.0, sg = over[i + 1] / 255.0, sb = over[i + 2] / 255.0;

            double cr, cg, cb;
            if (nonSeparable)
            {
                (cr, cg, cb) = BlendFunctions.Color(mode, (br, bg, bb), (sr, sg, sb));
            }
            else
            {
                cr = table![(under[i] * 256) + over[i]] / 255.0;
                cg = table![(under[i + 1] * 256) + over[i + 1]] / 255.0;
                cb = table![(under[i + 2] * 256) + over[i + 2]] / 255.0;
            }

            double alpha = sourceAlpha + (backdropAlpha * (1 - sourceAlpha));
            if (alpha <= 0)
            {
                dst[i] = 0;
                dst[i + 1] = 0;
                dst[i + 2] = 0;
                dst[i + 3] = 0;
                continue;
            }

            dst[i] = ToByte(BlendChannel(br, sr, cr, backdropAlpha, sourceAlpha) / alpha);
            dst[i + 1] = ToByte(BlendChannel(bg, sg, cg, backdropAlpha, sourceAlpha) / alpha);
            dst[i + 2] = ToByte(BlendChannel(bb, sb, cb, backdropAlpha, sourceAlpha) / alpha);
            dst[i + 3] = ToByte(alpha);
        }

        return result;
    }

    /// <summary>
    /// The W3C source-over form, weighted by the blend so the result is premultiplied; the caller
    /// divides by the output alpha afterwards to get back to straight alpha.
    /// </summary>
    private static double BlendChannel(double backdrop, double source, double blended, double backdropAlpha, double sourceAlpha)
    {
        double weighted = ((1 - backdropAlpha) * source) + (backdropAlpha * blended);
        return ((1 - sourceAlpha) * backdrop * backdropAlpha) + (sourceAlpha * weighted);
    }

    private static byte ToByte(double value) => value <= 0 ? (byte)0 : value >= 1 ? (byte)255 : (byte)Math.Round(value * 255, MidpointRounding.AwayFromZero);

    /// <summary>Precomputed channel results for a separable mode, indexed backdrop * 256 + source.</summary>
    private static byte[] TableFor(BlendMode mode) => Tables.GetOrAdd(mode, m =>
    {
        var table = new byte[256 * 256];
        for (int b = 0; b < 256; b++)
        {
            for (int s = 0; s < 256; s++)
            {
                table[(b * 256) + s] = ToByte(BlendFunctions.Channel(m, b / 255.0, s / 255.0));
            }
        }
        return table;
    });
}
