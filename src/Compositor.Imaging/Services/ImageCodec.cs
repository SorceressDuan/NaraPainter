using Compositor.Models.Pixels;
using Compositor.Models.Services;
using OpenCvSharp;

namespace Compositor.Imaging.Services;

public sealed class ImageCodec : IImageCodec
{
    private static readonly string[] Extensions = ["png", "jpg", "jpeg", "webp", "bmp", "tif", "tiff"];

    public IReadOnlyList<string> SupportedReadExtensions => Extensions;

    public IReadOnlyList<string> SupportedWriteExtensions => Extensions;

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

        using var mat = OpenCvInterop.ToMat(pixels);
        using var opaque = format == ImageFileFormat.Jpeg ? new Mat() : null;
        if (opaque is not null) Cv2.CvtColor(mat, opaque, ColorConversionCodes.BGRA2BGR);

        // MIGRATION: CGImageDestination / NSBitmapImageRep -> Cv2.ImWrite.
        if (!Cv2.ImWrite(path, opaque ?? mat, Parameters(format, options.Quality)))
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

    private static ImageEncodingParam[] Parameters(ImageFileFormat format, int quality)
    {
        int level = Math.Clamp(quality, 1, 100);
        return format switch
        {
            ImageFileFormat.Jpeg => [new ImageEncodingParam(ImwriteFlags.JpegQuality, level)],
            ImageFileFormat.WebP => [new ImageEncodingParam(ImwriteFlags.WebPQuality, level)],
            _ => []
        };
    }
}
