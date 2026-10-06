using NaraPainter.Models.Documents;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;

namespace NaraPainter.App.Services;

/// <summary>
/// Turns decoded pixels into document pieces. A file opened on its own becomes a canvas sized to the
/// image; a file dropped onto an open document becomes one more layer, scaled to the canvas.
/// </summary>
public sealed class ImageImporter
{
    private readonly IImageCodec _codec;

    public ImageImporter(IImageCodec codec) => _codec = codec;

    public CanvasDocument ReadDocument(string path)
    {
        PixelBuffer pixels = _codec.Read(path);
        var document = new CanvasDocument(pixels.Width, pixels.Height) { FilePath = path };
        document.Add(new Layer(NameFor(path)) { Pixels = pixels });
        document.IsDirty = false;
        return document;
    }

    public Layer ReadLayer(string path)
    {
        PixelBuffer pixels = _codec.Read(path);
        return new Layer(NameFor(path)) { Pixels = pixels };
    }

    private static string NameFor(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(name) ? Strings.LayersDefaultName : name;
    }
}
