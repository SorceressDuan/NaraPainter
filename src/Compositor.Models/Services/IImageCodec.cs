using Compositor.Models.Pixels;

namespace Compositor.Models.Services;

public enum ImageFileFormat
{
    Png,
    Jpeg,
    WebP,
    Bmp,
    Tiff
}

public sealed record ImageSaveOptions(ImageFileFormat Format, int Quality = 90);

/// <summary>
/// Reads and writes image files. The implementation lives in Compositor.Imaging, which owns the
/// OpenCvSharp dependency; nothing above this interface should know about Mat.
/// </summary>
public interface IImageCodec
{
    /// <summary>File extensions the build can actually encode, lower case and without the dot.</summary>
    IReadOnlyList<string> SupportedReadExtensions { get; }

    IReadOnlyList<string> SupportedWriteExtensions { get; }

    /// <summary>Decodes a file into straight-alpha RGBA. Throws on unreadable files.</summary>
    PixelBuffer Read(string path);

    void Write(PixelBuffer pixels, string path, ImageSaveOptions options);

    ImageFileFormat FormatFromPath(string path);
}
