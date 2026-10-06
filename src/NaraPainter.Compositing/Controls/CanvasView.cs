using System.Numerics;
using NaraPainter.Compositing.Rendering;
using NaraPainter.Models.Documents;
using NaraPainter.Models.Services;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace NaraPainter.Compositing.Controls;

/// <summary>
/// The editor canvas: zoom, pan and a Win2D render of a <see cref="CanvasDocument"/>.
///
/// Win2D's CanvasControl is sealed in the WinUI 3 projection, so this hosts one instead of deriving
/// from it. Everything is built in code to keep the project a plain C# library with no XAML.
/// </summary>
public sealed class CanvasView : UserControl
{
    private const double MinZoom = 0.02;
    private const double MaxZoom = 32;
    private const double ZoomStep = 1.1;
    private const double FitMargin = 24;
    private const double PixelGridZoom = 8;
    private const int MaxGridLines = 2000;
    private const float CheckerTile = 16;

    private static readonly Color BackdropColor = Color.FromArgb(255, 32, 32, 32);
    private static readonly Color GridColor = Color.FromArgb(48, 255, 255, 255);
    private static readonly Color CheckerLight = Color.FromArgb(255, 70, 70, 70);
    private static readonly Color CheckerDark = Color.FromArgb(255, 56, 56, 56);

    private readonly CanvasControl _canvas = new();
    private readonly DocumentRenderer _renderer = new();

    private CanvasDocument? _document;
    private IAdjustmentFilter? _adjustmentFilter;
    private double _zoom = 1;
    private double _panX;
    private double _panY;
    private bool _showPixelGrid;
    private bool _fitPending;
    private bool _spaceHeld;
    private bool _panning;
    private Point _panPointer;
    private double _panStartX;
    private double _panStartY;
    private CanvasRenderTarget? _checkerTile;
    private CanvasImageBrush? _checkerBrush;

    public CanvasView()
    {
        _canvas.ClearColor = BackdropColor;
        _canvas.IsTabStop = true;
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += OnCreateResources;
        _canvas.SizeChanged += OnSizeChanged;
        _canvas.PointerWheelChanged += OnPointerWheelChanged;
        _canvas.PointerPressed += OnPointerPressed;
        _canvas.PointerMoved += OnPointerMoved;
        _canvas.PointerReleased += OnPointerReleased;
        _canvas.PointerCaptureLost += OnPointerCaptureLost;
        _canvas.KeyDown += OnKeyDown;
        _canvas.KeyUp += OnKeyUp;
        _canvas.LostFocus += OnLostFocus;
        Content = _canvas;
    }

