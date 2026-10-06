using Compositor.Models.Blending;
using Compositor.Models.Documents;
using Compositor.Models.Layers;
using Compositor.Models.Pixels;

namespace Compositor.Compositing.Rendering;

/// <summary>
/// The software path. Every pixel here comes out of <see cref="BlendCompositor"/>, so a GPU failure
/// cannot change what the user sees: the fallback and the reference implementation are the same code,
/// and the pixels are byte for byte what an export would produce.
/// </summary>
public static class CpuCompositor
{
    /// <summary>
    /// Flattens the whole stack bottom up, adjustment layers included. This is
    /// <see cref="CanvasDocument.Flatten"/> on purpose: duplicating the loop here is how the two
    /// paths would drift apart.
    /// </summary>
    public static PixelBuffer Render(CanvasDocument document, Func<PixelBuffer, Layer, PixelBuffer>? adjustmentRunner = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.Flatten(adjustmentRunner);
    }

    /// <summary>One layer over a backdrop. Alpha is straight and opacity is 0-1.</summary>
    public static PixelBuffer Composite(PixelBuffer backdrop, PixelBuffer source, BlendMode mode, double opacity = 1, byte[]? coverage = null)
        => BlendCompositor.Composite(backdrop, source, mode, opacity, coverage);

    /// <summary>
    /// True when the stack cannot be composited layer by layer on the GPU. An adjustment layer
    /// rewrites everything below it with per-pixel math, which is CPU work by definition, so those
    /// documents take the whole-document path.
    /// </summary>
    public static bool NeedsFullDocumentPass(CanvasDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        foreach (Layer layer in document.Layers)
        {
            if (layer.IsVisible && layer.IsAdjustment) return true;
        }
        return false;
    }
}
