using System.Runtime.CompilerServices;
using NaraDreamPainter.Compositing.Blending;
using NaraDreamPainter.Models.Documents;
using NaraDreamPainter.Models.Layers;
using NaraDreamPainter.Models.Pixels;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI;
using Windows.Foundation;
using Windows.Graphics.DirectX;

namespace NaraDreamPainter.Compositing.Rendering;

/// <summary>
/// Draws a <see cref="CanvasDocument"/> with Win2D. Layers are uploaded once into cached bitmaps and
/// composited bottom up through <see cref="BlendEffectFactory"/>; only the part of the document that
/// is on screen is rendered, and the result is kept so panning and zooming reuse it instead of
/// recompositing. When the GPU path cannot run, the document goes through
/// <see cref="CpuCompositor"/> and the pixels match an export exactly.
/// </summary>
public sealed class DocumentRenderer
{
    private const double BitmapDpi = 96;
    private const double PaddingFraction = 0.25;
    private const double MaxPadding = 512;
    private const long MaxRenderPixels = 12_000_000;
    private const double MinScale = 0.02;

    private readonly Dictionary<Guid, LayerCache> _layers = new();
    private readonly Dictionary<Guid, MaskCache> _masks = new();
    private readonly HashSet<Guid> _live = new();
    private readonly List<Guid> _stale = new();

    private CanvasRenderTarget? _front;
    private CanvasRenderTarget? _back;
    private CanvasRenderTarget? _layerTarget;
    private Rect _compositeRegion;
    private double _compositeScale = 1;
    private Guid _compositeDocument;
    private int _compositeSignature = -1;

    private CanvasBitmap? _flatBitmap;
    private Guid _flatDocument;
    private int _flatSignature = -1;

    private double _uploadScale = 1;
    private bool _gpuBlocked;

    /// <summary>
    /// Runs an adjustment layer over everything below it. Null skips adjustment layers, which is what
    /// <see cref="CanvasDocument.Flatten"/> does without a runner.
    /// </summary>
    public Func<PixelBuffer, Layer, PixelBuffer>? AdjustmentRunner { get; set; }

    /// <summary>Skips the GPU path entirely, for tests and for a machine whose driver misbehaves.</summary>
    public bool ForceCpu { get; set; }

    /// <summary>True when the last frame came out of <see cref="CpuCompositor"/>.</summary>
    public bool UsedCpuPath { get; private set; }

    /// <summary>Drops every cached bitmap and render target. Call after the document changed.</summary>
    public void Invalidate()
    {
        ReleaseGpu();
        _flatBitmap?.Dispose();
        _flatBitmap = null;
        _flatSignature = -1;
        _gpuBlocked = false;
    }

    public void Draw(CanvasDrawingSession session, ICanvasResourceCreator resources, CanvasDocument document, ViewTransform view)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(document);

        UsedCpuPath = false;
        Rect visible = view.VisibleDocumentRect(document.Width, document.Height);
        if (visible.Width <= 0 || visible.Height <= 0) return;

        _uploadScale = UploadScaleFor(document, resources.Device);
        int signature = SignatureOf(document);
        if (ForceCpu || _gpuBlocked || CpuCompositor.NeedsFullDocumentPass(document))
        {
            UsedCpuPath = true;
            DrawFlattened(session, resources, document, view, visible, signature);
            return;
        }

