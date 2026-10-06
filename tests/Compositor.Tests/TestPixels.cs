using Compositor.Models.Pixels;
using Xunit;

namespace Compositor.Tests;

internal static class TestPixels
{
    internal const double Tolerance = 1.0 / 255.0;

    internal static PixelBuffer One(byte r, byte g, byte b, byte a) => new(1, 1, [r, g, b, a]);

    internal static PixelBuffer Solid(int width, int height, byte r, byte g, byte b, byte a)
    {
        var buffer = new PixelBuffer(width, height);
        for (int i = 0; i < buffer.Data.Length; i += 4)
        {
            buffer.Data[i] = r;
            buffer.Data[i + 1] = g;
            buffer.Data[i + 2] = b;
            buffer.Data[i + 3] = a;
        }
        return buffer;
    }

    internal static (double R, double G, double B, double A) Unit(PixelBuffer buffer, int x = 0, int y = 0)
    {
        Rgba32 pixel = buffer[x, y];
        return (pixel.R / 255.0, pixel.G / 255.0, pixel.B / 255.0, pixel.A / 255.0);
    }

    internal static void AssertClose(double expected, double actual, string what) =>
        Assert.True(Math.Abs(expected - actual) <= Tolerance,
            $"{what}: expected {expected:0.####} ({expected * 255:0.##}/255), got {actual:0.####} ({actual * 255:0.##}/255)");
}
