using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NaraDreamPainter.Imaging.Services;

/// <summary>
/// Edits coverage masks: one byte per pixel at document resolution, 255 keeps the layer, 0 hides it.
/// </summary>
public static class MaskService
{
    private const double TipCurve = 2.5;

    // MIGRATION: CGContext.drawRadialGradient brush tip -> a per-pixel distance field along the stroke
    public static void Paint(byte[] mask, int width, int height, IReadOnlyList<(int X, int Y)> points, int radius, double hardness, double opacity, bool erase)
    {
        ValidateMask(mask, width, height);
        ArgumentNullException.ThrowIfNull(points);
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));

        double strength = Math.Clamp(opacity, 0, 1);
        if (strength <= 0) return;
        double edge = Math.Clamp(hardness, 0, 1);

        // A pointer reports points several pixels apart. Splicing them keeps a fast drag from
        // leaving gaps, and every pair is then painted as one capsule so the edge stays round.
        List<(int X, int Y)> samples = BrushStroke.Interpolate(points, BrushStroke.SpacingFor(radius));
        if (samples.Count == 0) return;

        if (samples.Count == 1)
        {
            Stamp(mask, width, height, samples[0], samples[0], radius, edge, strength, erase);
            return;
        }

        for (int i = 1; i < samples.Count; i++)
        {
            Stamp(mask, width, height, samples[i - 1], samples[i], radius, edge, strength, erase);
        }
    }

    // MIGRATION: Core Image CIGaussianBlur -> Cv2.GaussianBlur
    public static void Blur(byte[] mask, int width, int height, double radius)
    {
        ValidateMask(mask, width, height);
        if (radius <= 0) return;

        int kernel = KernelSize(radius);
        using var source = ToMat(mask, width, height);
        using var blurred = new Mat();
        Cv2.GaussianBlur(source, blurred, new Size(kernel, kernel), radius / 2, 0, BorderTypes.Reflect101);
        Marshal.Copy(blurred.Data, mask, 0, mask.Length);
    }

    /// <summary>
    /// Softens the edge of a selection. A flat neighbourhood blurs to itself, so the inside of the
    /// selection reads back unchanged and only the boundary picks up a ramp.
    /// </summary>
    public static void Feather(byte[] mask, int width, int height, double radius) => Blur(mask, width, height, radius);

    public static void Invert(byte[] mask)
    {
        ArgumentNullException.ThrowIfNull(mask);
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = (byte)(255 - mask[i]);
        }
    }

    public static void Fill(byte[] mask, byte value)
    {
        ArgumentNullException.ThrowIfNull(mask);
        Array.Fill(mask, value);
    }

    internal static Mat ToMat(byte[] mask, int width, int height)
    {
        ValidateMask(mask, width, height);
        var mat = new Mat(height, width, MatType.CV_8UC1);
        Marshal.Copy(mask, 0, mat.Data, mask.Length);
        return mat;
    }

    internal static void ValidateMask(byte[] mask, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (mask.Length != (long)width * height) throw new ArgumentException("The mask length does not match the dimensions.", nameof(mask));
    }

    private static void Stamp(byte[] mask, int width, int height, (int X, int Y) from, (int X, int Y) to, int radius, double hardness, double opacity, bool erase)
    {
        int left = Math.Max(0, Math.Min(from.X, to.X) - radius - 1);
        int top = Math.Max(0, Math.Min(from.Y, to.Y) - radius - 1);
        int right = Math.Min(width - 1, Math.Max(from.X, to.X) + radius + 1);
        int bottom = Math.Min(height - 1, Math.Max(from.Y, to.Y) + radius + 1);

        for (int y = top; y <= bottom; y++)
        {
            int row = y * width;
            for (int x = left; x <= right; x++)
            {
                double coverage = Falloff(DistanceToSegment(from.X, from.Y, to.X, to.Y, x, y), radius, hardness);
                if (coverage <= 0) continue;

                int level = (int)Math.Round(255 * opacity * coverage, MidpointRounding.AwayFromZero);
                if (level <= 0) continue;

                int i = row + x;
                if (erase)
                {
                    mask[i] = (byte)(mask[i] * (255 - level) / 255);
                }
                else if (mask[i] < level)
                {
                    mask[i] = (byte)level;
                }
            }
        }
    }

    // The tip's normalized Gaussian, ramped from the hardness radius out to the rim, so 0% hardness
    // fades across the whole brush and 100% stays solid to the edge.
    private static double Falloff(double distance, double radius, double hardness)
    {
        if (distance >= radius) return 0;

        double solid = radius * hardness;
        if (distance <= solid) return 1;

        double u = (distance - solid) / (radius - solid);
        return (Math.Exp(-TipCurve * u * u) - Math.Exp(-TipCurve)) / (1 - Math.Exp(-TipCurve));
    }

    private static double DistanceToSegment(double ax, double ay, double bx, double by, double px, double py)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0) return Math.Sqrt(((px - ax) * (px - ax)) + ((py - ay) * (py - ay)));

        double t = Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / lengthSquared, 0, 1);
        double cx = ax + (t * dx);
        double cy = ay + (t * dy);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }

    private static int KernelSize(double radius) => Math.Clamp(((int)Math.Ceiling(radius) * 2) + 1, 3, 255);
}
