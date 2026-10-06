using NaraPainter.Models.Blending;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;

namespace NaraPainter.Models.Documents;

/// <summary>
/// A canvas and its layers. The layer list is bottom first, so index 0 is the layer drawn first
/// and the last entry is the one the user sees on top.
/// </summary>
public sealed class CanvasDocument
{
    private readonly List<Layer> _layers = [];

    public CanvasDocument(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        Width = width;
        Height = height;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public int Width { get; }

    public int Height { get; }

    public string? FilePath { get; set; }

    public bool IsDirty { get; set; }

    public IReadOnlyList<Layer> Layers => _layers;

    /// <summary>The top layer, which is what the properties panel edits.</summary>
    public Layer? ActiveLayer => _layers.Count == 0 ? null : _layers[^1];

    public int IndexOf(Layer layer) => _layers.IndexOf(layer);

    public bool CanMove(Layer layer) => _layers.Contains(layer);

    /// <summary>Adds a layer on top and returns it.</summary>
    public Layer Add(Layer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        _layers.Add(layer);
        IsDirty = true;
        return layer;
    }

    public Layer AddBlank(string name)
    {
        var layer = new Layer(name) { Pixels = new PixelBuffer(Width, Height) };
        return Add(layer);
    }

    public bool Remove(Layer layer)
    {
        if (!_layers.Remove(layer)) return false;
        IsDirty = true;
        return true;
    }

    /// <summary>
    /// Moves a layer to a new index in the stack. <paramref name="newIndex"/> is clamped, so callers
    /// can pass a drop target without checking the range first.
    /// </summary>
    public bool Move(Layer layer, int newIndex)
    {
        int current = _layers.IndexOf(layer);
        if (current < 0) return false;

        int target = Math.Clamp(newIndex, 0, _layers.Count - 1);
        if (target == current) return false;

        _layers.RemoveAt(current);
        _layers.Insert(target, layer);
        IsDirty = true;
        return true;
    }

    public bool MoveUp(Layer layer) => Move(layer, _layers.IndexOf(layer) + 1);

    public bool MoveDown(Layer layer) => Move(layer, _layers.IndexOf(layer) - 1);

    /// <summary>
    /// Flattens the stack into one buffer by compositing bottom up. Adjustment layers run over
    /// whatever has been drawn so far, which is what makes them affect the layers below them.
    /// </summary>
    public PixelBuffer Flatten(Func<PixelBuffer, Layer, PixelBuffer>? adjustmentRunner = null)
    {
        var canvas = new PixelBuffer(Width, Height);
        foreach (var layer in _layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0) continue;

            PixelBuffer contribution;
            if (layer.IsAdjustment)
            {
                if (adjustmentRunner is null) continue;
                contribution = adjustmentRunner(canvas, layer);
            }
            else
            {
                contribution = layer.Pixels is null
                    ? new PixelBuffer(Width, Height)
                    : Fit(layer.Pixels);
            }

            canvas = BlendCompositor.Composite(canvas, contribution, layer.BlendMode, layer.Opacity, layer.CoverageFor(Width, Height));
        }
        return canvas;
    }

    /// <summary>
    /// Places a layer's pixels on a document-sized buffer. Layers are stored at their own size and
    /// only stretch to the canvas when they came from a smaller image.
    /// </summary>
    public PixelBuffer Fit(PixelBuffer source)
    {
        if (source.Width == Width && source.Height == Height) return source;

        var fitted = new PixelBuffer(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            int sourceY = source.Height == Height ? y : (int)((long)y * source.Height / Height);
            for (int x = 0; x < Width; x++)
            {
                int sourceX = source.Width == Width ? x : (int)((long)x * source.Width / Width);
                int from = source.Offset(sourceX, sourceY);
                int to = fitted.Offset(x, y);
                fitted.Data[to] = source.Data[from];
                fitted.Data[to + 1] = source.Data[from + 1];
                fitted.Data[to + 2] = source.Data[from + 2];
                fitted.Data[to + 3] = source.Data[from + 3];
            }
        }
        return fitted;
    }
}
