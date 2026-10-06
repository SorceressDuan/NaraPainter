namespace NaraPainter.Models.Pixels;

/// <summary>
/// An 8-bit RGBA raster with straight (non-premultiplied) alpha.
/// </summary>
public sealed class PixelBuffer
{
    public PixelBuffer(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
        Data = new byte[checked(width * height * 4)];
    }

    public PixelBuffer(int width, int height, byte[] data)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length != width * height * 4) throw new ArgumentException("Buffer size does not match the dimensions.", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }

    public int Height { get; }

    public byte[] Data { get; }

    public int PixelCount => Width * Height;

    /// <summary>Reads one pixel. Out-of-range coordinates come back fully transparent.</summary>
    public Rgba32 this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return default;
            int i = ((y * Width) + x) * 4;
            return new Rgba32(Data[i], Data[i + 1], Data[i + 2], Data[i + 3]);
        }
    }

    public PixelBuffer Clone() => new(Width, Height, (byte[])Data.Clone());

    public bool SameSizeAs(PixelBuffer other) => other is not null && Width == other.Width && Height == other.Height;

    /// <summary>Index of the first byte of a pixel, for direct buffer work.</summary>
    public int Offset(int x, int y) => ((y * Width) + x) * 4;
}