        try
        {
            DrawLayers(session, resources, document, view, visible, signature);
        }
        catch (Exception exception)
        {
            // A lost device, a surface the driver refuses, an effect the device will not build: throw
            // away what the GPU produced and paint the same document through BlendCompositor.
            _gpuBlocked = exception is NotSupportedException;
            ReleaseGpu();
            UsedCpuPath = true;
            DrawFlattened(session, resources, document, view, visible, signature);
        }
    }

    private void DrawLayers(CanvasDrawingSession session, ICanvasResourceCreator resources, CanvasDocument document, ViewTransform view, Rect visible, int signature)
    {
        Rect region = Pad(visible, document);
        double scale = ScaleFor(region, resources.Device, view.Zoom);
        bool reuse = _front is not null
            && _compositeSignature == signature
            && _compositeDocument == document.Id
            && _compositeScale == scale
            && Covers(_compositeRegion, visible);

        if (!reuse) RebuildLayers(resources, document, region, scale, signature);
        DrawComposite(session, view, visible);
    }

    /// <summary>
    /// Composites the region into <c>_front</c>. Every layer lands in a freshly cleared target, so no
    /// step depends on whether a render target keeps its contents between drawing sessions; keeping
    /// the whole document out of this and rendering only the padded visible region is what makes
    /// dragging a large document cheap.
    /// </summary>
    private void RebuildLayers(ICanvasResourceCreator resources, CanvasDocument document, Rect region, double scale, int signature)
    {
        int width = Math.Max(1, (int)Math.Ceiling(region.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(region.Height * scale));
        EnsureTargets(resources, width, height);

        var full = new Rect(0, 0, width, height);
        var source = new Rect(
            region.X * _uploadScale,
            region.Y * _uploadScale,
            region.Width * _uploadScale,
            region.Height * _uploadScale);
        var dest = new Rect(0, 0, region.Width * scale, region.Height * scale);
        CanvasImageInterpolation interpolation = scale >= 1 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.Linear;

        using (CanvasDrawingSession session = _front!.CreateDrawingSession())
        {
            session.Clear(Colors.Transparent);
        }

        _live.Clear();
        bool hasBackdrop = false;
        foreach (Layer layer in document.Layers)
        {
            if (!layer.IsVisible || layer.Opacity <= 0) continue;

            CanvasBitmap? bitmap = GetLayerBitmap(resources, document, layer);
            if (bitmap is null) continue;
            _live.Add(layer.Id);

            ICanvasImage contribution = bitmap;
            CanvasBitmap? mask = GetMaskBitmap(resources, document, layer);
            if (mask is not null) contribution = new AlphaMaskEffect { Source = bitmap, AlphaMask = mask };

            if (!hasBackdrop || layer.BlendMode == BlendMode.Normal)
            {
                using (CanvasDrawingSession session = _back!.CreateDrawingSession())
                {
                    session.Clear(Colors.Transparent);
                    if (hasBackdrop) session.DrawImage(_front, full, full);
                    session.DrawImage(contribution, dest, source, (float)layer.Opacity, interpolation);
                }
            }
            else
            {
                CanvasRenderTarget layerTarget = EnsureLayerTarget(resources, width, height);
                using (CanvasDrawingSession session = layerTarget.CreateDrawingSession())
                {
                    session.Clear(Colors.Transparent);
                    session.DrawImage(contribution, dest, source, (float)layer.Opacity, interpolation);
                }

                BlendEffect? effect = BlendEffectFactory.Create(layer.BlendMode, _front, layerTarget)
                    ?? throw new NotSupportedException($"No GPU route for {layer.BlendMode}.");
                using (CanvasDrawingSession session = _back!.CreateDrawingSession())
                {
                    session.Clear(Colors.Transparent);
                    session.DrawImage(effect, full, full);
                }
            }

            (_front, _back) = (_back, _front);
            hasBackdrop = true;
        }

        PruneLayers();
        _compositeRegion = region;
        _compositeScale = scale;
        _compositeDocument = document.Id;
        _compositeSignature = signature;
    }

    private void DrawComposite(CanvasDrawingSession session, ViewTransform view, Rect visible)
    {
        var source = new Rect(
            (visible.X - _compositeRegion.X) * _compositeScale,
            (visible.Y - _compositeRegion.Y) * _compositeScale,
            visible.Width * _compositeScale,
            visible.Height * _compositeScale);
        Point topLeft = view.ToScreen(visible.X, visible.Y);
        var dest = new Rect(topLeft.X, topLeft.Y, visible.Width * view.Zoom, visible.Height * view.Zoom);
        session.DrawImage(_front!, dest, source, 1, SamplingFor(view.Zoom));
    }

    private void DrawFlattened(CanvasDrawingSession session, ICanvasResourceCreator resources, CanvasDocument document, ViewTransform view, Rect visible, int signature)
    {
        if (_flatBitmap is null || _flatSignature != signature || _flatDocument != document.Id)
        {
            PixelBuffer flattened = CpuCompositor.Render(document, AdjustmentRunner);
            CanvasBitmap bitmap = UploadBitmap(resources, ToPremultipliedBgra(flattened), flattened.Width, flattened.Height);
            _flatBitmap?.Dispose();
            _flatBitmap = bitmap;
            _flatSignature = signature;
            _flatDocument = document.Id;
        }

        Point topLeft = view.ToScreen(visible.X, visible.Y);
        var dest = new Rect(topLeft.X, topLeft.Y, visible.Width * view.Zoom, visible.Height * view.Zoom);
        var source = new Rect(
            visible.X * _uploadScale,
            visible.Y * _uploadScale,
            visible.Width * _uploadScale,
            visible.Height * _uploadScale);
        session.DrawImage(_flatBitmap, dest, source, 1, SamplingFor(view.Zoom));
    }

    private CanvasBitmap? GetLayerBitmap(ICanvasResourceCreator resources, CanvasDocument document, Layer layer)
    {
        PixelBuffer? pixels = layer.Pixels;
        if (pixels is null) return null;

        if (_layers.TryGetValue(layer.Id, out LayerCache? cached) && ReferenceEquals(cached.Source, pixels))
            return cached.Bitmap;

        // A layer smaller than the canvas is stretched the same way CanvasDocument.Flatten stretches
        // it, so the preview and an export see the same pixels.
        PixelBuffer upload = pixels.Width == document.Width && pixels.Height == document.Height ? pixels : document.Fit(pixels);
        CanvasBitmap bitmap = UploadBitmap(resources, ToPremultipliedBgra(upload), upload.Width, upload.Height);

        cached?.Bitmap.Dispose();
        _layers[layer.Id] = new LayerCache(pixels, bitmap);
        return bitmap;
    }

    private CanvasBitmap? GetMaskBitmap(ICanvasResourceCreator resources, CanvasDocument document, Layer layer)
    {
        byte[]? coverage = layer.CoverageFor(document.Width, document.Height);
        if (coverage is null) return null;

        if (_masks.TryGetValue(layer.Id, out MaskCache? cached) && ReferenceEquals(cached.Source, coverage))
            return cached.Bitmap;

        // AlphaMaskEffect multiplies by the mask's alpha. Premultiplied white at alpha c is just c in
        // every channel, so the coverage lands in alpha and the color stays neutral.
        var rgba = new byte[coverage.Length * 4];
        for (int i = 0; i < coverage.Length; i++)
        {
            int offset = i * 4;
            byte value = coverage[i];
            rgba[offset] = value;
            rgba[offset + 1] = value;
            rgba[offset + 2] = value;
            rgba[offset + 3] = value;
        }

        CanvasBitmap bitmap = UploadBitmap(resources, rgba, document.Width, document.Height);

        cached?.Bitmap.Dispose();
        _masks[layer.Id] = new MaskCache(coverage, bitmap);
        return bitmap;
    }

    private void EnsureTargets(ICanvasResourceCreator resources, int width, int height)
    {
        if (_front is not null && _front.SizeInPixels.Width == width && _front.SizeInPixels.Height == height) return;

        ReleaseTargets();
        _front = new CanvasRenderTarget(resources, width, height, (float)BitmapDpi);
        _back = new CanvasRenderTarget(resources, width, height, (float)BitmapDpi);
    }

    private CanvasRenderTarget EnsureLayerTarget(ICanvasResourceCreator resources, int width, int height)
    {
        if (_layerTarget is null || _layerTarget.SizeInPixels.Width != width || _layerTarget.SizeInPixels.Height != height)
        {
            _layerTarget?.Dispose();
            _layerTarget = new CanvasRenderTarget(resources, width, height, (float)BitmapDpi);
        }
        return _layerTarget;
    }

    private void PruneLayers()
    {
        _stale.Clear();
        foreach (Guid id in _layers.Keys)
        {
            if (!_live.Contains(id)) _stale.Add(id);
        }
        foreach (Guid id in _stale)
        {
            _layers[id].Bitmap.Dispose();
            _layers.Remove(id);
        }

        _stale.Clear();
        foreach (Guid id in _masks.Keys)
        {
            if (!_live.Contains(id)) _stale.Add(id);
        }
        foreach (Guid id in _stale)
        {
            _masks[id].Bitmap.Dispose();
            _masks.Remove(id);
        }
    }

    private void ReleaseGpu()
    {
        foreach (LayerCache cache in _layers.Values) cache.Bitmap.Dispose();
        _layers.Clear();
        foreach (MaskCache cache in _masks.Values) cache.Bitmap.Dispose();
        _masks.Clear();
        ReleaseTargets();
        _compositeSignature = -1;
    }

    private void ReleaseTargets()
    {
        _front?.Dispose();
        _back?.Dispose();
        _layerTarget?.Dispose();
        _front = null;
        _back = null;
        _layerTarget = null;
    }

    /// <summary>
    /// Document-space rectangle to render: the visible part plus a margin, so a drag of a few pixels
    /// stays inside the cache. Clipped to the document because nothing outside it can be seen.
    /// </summary>
    private static Rect Pad(Rect visible, CanvasDocument document)
    {
        double padX = Math.Min(visible.Width * PaddingFraction, MaxPadding);
        double padY = Math.Min(visible.Height * PaddingFraction, MaxPadding);
        double left = Math.Max(0, visible.X - padX);
        double top = Math.Max(0, visible.Y - padY);
        double right = Math.Min(document.Width, visible.Right + padX);
        double bottom = Math.Min(document.Height, visible.Bottom + padY);
        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Pixels per document pixel for the render targets. Zoomed out, one screen pixel is all the
    /// detail anyone can see, so the composite is rendered at screen resolution; zoomed in, the
    /// visible region is smaller than the screen and it is rendered at document resolution. The caps
    /// keep one target under a sane size on an 8K display or a driver with a small surface limit.
    /// </summary>
    private static double ScaleFor(Rect region, CanvasDevice device, double zoom)
    {
        double scale = zoom < 1 ? Math.Max(zoom, MinScale) : 1;
        long pixels = (long)Math.Ceiling(region.Width) * (long)Math.Ceiling(region.Height);
        if (pixels * scale * scale > MaxRenderPixels)
            scale = Math.Sqrt((double)MaxRenderPixels / pixels);

        int limit = device.MaximumBitmapSizeInPixels;
        if (limit > 0) scale = Math.Min(scale, limit / Math.Max(region.Width, region.Height));
        return Math.Clamp(scale, MinScale, 1);
    }

    /// <summary>
    /// Straight RGBA to premultiplied BGRA. Win2D refuses <c>CanvasAlphaMode.Straight</c> for
    /// bitmaps built from bytes (WINCODEC_ERR_UNSUPPORTEDPIXELFORMAT), and the blend effect wants
    /// premultiplied input anyway, so the conversion happens here, once per upload.
    /// </summary>
    private static byte[] ToPremultipliedBgra(PixelBuffer buffer)
    {
        byte[] source = buffer.Data;
        var premultiplied = new byte[source.Length];
        for (int i = 0; i < source.Length; i += 4)
        {
            byte alpha = source[i + 3];
            if (alpha == 0) continue;

            premultiplied[i] = (byte)(((source[i + 2] * alpha) + 127) / 255);
            premultiplied[i + 1] = (byte)(((source[i + 1] * alpha) + 127) / 255);
            premultiplied[i + 2] = (byte)(((source[i] * alpha) + 127) / 255);
            premultiplied[i + 3] = alpha;
        }
        return premultiplied;
    }

    /// <summary>
    /// Uploads premultiplied BGRA, shrinking it first when the document is longer than the device's
    /// largest surface. The screen cannot show more than its own resolution anyway, and refusing to
    /// draw a 20000 pixel wide panorama would be worse than showing it soft.
    /// </summary>
    private CanvasBitmap UploadBitmap(ICanvasResourceCreator resources, byte[] premultiplied, int width, int height)
    {
        if (_uploadScale < 1)
        {
            int scaledWidth = Math.Max(1, (int)Math.Round(width * _uploadScale));
            int scaledHeight = Math.Max(1, (int)Math.Round(height * _uploadScale));
            premultiplied = ScaleNearest(premultiplied, width, height, scaledWidth, scaledHeight);
            width = scaledWidth;
            height = scaledHeight;
        }

        return CanvasBitmap.CreateFromBytes(
            resources, premultiplied, width, height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized, (float)BitmapDpi, CanvasAlphaMode.Premultiplied);
    }

    /// <summary>Nearest-neighbour shrink of premultiplied BGRA, same sampling rule as CanvasDocument.Fit.</summary>
    private static byte[] ScaleNearest(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        var scaled = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sourceY = (int)((long)y * sourceHeight / height);
            for (int x = 0; x < width; x++)
            {
                int sourceX = (int)((long)x * sourceWidth / width);
                int from = ((sourceY * sourceWidth) + sourceX) * 4;
                int to = ((y * width) + x) * 4;
                scaled[to] = source[from];
                scaled[to + 1] = source[from + 1];
                scaled[to + 2] = source[from + 2];
                scaled[to + 3] = source[from + 3];
            }
        }
        return scaled;
    }

    private static double UploadScaleFor(CanvasDocument document, CanvasDevice device)
    {
        int limit = device.MaximumBitmapSizeInPixels;
        if (limit <= 0) return 1;

        int longest = Math.Max(document.Width, document.Height);
        return longest <= limit ? 1 : (double)limit / longest;
    }

    private static CanvasImageInterpolation SamplingFor(double zoom)
        => zoom >= 1 ? CanvasImageInterpolation.NearestNeighbor : CanvasImageInterpolation.Linear;

    private static bool Covers(Rect outer, Rect inner)
        => inner.X >= outer.X - 0.5
        && inner.Y >= outer.Y - 0.5
        && inner.Right <= outer.Right + 0.5
        && inner.Bottom <= outer.Bottom + 0.5;

    /// <summary>
    /// Cheap fingerprint of the stack. Buffers are edited in place elsewhere in the app, so identity
    /// of the pixel and mask arrays plus a Refresh() call is what tells the renderer to rebuild.
    /// </summary>
    private static int SignatureOf(CanvasDocument document)
    {
        var hash = new HashCode();
        hash.Add(document.Id);
        hash.Add(document.Width);
        hash.Add(document.Height);
        foreach (Layer layer in document.Layers)
        {
            hash.Add(layer.Id);
            hash.Add(layer.IsVisible);
            hash.Add(layer.Opacity);
            hash.Add((int)layer.BlendMode);
            hash.Add(layer.Pixels is null ? 0 : RuntimeHelpers.GetHashCode(layer.Pixels));
            hash.Add(layer.Mask is null ? 0 : RuntimeHelpers.GetHashCode(layer.Mask));
            hash.Add(layer.Adjustment is null ? 0 : layer.Adjustment.GetHashCode());
        }
        return hash.ToHashCode();
    }

    private sealed class LayerCache(PixelBuffer source, CanvasBitmap bitmap)
    {
        public PixelBuffer Source { get; } = source;

        public CanvasBitmap Bitmap { get; } = bitmap;
    }

    private sealed class MaskCache(byte[] source, CanvasBitmap bitmap)
    {
        public byte[] Source { get; } = source;

        public CanvasBitmap Bitmap { get; } = bitmap;
    }
}
