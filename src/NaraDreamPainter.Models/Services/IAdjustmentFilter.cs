using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Pixels;

namespace NaraDreamPainter.Models.Services;

/// <summary>
/// Runs adjustments over pixels. Used both by the CanvasRenderer when flattening a document with
/// adjustment layers and by the properties panel for its live preview.
/// </summary>
public interface IAdjustmentFilter
{
    /// <summary>Applies one adjustment in place and returns the result as a new buffer.</summary>
    PixelBuffer Apply(PixelBuffer source, AdjustmentSettings settings);

    /// <summary>Applies a chain in order; the preview uses this to show a layer's whole stack at once.</summary>
    PixelBuffer ApplyAll(PixelBuffer source, IEnumerable<AdjustmentSettings> settings);
}
