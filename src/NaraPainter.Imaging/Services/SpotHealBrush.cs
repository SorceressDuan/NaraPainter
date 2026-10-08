using NaraPainter.Models.Pixels;

namespace NaraPainter.Imaging.Services;

/// <summary>
/// Runs <see cref="SpotHeal"/> over the part of a layer a stroke covered, and hands back a new buffer.
/// </summary>
/// <remarks>
/// The upstream brush does this inside its own tile bookkeeping
/// (<c>legacy/Compositor/Document/BrushStroke.swift</c>, <c>heal()</c>); here the layer is a single
/// buffer, so the same steps are: shrink the work to just around the stroke, convert alpha, run the
/// kernel, convert back. Nothing is written to <paramref name="source"/>.
/// </remarks>
public static class SpotHealBrush
{
    /// <summary>
    /// Heals the covered pixels of <paramref name="source"/> into a copy of it. Returns null when the
    /// coverage is empty or falls outside the buffer, so the caller can leave the layer alone.
    /// </summary>
    /// <param name="coverage">
    /// One byte per pixel at the layer's resolution, as <see cref="MaskService.Paint"/> lays it down.
    /// </param>
    public static PixelBuffer? Heal(PixelBuffer source, byte[] coverage, float opacity, int mode, uint seed)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(coverage);
        if (coverage.Length != (long)source.Width * source.Height)
        {
            throw new ArgumentException("The coverage does not match the layer's dimensions.", nameof(coverage));
        }

        (int x0, int y0, int x1, int y1) = SpotHeal.CoverageBounds(coverage, source.Width, source.Height);
        if (x1 <= x0 || y1 <= y0) return null;

        // Room for the kernel's patch search, which looks about three spot-widths away. The kernel
        // grows its own ring inside this, so this only has to leave the search somewhere to land.
        double reach = (Math.Max(x1 - x0, y1 - y0) + 32) * 3.2;
        int left = (int)Math.Max(0, Math.Floor(x0 - reach));
        int top = (int)Math.Max(0, Math.Floor(y0 - reach));
        int right = (int)Math.Min(source.Width, Math.Ceiling(x1 + reach));
        int bottom = (int)Math.Min(source.Height, Math.Ceiling(y1 + reach));

        int width = right - left;
        int height = bottom - top;
        if (width <= 0 || height <= 0) return null;

        var region = new PixelBuffer(width, height);
        var regionCoverage = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            int from = source.Offset(left, top + y);
            int to = y * width * 4;
            Array.Copy(source.Data, from, region.Data, to, width * 4);
            Array.Copy(coverage, ((top + y) * source.Width) + left, regionCoverage, y * width, width);
        }

        ToPremultiplied(region.Data);
        if (!SpotHeal.Apply(region.Data, regionCoverage, width, height, opacity, mode, seed)) return null;
        ToStraight(region.Data);

        var healed = source.Clone();
        for (int y = 0; y < height; y++)
        {
            Array.Copy(region.Data, y * width * 4, healed.Data, healed.Offset(left, top + y), width * 4);
        }

        return healed;
    }

    // MIGRATION: the kernel works on premultiplied RGBA (HealPixels.h), the layer does not.
    private static void ToPremultiplied(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            // A pixel with no alpha has no colour to carry, which is the one thing this cannot
            // round-trip.
            if (rgba[i + 3] == 0)
            {
                rgba[i] = 0;
                rgba[i + 1] = 0;
                rgba[i + 2] = 0;
                continue;
            }

            for (int c = 0; c < 3; c++) rgba[i + c] = (byte)(((rgba[i + c] * rgba[i + 3]) + 127) / 255);
        }
    }

    private static void ToStraight(byte[] rgba)
    {
        for (int i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;

            for (int c = 0; c < 3; c++)
            {
                int straight = ((rgba[i + c] * 255) + (rgba[i + 3] / 2)) / rgba[i + 3];
                rgba[i + c] = (byte)Math.Min(255, straight);
            }
        }
    }
}
