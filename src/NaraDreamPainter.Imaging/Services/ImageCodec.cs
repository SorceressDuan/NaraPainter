using NaraDreamPainter.Models.Pixels;
using NaraDreamPainter.Models.Services;
using OpenCvSharp;

namespace NaraDreamPainter.Imaging.Services;

public sealed class ImageCodec : IImageCodec
{
    private static readonly string[] Extensions = ["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"];

    private static readonly TiffCompression[] TiffSchemes =
        [TiffCompression.None, TiffCompression.Lzw, TiffCompression.Deflate, TiffCompression.PackBits];

    private static readonly ImageFormatCapabilities[] Formats =
    [
        new(ImageFileFormat.Png, "png", SupportsAlpha: true, SupportsQuality: false, DefaultQuality: 0, []),
        new(ImageFileFormat.Jpeg, "jpg", SupportsAlpha: false, SupportsQuality: true, DefaultQuality: 90, []),
        new(ImageFileFormat.WebP, "webp", SupportsAlpha: true, SupportsQuality: true, DefaultQuality: 90, []),
        new(ImageFileFormat.Bmp, "bmp", SupportsAlpha: true, SupportsQuality: false, DefaultQuality: 0, []),
        new(ImageFileFormat.Tiff, "tif", SupportsAlpha: true, SupportsQuality: false, DefaultQuality: 0, TiffSchemes)
    ];

    public IReadOnlyList<string> SupportedReadExtensions => Extensions;

    public IReadOnlyList<string> SupportedWriteExtensions => Extensions;

    public IReadOnlyList<ImageFormatCapabilities> WriteFormats => Formats;

    public PixelBuffer Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("The image file does not exist.", path);

        // MIGRATION: CGImageSourceCreateImageAtIndex -> Cv2.ImRead. Unchanged keeps whatever channel
        // count the file carries, so PNG/WebP alpha survives instead of flattening to three channels.
        // OpenCV applies the EXIF orientation by itself here, which the macOS importer did by hand.
        using var mat = Cv2.ImRead(path, ImreadModes.Unchanged);
        if (mat.Empty()) throw new InvalidDataException($"Could not decode '{path}'.");
        return OpenCvInterop.ToPixelBuffer(mat);
    }

    public void Write(PixelBuffer pixels, string path, ImageSaveOptions options)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));

        // OpenCV picks the encoder from the extension, so a disagreement would write a different
        // format than the caller asked for and hand the quality parameter to the wrong encoder.
        ImageFileFormat format = FormatFromPath(path);
        if (format != options.Format)
        {
            throw new ArgumentException($"The path is {format} but the save options say {options.Format}.", nameof(path));
        }

        ImageFormatCapabilities capabilities = Capabilities(format);
        using var mat = OpenCvInterop.ToMat(pixels);
        using var flattened = capabilities.SupportsAlpha ? null : new Mat();
        if (flattened is not null) Cv2.CvtColor(mat, flattened, ColorConversionCodes.BGRA2BGR);

        // MIGRATION: CGImageDestination / NSBitmapImageRep -> Cv2.ImWrite.
        if (!Cv2.ImWrite(path, flattened ?? mat, Parameters(format, options)))
        {
            throw new IOException($"Could not write '{path}'.");
        }
    }

    public ImageFileFormat FormatFromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));

        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return extension switch
        {
            "png" => ImageFileFormat.Png,
            "jpg" or "jpeg" => ImageFileFormat.Jpeg,
            "webp" => ImageFileFormat.WebP,
            "bmp" => ImageFileFormat.Bmp,
            "tif" or "tiff" => ImageFileFormat.Tiff,
            _ => throw new NotSupportedException($"'{extension}' is not a supported image format.")
        };
    }

    public ImageFormatCapabilities Capabilities(ImageFileFormat format)
    {
        foreach (ImageFormatCapabilities candidate in Formats)
        {
            if (candidate.Format == format) return candidate;
        }

        throw new NotSupportedException($"'{format}' is not a format this build can write.");
    }

    private static ImageEncodingParam[] Parameters(ImageFileFormat format, ImageSaveOptions options)
    {
        int quality = Math.Clamp(options.Quality, 1, 100);
        return format switch
        {
            ImageFileFormat.Jpeg => [new ImageEncodingParam(ImwriteFlags.JpegQuality, quality)],
            ImageFileFormat.WebP => [new ImageEncodingParam(ImwriteFlags.WebPQuality, quality)],
            ImageFileFormat.Tiff => [new ImageEncodingParam(ImwriteFlags.TiffCompression, TiffConstant(options.Compression))],
            _ => []
        };
    }

    // libtiff reads the scheme as a bare constant out of IMWRITE_TIFF_COMPRESSION, and OpenCvSharp has
    // no enum for it. Adding the parameter is a no-op for a bare ImageSaveOptions because libtiff
    // compresses TIFF with LZW when nothing says otherwise, which is what TiffCompression.Lzw maps to.
    private static int TiffConstant(TiffCompression compression) => compression switch
    {
        TiffCompression.None => 1,
        TiffCompression.Lzw => 5,
        TiffCompression.Deflate => 8,
        TiffCompression.PackBits => 32773,
        _ => throw new NotSupportedException($"'{compression}' is not a TIFF compression scheme.")
    };
}