    /// <summary>Document on the canvas. Setting one fits it to the control and clears the render caches.</summary>
    public CanvasDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value)) return;
            _document = value;
            _renderer.Invalidate();
            _fitPending = value is not null;
            if (_fitPending && ViewWidth > 0 && ViewHeight > 0) FitToWindow();
            else _canvas.Invalidate();
        }
    }

    /// <summary>1.0 shows one document pixel per DIP.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetView(value, _panX, _panY);
    }

    public double PanX
    {
        get => _panX;
        set => SetView(_zoom, value, _panY);
    }

    public double PanY
    {
        get => _panY;
        set => SetView(_zoom, _panX, value);
    }

    /// <summary>Turns the pixel grid on. It only appears from <see cref="PixelGridZoom"/> upwards.</summary>
    public bool ShowPixelGrid
    {
        get => _showPixelGrid;
        set
        {
            if (_showPixelGrid == value) return;
            _showPixelGrid = value;
            _canvas.Invalidate();
        }
    }

    /// <summary>Forces the BlendCompositor path instead of the GPU one.</summary>
    public bool ForceCpuRendering
    {
        get => _renderer.ForceCpu;
        set
        {
            if (_renderer.ForceCpu == value) return;
            _renderer.ForceCpu = value;
            Refresh();
        }
    }

    /// <summary>True when the last frame was drawn through <see cref="CpuCompositor"/>.</summary>
    public bool UsedCpuPath => _renderer.UsedCpuPath;

    /// <summary>
    /// Runs adjustment layers for the preview. Without it adjustment layers are skipped on the canvas
    /// the same way <c>CanvasDocument.Flatten</c> skips them, which would make the canvas disagree
    /// with an export; the app hands in the same filter it exports with.
    /// </summary>
    public IAdjustmentFilter? AdjustmentFilter
    {
        get => _adjustmentFilter;
        set
        {
            if (ReferenceEquals(_adjustmentFilter, value)) return;
            _adjustmentFilter = value;
            _renderer.AdjustmentRunner = value is null ? null : (backdrop, layer) => value.Apply(backdrop, layer.Adjustment!);
            Refresh();
        }
    }

    /// <summary>Raised after zoom or pan changed, for the status bar.</summary>
    public event EventHandler? ViewChanged;

    public void FitToWindow()
    {
        CanvasDocument? document = _document;
        if (document is null) return;

        double width = ViewWidth;
        double height = ViewHeight;
        if (width <= 0 || height <= 0)
        {
            _fitPending = true;
            return;
        }

        double zoom = Math.Clamp(
            Math.Min((width - FitMargin) / document.Width, (height - FitMargin) / document.Height),
            MinZoom, MaxZoom);
        _fitPending = false;
        SetView(zoom, (width - (document.Width * zoom)) / 2, (height - (document.Height * zoom)) / 2);
    }

    /// <summary>Zooms around the middle of the control, so the center stays where it is.</summary>
    public void ZoomTo(double zoom)
    {
        double centerX = ViewWidth / 2;
        double centerY = ViewHeight / 2;
        Point anchor = new((centerX - _panX) / _zoom, (centerY - _panY) / _zoom);
        double target = Math.Clamp(zoom, MinZoom, MaxZoom);
        SetView(target, centerX - (anchor.X * target), centerY - (anchor.Y * target));
    }

    /// <summary>Rebuilds the caches and redraws. Call after a layer, its pixels or a mask changed.</summary>
    public void Refresh()
    {
        _renderer.Invalidate();
        _canvas.Invalidate();
    }

    private double ViewWidth => _canvas.ActualWidth;

    private double ViewHeight => _canvas.ActualHeight;

    private void SetView(double zoom, double panX, double panY)
    {
        if (!double.IsFinite(zoom) || !double.IsFinite(panX) || !double.IsFinite(panY)) return;

        double clamped = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (clamped == _zoom && panX == _panX && panY == _panY) return;

        _zoom = clamped;
        _panX = panX;
        _panY = panY;
        _fitPending = false;
        _canvas.Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ZoomAt(Point anchor, double zoom)
    {
        Point document = new((anchor.X - _panX) / _zoom, (anchor.Y - _panY) / _zoom);
        double target = Math.Clamp(zoom, MinZoom, MaxZoom);
        SetView(target, anchor.X - (document.X * target), anchor.Y - (document.Y * target));
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        CanvasDocument? document = _document;
        if (document is null) return;

        CanvasDrawingSession session = args.DrawingSession;
        var view = new ViewTransform(_zoom, _panX, _panY, ViewWidth, ViewHeight);
        Rect visible = view.VisibleDocumentRect(document.Width, document.Height);
        if (visible.Width <= 0 || visible.Height <= 0) return;

        DrawCheckerboard(session, view, visible);
        _renderer.Draw(session, sender, document, view);
        DrawPixelGrid(session, view, visible);
    }

    private void OnCreateResources(CanvasControl sender, CanvasCreateResourcesEventArgs args)
    {
        if (args.Reason == CanvasCreateResourcesReason.FirstTime) return;

        // Everything below is bound to the device that just went away, render targets included.
        _checkerBrush?.Dispose();
        _checkerTile?.Dispose();
        _checkerBrush = null;
        _checkerTile = null;
        _renderer.Invalidate();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_document is null) return;

        if (_fitPending)
        {
            FitToWindow();
            return;
        }

        if (e.PreviousSize.Width <= 0 || e.PreviousSize.Height <= 0) return;
        SetView(
            _zoom,
            _panX + ((e.NewSize.Width - e.PreviousSize.Width) / 2),
            _panY + ((e.NewSize.Height - e.PreviousSize.Height) / 2));
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_canvas);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) return;

        if ((e.KeyModifiers & VirtualKeyModifiers.Shift) == VirtualKeyModifiers.Shift)
        {
            SetView(_zoom, _panX + delta, _panY);
        }
        else
        {
            ZoomAt(point.Position, _zoom * Math.Pow(ZoomStep, delta / 120.0));
        }
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _canvas.Focus(FocusState.Pointer);

        var point = e.GetCurrentPoint(_canvas);
        bool wantsPan = point.Properties.IsMiddleButtonPressed
            || (point.Properties.IsLeftButtonPressed && _spaceHeld);
        if (!wantsPan) return;

        _panning = true;
        _panPointer = point.Position;
        _panStartX = _panX;
        _panStartY = _panY;
        _canvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;

        var point = e.GetCurrentPoint(_canvas);
        SetView(
            _zoom,
            _panStartX + (point.Position.X - _panPointer.X),
            _panStartY + (point.Position.Y - _panPointer.Y));
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_panning) return;
        _panning = false;
        _canvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e) => _panning = false;

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space) return;
        _spaceHeld = true;
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Space) return;
        _spaceHeld = false;
        e.Handled = true;
    }

    private void OnLostFocus(object sender, RoutedEventArgs e) => _spaceHeld = false;

    private void DrawCheckerboard(CanvasDrawingSession session, ViewTransform view, Rect visible)
    {
        if (_checkerBrush is null)
        {
            (_checkerTile, _checkerBrush) = CreateCheckerBrush(session.Device);
        }

        Point origin = view.ToScreen(visible.X, visible.Y);
        _checkerBrush.Transform = Matrix3x2.CreateTranslation((float)origin.X, (float)origin.Y);
        session.FillRectangle(
            new Rect(origin.X, origin.Y, visible.Width * view.Zoom, visible.Height * view.Zoom),
            _checkerBrush);
    }

    private static (CanvasRenderTarget Tile, CanvasImageBrush Brush) CreateCheckerBrush(CanvasDevice device)
    {
        var tile = new CanvasRenderTarget(device, CheckerTile, CheckerTile, 96);
        using (CanvasDrawingSession session = tile.CreateDrawingSession())
        {
            session.Clear(CheckerLight);
            session.FillRectangle(0, 0, CheckerTile / 2, CheckerTile / 2, CheckerDark);
            session.FillRectangle(CheckerTile / 2, CheckerTile / 2, CheckerTile / 2, CheckerTile / 2, CheckerDark);
        }

        var brush = new CanvasImageBrush(device, tile)
        {
            SourceRectangle = new Rect(0, 0, CheckerTile, CheckerTile),
            ExtendX = CanvasEdgeBehavior.Wrap,
            ExtendY = CanvasEdgeBehavior.Wrap,
            Interpolation = CanvasImageInterpolation.NearestNeighbor
        };
        return (tile, brush);
    }

    /// <summary>
    /// One line per document pixel boundary, only once a pixel is big enough on screen to be worth
    /// telling apart from its neighbour.
    /// </summary>
    private void DrawPixelGrid(CanvasDrawingSession session, ViewTransform view, Rect visible)
    {
        if (!_showPixelGrid || view.Zoom < PixelGridZoom) return;
        if (visible.Width > MaxGridLines || visible.Height > MaxGridLines) return;

        double left = Math.Ceiling(visible.X);
        double top = Math.Ceiling(visible.Y);
        Point origin = view.ToScreen(left, top);
        double right = origin.X + ((visible.Right - left) * view.Zoom);
        double bottom = origin.Y + ((visible.Bottom - top) * view.Zoom);

        session.Antialiasing = CanvasAntialiasing.Aliased;
        for (double x = left; x <= visible.Right; x++)
        {
            double screenX = view.ToScreen(x, top).X;
            session.DrawLine((float)screenX, (float)origin.Y, (float)screenX, (float)bottom, GridColor, 1);
        }
        for (double y = top; y <= visible.Bottom; y++)
        {
            double screenY = view.ToScreen(left, y).Y;
            session.DrawLine((float)origin.X, (float)screenY, (float)right, (float)screenY, GridColor, 1);
        }
    }
}
