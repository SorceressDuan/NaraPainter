using System.Runtime.InteropServices;
using NaraPainter.Models.Pixels;
using OpenCvSharp;

namespace NaraPainter.Imaging.Services;

/// <summary>
/// The two filters on the menu, both built from OpenCV primitives.
/// </summary>
/// <remarks>
/// The alpha channel is carried through untouched rather than filtered with the colour: blurring
/// coverage would soften every layer edge in the document, which is a different operation from
/// softening the picture.
/// </remarks>
public static class Filters
{
    /// <summary>Gaussian blur. <paramref name="radius"/> is a pixel radius, turned into an odd kernel.</summary>
    public static PixelBuffer GaussianBlur(PixelBuffer source, double radius)
    {
        ArgumentNullException.ThrowIfNull(source);

        int size = KernelSize(radius);
        if (size <= 1) return source.Clone();

        (Mat bgr, byte[] alpha) = Split(source);
        using (bgr)
        {
            OpenCvSharp.Cv2.GaussianBlur(bgr, bgr, new Size(size, size), 0);
            return Merge(bgr, alpha, source.Width, source.Height);
        }
    }

    /// <summary>
    /// Unsharp mask: the picture plus a scaled copy of the difference between it and a blurred version.
    /// </summary>
    public static PixelBuffer UnsharpMask(PixelBuffer source, double radius, double amount = 1.0, int threshold = 0)
    {
        ArgumentNullException.ThrowIfNull(source);

        int size = KernelSize(radius);
        if (size <= 1 || amount <= 0) return source.Clone();

        (Mat bgr, byte[] alpha) = Split(source);
        using (bgr)
        using (Mat blurred = new())
        {
            OpenCvSharp.Cv2.GaussianBlur(bgr, blurred, new Size(size, size), 0);

            if (threshold <= 0)
            {
                OpenCvSharp.Cv2.AddWeighted(bgr, 1 + amount, blurred, -amount, 0, bgr);
            }
            else
            {
                // Only the edges sharper than the threshold are pushed, which is what keeps a sharpening
                // pass from turning flat areas into noise.
                using Mat difference = new();
                OpenCvSharp.Cv2.Absdiff(bgr, blurred, difference);
                using Mat mask = new();
                OpenCvSharp.Cv2.Threshold(difference, mask, threshold, 255, ThresholdTypes.Binary);
                mask.ConvertTo(mask, MatType.CV_8UC3);

                using Mat sharpened = new();
                OpenCvSharp.Cv2.AddWeighted(bgr, 1 + amount, blurred, -amount, 0, sharpened);
                sharpened.CopyTo(bgr, mask);
            }

            return Merge(bgr, alpha, source.Width, source.Height);
        }
    }

    /// <summary>OpenCV wants an odd kernel no smaller than three.</summary>
    private static int KernelSize(double radius)
    {
        if (radius <= 0) return 1;

        int size = (int)Math.Round(radius * 2) + 1;
        if (size % 2 == 0) size++;
        return Math.Clamp(size, 1, 301);
    }

    /// <summary>Colour without alpha, so a filter cannot move coverage around.</summary>
    private static (Mat Bgr, byte[] Alpha) Split(PixelBuffer source)
    {
        var bgr = new Mat(source.Height, source.Width, MatType.CV_8UC3);
        var bgrBytes = new byte[source.PixelCount * 3];
        var alpha = new byte[source.PixelCount];

        byte[] data = source.Data;
        for (int p = 0, i = 0, o = 0; p < source.PixelCount; p++, i += 4, o += 3)
        {
            bgrBytes[o] = data[i + 2];
            bgrBytes[o + 1] = data[i + 1];
            bgrBytes[o + 2] = data[i];
            alpha[p] = data[i + 3];
        }

        Marshal.Copy(bgrBytes, 0, bgr.Data, bgrBytes.Length);
        return (bgr, alpha);
    }

    private static PixelBuffer Merge(Mat bgr, byte[] alpha, int width, int height)
    {
        var bgrBytes = new byte[width * height * 3];
        Marshal.Copy(bgr.Data, bgrBytes, 0, bgrBytes.Length);

        var result = new PixelBuffer(width, height);
        byte[] data = result.Data;

        for (int p = 0, i = 0, o = 0; p < result.PixelCount; p++, i += 4, o += 3)
        {
            data[i] = bgrBytes[o + 2];
            data[i + 1] = bgrBytes[o + 1];
            data[i + 2] = bgrBytes[o];
            data[i + 3] = alpha[p];
        }

        return result;
    }
}
