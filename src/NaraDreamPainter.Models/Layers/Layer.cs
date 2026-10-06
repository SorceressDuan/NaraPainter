using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Blending;
using NaraDreamPainter.Models.Pixels;

namespace NaraDreamPainter.Models.Layers;

/// <summary>
/// One raster layer or adjustment layer. Layers are stored bottom first, so the array order in
/// <see cref="Documents.CanvasDocument"/> is also the stacking order.
/// </summary>
public sealed class Layer
{
    public Layer(string name)
    {
        Name = string.IsNullOrWhiteSpace(name) ? "Layer" : name;
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; }

    public bool IsVisible { get; set; } = true;

    /// <summary>0 to 1. Dims the layer's own pixels, blend included.</summary>
    public double Opacity { get; set; } = 1;

    public BlendMode BlendMode { get; set; } = BlendMode.Normal;

    public PixelBuffer? Pixels { get; set; }

    /// <summary>
    /// Per-pixel coverage at document resolution: 255 keeps the layer, 0 hides it. Null means the
    /// layer is unmasked, which is not the same as an all-black mask.
    /// </summary>
    public byte[]? Mask { get; set; }

    /// <summary>Set on layers that re-run an adjustment over the layers below instead of holding pixels.</summary>
    public AdjustmentSettings? Adjustment { get; set; }

    public bool IsAdjustment => Adjustment is not null;

    /// <summary>True once the layer has pixels to draw.</summary>
    public bool HasContent => Pixels is not null;

    public Layer Clone() => new(Name)
    {
        IsVisible = IsVisible,
        Opacity = Opacity,
        BlendMode = BlendMode,
        Pixels = Pixels?.Clone(),
        Mask = Mask is null ? null : (byte[])Mask.Clone(),
        Adjustment = Adjustment?.Clone()
    };

    /// <summary>
    /// The coverage to composite this layer through, or null when nothing restricts it. Layer opacity
    /// is applied separately by the compositor, so it does not gate the mask here. A mask whose size
    /// does not match the document is ignored rather than resampled.
    /// </summary>
    public byte[]? CoverageFor(int width, int height)
    {
        return Mask is not null && Mask.Length == width * height ? Mask : null;
    }
}
