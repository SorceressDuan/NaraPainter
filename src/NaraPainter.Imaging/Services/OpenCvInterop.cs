using System.Runtime.InteropServices;
using NaraPainter.Models.Pixels;
using OpenCvSharp;

namespace NaraPainter.Imaging.Services;

/// <summary>
/// Moves pixels between <see cref="PixelBuffer"/> (straight-alpha RGBA) and OpenCV's <see cref="Mat"/>.
/// </summary>
internal static class OpenCvInterop
{
    // MIGRATION: CGImage/CGContext raw buffers -> Mat. OpenCV keeps colour channels in BGR/BGRA
    // order, PixelBuffer is RGBA, so every crossing swaps channels by hand. CvtColor would need a
    // promise about the channel count that a file on disk does not make.
    public static PixelBuffer ToPixelBuffer(Mat mat)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (mat.Empty()) throw new ArgumentException("The Mat has no pixels.", nameof(mat));

        Mat? copy = null;
        try
        {
            Mat readable = mat;
            if (mat.Depth() != MatType.CV_8U || !mat.IsContinuous())
            {
                copy = Materialize(mat);
                readable = copy;
            }

            int width = readable.Width;
            int height = readable.Height;
            int channels = readable.Channels();
            int count = checked(width * height);

            var raw = new byte[checked(count * channels)];
            Marshal.Copy(readable.Data, raw, 0, raw.Length);

            var rgba = new byte[checked(count * 4)];
            switch (channels)
            {
                case 1:
                    for (int p = 0; p < count; p++)
                    {
                        byte gray = raw[p];
                        int o = p * 4;
                        rgba[o] = gray;
                        rgba[o + 1] = gray;
                        rgba[o + 2] = gray;
                        rgba[o + 3] = 255;
                    }
                    break;
                case 3:
                    for (int i = 0, o = 0; i < raw.Length; i += 3, o += 4)
                    {
                        rgba[o] = raw[i + 2];
                        rgba[o + 1] = raw[i + 1];
                        rgba[o + 2] = raw[i];
                        rgba[o + 3] = 255;
                    }
                    break;
                case 4:
                    for (int i = 0, o = 0; i < raw.Length; i += 4, o += 4)
                    {
                        rgba[o] = raw[i + 2];
                        rgba[o + 1] = raw[i + 1];
                        rgba[o + 2] = raw[i];
                        rgba[o + 3] = raw[i + 3];
                    }
                    break;
                default:
                    throw new NotSupportedException($"A {channels}-channel image cannot be mapped to RGBA.");
            }

            return new PixelBuffer(width, height, rgba);
        }
        finally
        {
            copy?.Dispose();
        }
    }

    public static Mat ToMat(PixelBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var mat = new Mat(buffer.Height, buffer.Width, MatType.CV_8UC4);
        Span<byte> bgra = mat.AsSpan<byte>();
        byte[] rgba = buffer.Data;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            bgra[i] = rgba[i + 2];
            bgra[i + 1] = rgba[i + 1];
            bgra[i + 2] = rgba[i];
            bgra[i + 3] = rgba[i + 3];
        }

        return mat;
    }

    /// <summary>An 8-bit continuous copy, for Mats that are padded or deeper than a byte per channel.</summary>
    private static Mat Materialize(Mat mat)
    {
        var eightBit = new Mat();
        if (mat.Depth() == MatType.CV_8U)
        {
            mat.CopyTo(eightBit);
            return eightBit;
        }

        // 16-bit is what a scanned TIFF usually is. Float input is taken as normalised to 0-1,
        // which is the convention the formats that carry it use.
        double scale = mat.Depth() == MatType.CV_16U ? 1.0 / 257.0
            : mat.Depth() == MatType.CV_32F || mat.Depth() == MatType.CV_64F ? 255.0
            : 1.0;
        mat.ConvertTo(eightBit, MatType.CV_8U, scale);
        return eightBit;
    }
}
