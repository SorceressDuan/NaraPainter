using NaraDreamPainter.Models.Pixels;

namespace NaraDreamPainter.Models.Services;

public enum ImageFileFormat
{
    Png,
    Jpeg,
    WebP,
    Bmp,
    Tiff
}

/// <summary>
/// TIFF compression schemes this build can write. All of them are lossless; lossy JPEG-in-TIFF is
/// deliberately not offered, see docs/modules/EXPORT.md.
/// </summary>
public enum TiffCompression
{
    None,
    Lzw,
    Deflate,
    PackBits
}

/// <summary>
/// A caller that passes only the format gets the same bytes this call produced before quality and
/// compression existed: the encoders were already running at quality 90 and at LZW for TIFF.
/// <paramref name="Quality"/> is 1-100 and only reaches formats that have a quality setting,
/// <paramref name="Compression"/> is read by TIFF alone.
/// </summary>
public sealed record ImageSaveOptions(
    ImageFileFormat Format,
    int Quality = 90,
    TiffCompression Compression = TiffCompression.Lzw);

/// <summary>
/// What a format can actually encode, so the export UI offers only options that do something.
/// The codec answers these from the same facts it uses while writing, not from a second table.
/// </summary>
/// <param name="Extension">Canonical extension without the dot; "jpg" for <see cref="ImageFileFormat.Jpeg"/>.</param>
/// <param name="DefaultQuality">Value a quality control starts at, zero when the format has no quality setting.</param>
/// <param name="Compressions">Schemes a TIFF write honours, empty for every other format.</param>
public sealed record ImageFormatCapabilities(
    ImageFileFormat Format,
    string Extension,
    bool SupportsAlpha,
    bool SupportsQuality,
    int DefaultQuality,
    IReadOnlyList<TiffCompression> Compressions)
{
    public bool SupportsCompression => Compressions.Count > 0;
}

/// <summary>
/// Reads and writes image files. The implementation lives in NaraDreamPainter.Imaging, which owns the
/// OpenCvSharp dependency; nothing above this interface should know about Mat.
/// </summary>
public interface IImageCodec
{
    /// <summary>File extensions the build can actually encode, lower case and without the dot.</summary>
    IReadOnlyList<string> SupportedReadExtensions { get; }

    IReadOnlyList<string> SupportedWriteExtensions { get; }

    /// <summary>One entry per writable format, carrying its canonical extension, for a format picker.</summary>
    IReadOnlyList<ImageFormatCapabilities> WriteFormats { get; }

    /// <summary>Decodes a file into straight-alpha RGBA. Throws on unreadable files.</summary>
    PixelBuffer Read(string path);

    void Write(PixelBuffer pixels, string path, ImageSaveOptions options);

    ImageFileFormat FormatFromPath(string path);

    /// <summary>What the format keeps and which parameters reach its encoder.</summary>
    ImageFormatCapabilities Capabilities(ImageFileFormat format);
}
