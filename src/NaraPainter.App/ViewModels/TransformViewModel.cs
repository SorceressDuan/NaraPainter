using NaraPainter.App.Services;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// Crop, rotate, flip and resize.
/// </summary>
/// <remarks>
/// The first three act on the selected layer, because a layer keeps its own buffer size and only meets
/// the canvas when it is composited. Resizing is the canvas: it produces a new document with every
/// layer resampled, which is why it goes through a document swap rather than a pixel edit.
/// </remarks>
public sealed class TransformViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;

    internal TransformViewModel(DocumentViewModel owner) => _owner = owner;

    private LayerViewModel? Layer => _owner.SelectedLayer;

    public bool CanTransform => Layer?.Model.Pixels is not null;

    public bool CanChangeCanvasSize => _owner.Layers.Any(layer => layer.Model.Pixels is not null);

    /// <summary>Crops the selected layer. Coordinates are in that layer's own pixels.</summary>
    public void Crop(int x, int y, int width, int height)
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;

        PixelBuffer after = Geometry.Crop(source, x, y, width, height);
        Commit(layer, Strings.UndoCropLayer, source, after, $"{after.Width} × {after.Height}");
    }

    /// <summary>Crops to the selection, which is expressed in canvas coordinates.</summary>
    public void CropToSelection()
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;
        if (!_owner.Selection.HasRegion) return;

        int x = (int)Math.Round(_owner.Selection.X);
        int y = (int)Math.Round(_owner.Selection.Y);
        int width = (int)Math.Round(_owner.Selection.Width);
        int height = (int)Math.Round(_owner.Selection.Height);

        PixelBuffer after = Geometry.Crop(source, x, y, width, height);
        Commit(layer, Strings.UndoCropLayer, source, after, $"{after.Width} × {after.Height}");
    }

    public void Rotate(QuarterTurn turn)
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;

        PixelBuffer after = Geometry.RotateQuarter(source, turn);
        string label = turn == QuarterTurn.Clockwise ? Strings.UndoRotateRight : Strings.UndoRotateLeft;
        Commit(layer, label, source, after, $"{after.Width} × {after.Height}");
    }

    public void Flip(FlipAxis axis)
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;

        PixelBuffer after = Geometry.Flip(source, axis);
        string label = axis == FlipAxis.Horizontal ? Strings.UndoFlipHorizontal : Strings.UndoFlipVertical;
        Commit(layer, label, source, after, $"{source.Width} × {source.Height}");
    }

    /// <summary>Gaussian blur, in pixels of radius. Zero leaves the layer alone.</summary>
    public void GaussianBlur(double radius)
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;
        if (radius <= 0) return;

        PixelBuffer after = Filters.GaussianBlur(source, radius);
        Commit(layer, Strings.UndoGaussianBlur, source, after, $"{source.Width} × {source.Height}");
    }

    /// <summary>Unsharp mask. <paramref name="amount"/> is how much of the difference is added back.</summary>
    public void Sharpen(double radius, double amount = 1.0, int threshold = 0)
    {
        if (Layer is not { } layer || layer.Source is not { } source) return;
        if (radius <= 0 || amount <= 0) return;

        PixelBuffer after = Filters.UnsharpMask(source, radius, amount, threshold);
        Commit(layer, Strings.UndoSharpen, source, after, $"{source.Width} × {source.Height}");
    }

    /// <summary>
    /// Resamples the canvas. The document is replaced, so undo swaps the whole thing back, which also
    /// restores every layer's own size rather than only the one on screen.
    /// </summary>
    public void ResizeCanvas(int width, int height, bool smooth = true)
    {
        if (width <= 0 || height <= 0) return;
        if (width == _owner.Document.Width && height == _owner.Document.Height) return;

        CanvasDocument before = _owner.Document;
        CanvasDocument after = Resampled(before, width, height, smooth);
        if (ReferenceEquals(before, after)) return;

        _owner.History.Push(new DelegateAction(
            Strings.UndoResizeCanvas,
            () => _owner.SwapDocument(before),
            () => _owner.SwapDocument(after)));

        _owner.SwapDocument(after);
        _owner.Status = Localization.Interpolate(Strings.StatusResized, $"{width} × {height}");
    }

    private static CanvasDocument Resampled(CanvasDocument source, int width, int height, bool smooth)
    {
        var resized = new CanvasDocument(width, height);
        foreach (Layer layer in source.Layers)
        {
            Layer copy = layer.Clone();
            copy.Pixels = layer.Pixels is null ? null : Geometry.Resize(layer.Pixels, width, height, smooth);
            copy.Mask = layer.Mask is null ? null : Geometry.Resize(Channel(layer.Mask, source.Width, source.Height), width, height, smooth).Data;
            resized.Add(copy);
        }

        return resized;
    }

    /// <summary>A mask is one byte per pixel; the resampler works on four, so it is widened and narrowed again.</summary>
    private static PixelBuffer Channel(byte[] mask, int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        int count = Math.Min(mask.Length, width * height);
        for (int i = 0; i < count; i++)
        {
            int at = i * 4;
            buffer.Data[at] = mask[i];
            buffer.Data[at + 3] = 255;
        }

        return buffer;
    }

    private void Commit(LayerViewModel layer, string label, PixelBuffer before, PixelBuffer after, string size)
    {
        layer.ReplacePixels(after);
        _owner.History.Push(new PropertyChange<PixelBuffer>(label, before, after, layer.ReplacePixels));
        _owner.NotifyChanged();
        _owner.Status = Localization.Interpolate(Strings.StatusTransformed, label, size);
        OnPropertyChanged(nameof(CanTransform));
    }
}
