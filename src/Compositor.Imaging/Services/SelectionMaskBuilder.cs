using Compositor.Models.Pixels;
using Compositor.Models.Services;
using OpenCvSharp;

namespace Compositor.Imaging.Services;

public sealed class SelectionMaskBuilder : ISelectionMaskBuilder
{
    public byte[] Build(SelectionRegion region, int canvasWidth, int canvasHeight, double feather = 0)
    {
        ValidateCanvas(canvasWidth, canvasHeight);

        var coverage = new byte[checked(canvasWidth * canvasHeight)];
        if (region.IsEmpty) return coverage;

        using var mask = new Mat(canvasHeight, canvasWidth, MatType.CV_8UC1, new Scalar(0));

        // MIGRATION: NSBezierPath / CGPath fill -> Cv2.Rectangle / Cv2.Ellipse. Both clip to the
        // canvas on their own, so a region hanging over an edge keeps the part that is inside.
        if (region.Shape == SelectionShape.Ellipse)
        {
            var bounds = new RotatedRect(
                new Point2f(region.X + (region.Width / 2f), region.Y + (region.Height / 2f)),
                new Size2f(region.Width, region.Height),
                0);
            Cv2.Ellipse(mask, bounds, new Scalar(255), -1, LineTypes.Link8);
        }
        else
        {
            Cv2.Rectangle(mask, new Rect(region.X, region.Y, region.Width, region.Height), new Scalar(255), -1, LineTypes.Link8);
        }

        if (feather > 0)
        {
            // MIGRATION: CIGaussianBlur(sigma: feather / 2) -> Cv2.GaussianBlur. Same sigma the macOS
            // selection carried; a zero kernel size lets OpenCV size the kernel from it.
            Cv2.GaussianBlur(mask, mask, new Size(0, 0), feather / 2, feather / 2);
        }

        mask.AsSpan<byte>().CopyTo(coverage);
        return coverage;
    }

    public byte[] Full(int canvasWidth, int canvasHeight)
    {
        ValidateCanvas(canvasWidth, canvasHeight);

        var coverage = new byte[checked(canvasWidth * canvasHeight)];
        Array.Fill(coverage, (byte)255);
        return coverage;
    }

    public PixelBuffer BlendThrough(PixelBuffer adjusted, PixelBuffer original, byte[] coverage)
    {
        ArgumentNullException.ThrowIfNull(adjusted);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(coverage);
        if (!adjusted.SameSizeAs(original)) throw new ArgumentException("Buffers must be the same size.", nameof(original));
        if (coverage.Length != original.PixelCount) throw new ArgumentException("Coverage must match the canvas size.", nameof(coverage));

        // MIGRATION: CIBlendWithMask -> a coverage lerp. Alpha is straight, so all four channels mix
        // on their own; coverage 255 keeps the adjusted pixel whole and 0 keeps the original.
        var result = new PixelBuffer(original.Width, original.Height);
        byte[] dst = result.Data;
        byte[] under = original.Data;
        byte[] over = adjusted.Data;
        for (int p = 0; p < coverage.Length; p++)
        {
            int i = p * 4;
            double amount = coverage[p] / 255.0;
            for (int c = 0; c < 4; c++)
            {
                dst[i + c] = (byte)Math.Round(under[i + c] + ((over[i + c] - under[i + c]) * amount), MidpointRounding.AwayFromZero);
            }
        }

        return result;
    }

    private static void ValidateCanvas(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
    }
}
