using NaraPainter.Models.Pixels;

namespace NaraPainter.Imaging.Services;

/// <summary>Which way a quarter turn goes.</summary>
public enum QuarterTurn
{
    Clockwise,
    CounterClockwise
}

/// <summary>Which axis a mirror uses.</summary>
public enum FlipAxis
{
    Horizontal,
    Vertical
}

/// <summary>
/// Crop, rotate, flip and resample for a single pixel buffer.
/// </summary>
/// <remarks>
/// Every operation returns a new buffer and leaves its input alone, because the caller keeps the
/// original to undo with. Sizes are clamped rather than rejected: a crop rectangle dragged past the
/// edge is a normal thing for a user to do, and the sensible reading is "as much as there is".
/// </remarks>
public static class Geometry
{
    public static PixelBuffer Crop(PixelBuffer source, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(source);

        int left = Math.Clamp(x, 0, source.Width - 1);
        int top = Math.Clamp(y, 0, source.Height - 1);
        int right = Math.Clamp(x + width, left + 1, source.Width);
        int bottom = Math.Clamp(y + height, top + 1, source.Height);

        var cropped = new PixelBuffer(right - left, bottom - top);
        for (int row = 0; row < cropped.Height; row++)
        {
            int from = source.Offset(left, top + row);
            Array.Copy(source.Data, from, cropped.Data, cropped.Offset(0, row), cropped.Width * 4);
        }

        return cropped;
    }

    /// <summary>A quarter turn, which swaps the two dimensions.</summary>
    public static PixelBuffer RotateQuarter(PixelBuffer source, QuarterTurn turn)
    {
        ArgumentNullException.ThrowIfNull(source);

        var rotated = new PixelBuffer(source.Height, source.Width);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int targetX = turn == QuarterTurn.Clockwise ? source.Height - 1 - y : y;
                int targetY = turn == QuarterTurn.Clockwise ? x : source.Width - 1 - x;
                Copy(source, x, y, rotated, targetX, targetY);
            }
        }

        return rotated;
    }

    public static PixelBuffer Flip(PixelBuffer source, FlipAxis axis)
    {
        ArgumentNullException.ThrowIfNull(source);

        var flipped = new PixelBuffer(source.Width, source.Height);
        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                int targetX = axis == FlipAxis.Horizontal ? source.Width - 1 - x : x;
                int targetY = axis == FlipAxis.Vertical ? source.Height - 1 - y : y;
                Copy(source, x, y, flipped, targetX, targetY);
            }
        }

        return flipped;
    }

    /// <summary>
    /// Resamples to a new size. <paramref name="smooth"/> picks bilinear over nearest neighbour: a
    /// shrink wants the average of what it drops, while a zoom of pixel art wants the block.
    /// </summary>
    public static PixelBuffer Resize(PixelBuffer source, int width, int height, bool smooth = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        if (source.Width == width && source.Height == height) return source.Clone();

        var resized = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (!smooth)
                {
                    Copy(source, x * source.Width / width, y * source.Height / height, resized, x, y);
                    continue;
                }

                SampleBilinear(source, (x + 0.5) * source.Width / width - 0.5, (y + 0.5) * source.Height / height - 0.5, resized, x, y);
            }
        }

        return resized;
    }

    /// <summary>Keeps the longer side inside the box, preserving the aspect ratio.</summary>
    public static (int Width, int Height) FitInside(int width, int height, int maxWidth, int maxHeight)
    {
        if (width <= 0 || height <= 0 || maxWidth <= 0 || maxHeight <= 0) return (Math.Max(1, width), Math.Max(1, height));

        double scale = Math.Min((double)maxWidth / width, (double)maxHeight / height);
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static void SampleBilinear(PixelBuffer source, double x, double y, PixelBuffer target, int targetX, int targetY)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;

        // Outside the source counts as transparent, which keeps a shrink from smearing the edge pixel
        // outwards the way clamping does.
        double[] sum = new double[4];
        for (int corner = 0; corner < 4; corner++)
        {
            int sx = x0 + (corner & 1);
            int sy = y0 + (corner >> 1);
            double weight = ((corner & 1) == 0 ? 1 - fx : fx) * ((corner >> 1) == 0 ? 1 - fy : fy);
            if (weight == 0) continue;
            if ((uint)sx >= (uint)source.Width || (uint)sy >= (uint)source.Height) continue;

            int i = ((sy * source.Width) + sx) * 4;
            sum[0] += source.Data[i] * weight;
            sum[1] += source.Data[i + 1] * weight;
            sum[2] += source.Data[i + 2] * weight;
            sum[3] += source.Data[i + 3] * weight;
        }

        int to = ((targetY * target.Width) + targetX) * 4;
        for (int channel = 0; channel < 4; channel++) target.Data[to + channel] = (byte)Math.Clamp(Math.Round(sum[channel]), 0, 255);
    }

    private static void Copy(PixelBuffer source, int x, int y, PixelBuffer target, int targetX, int targetY)
    {
        if ((uint)x >= (uint)source.Width || (uint)y >= (uint)source.Height) return;

        int from = ((y * source.Width) + x) * 4;
        int to = ((targetY * target.Width) + targetX) * 4;
        target.Data[to] = source.Data[from];
        target.Data[to + 1] = source.Data[from + 1];
        target.Data[to + 2] = source.Data[from + 2];
        target.Data[to + 3] = source.Data[from + 3];
    }
}
