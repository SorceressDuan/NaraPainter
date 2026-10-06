using System.Runtime.InteropServices;
using NaraPainter.Models.Pixels;
using OpenCvSharp;

namespace NaraPainter.Imaging.Services;

/// <summary>
/// Removes the masked pixels of an image and grows a canvas past its original bounds.
/// </summary>
public static class ContentAwareFill
{
    // Radius OpenCV samples around a hole. Telea propagates from the boundary inwards, so a small
    // radius still reaches across a wide gap.
    private const double FillRadius = 3;

    // MIGRATION: the original's hand-rolled content-aware fill -> Cv2.Inpaint
    public static PixelBuffer Fill(PixelBuffer source, byte[] mask, double radius = 3, int method = 0)
    {
        ArgumentNullException.ThrowIfNull(source);
        MaskService.ValidateMask(mask, source.Width, source.Height);
        if (radius <= 0) throw new ArgumentOutOfRangeException(nameof(radius));
        InpaintTypes flags = MethodOf(method);

        if (IsEmpty(mask)) return source.Clone();

        using var image = ToBgr(source);
        using var alpha = Plane(source, 3);
        using var hole = MaskService.ToMat(mask, source.Width, source.Height);

        // Inpaint only takes a binary hole mask, so a feathered one is cut at zero.
        Cv2.Threshold(hole, hole, 0, 255, ThresholdTypes.Binary);

        using var filled = new Mat();
        Cv2.Inpaint(image, hole, filled, radius, flags);
        return Compose(filled, alpha);
    }

    /// <summary>
    /// Enlarges the canvas by the given number of pixels per side and invents the new border from
    /// the pixels that were already there.
    /// </summary>
    // MIGRATION: growing the layer past its edge for a content-aware fill -> Cv2.CopyMakeBorder + Cv2.Inpaint
    public static PixelBuffer Extend(PixelBuffer source, int left, int top, int right, int bottom)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (left < 0) throw new ArgumentOutOfRangeException(nameof(left));
        if (top < 0) throw new ArgumentOutOfRangeException(nameof(top));
        if (right < 0) throw new ArgumentOutOfRangeException(nameof(right));
        if (bottom < 0) throw new ArgumentOutOfRangeException(nameof(bottom));
        if (left == 0 && top == 0 && right == 0 && bottom == 0) return source.Clone();

        int width = source.Width + left + right;
        int height = source.Height + top + bottom;

        using var image = ToBgr(source);
        using var alpha = Plane(source, 3);
        using var canvas = new Mat();
        using var canvasAlpha = new Mat();
        Cv2.CopyMakeBorder(image, canvas, top, bottom, left, right, BorderTypes.Constant, Scalar.Black);
        Cv2.CopyMakeBorder(alpha, canvasAlpha, top, bottom, left, right, BorderTypes.Constant, Scalar.Black);

        using var hole = new Mat(height, width, MatType.CV_8UC1, Scalar.All(255));
        using (var known = new Mat(hole, new Rect(left, top, source.Width, source.Height)))
        {
            known.SetTo(Scalar.Black);
        }

        using var filled = new Mat();
        using var filledAlpha = new Mat();
        Cv2.Inpaint(canvas, hole, filled, FillRadius, InpaintTypes.Telea);
        Cv2.Inpaint(canvasAlpha, hole, filledAlpha, FillRadius, InpaintTypes.Telea);
        return Compose(filled, filledAlpha);
    }

    // 0 keeps the edges sharper than Navier-Stokes, which is what a removed object wants.
    private static InpaintTypes MethodOf(int method) => method switch
    {
        0 => InpaintTypes.Telea,
        1 => InpaintTypes.NS,
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown inpainting method.")
    };

    private static bool IsEmpty(byte[] mask)
    {
        foreach (byte value in mask)
        {
            if (value != 0) return false;
        }

        return true;
    }

    // OpenCV keeps color channels in BGR order, PixelBuffer is RGBA, so the swap is explicit here.
    private static Mat ToBgr(PixelBuffer source)
    {
        var mat = new Mat(source.Height, source.Width, MatType.CV_8UC3);
        var bgr = new byte[checked(source.Width * source.Height * 3)];
        byte[] rgba = source.Data;
        for (int i = 0, o = 0; i < rgba.Length; i += 4, o += 3)
        {
            bgr[o] = rgba[i + 2];
            bgr[o + 1] = rgba[i + 1];
            bgr[o + 2] = rgba[i];
        }

        Marshal.Copy(bgr, 0, mat.Data, bgr.Length);
        return mat;
    }

    private static Mat Plane(PixelBuffer source, int channel)
    {
        var mat = new Mat(source.Height, source.Width, MatType.CV_8UC1);
        var plane = new byte[checked(source.Width * source.Height)];
        byte[] rgba = source.Data;
        for (int i = channel, p = 0; p < plane.Length; i += 4, p++)
        {
            plane[p] = rgba[i];
        }

        Marshal.Copy(plane, 0, mat.Data, plane.Length);
        return mat;
    }

    private static PixelBuffer Compose(Mat bgr, Mat alpha)
    {
        int width = bgr.Width;
        int height = bgr.Height;
        int count = checked(width * height);
        var color = new byte[checked(count * 3)];
        var coverage = new byte[count];
        Marshal.Copy(bgr.Data, color, 0, color.Length);
        Marshal.Copy(alpha.Data, coverage, 0, coverage.Length);

        var rgba = new byte[checked(count * 4)];
        for (int i = 0, o = 0, p = 0; p < count; i += 3, o += 4, p++)
        {
            rgba[o] = color[i + 2];
            rgba[o + 1] = color[i + 1];
            rgba[o + 2] = color[i];
            rgba[o + 3] = coverage[p];
        }

        return new PixelBuffer(width, height, rgba);
    }
}
