using System.ComponentModel;
using NaraDreamPainter.App.Services;
using NaraDreamPainter.App.ViewModels;
using NaraDreamPainter.Imaging.Services;
using NaraDreamPainter.Models.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace NaraDreamPainter.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly FileDialogService _dialogs;
    private bool _fitted;

    public MainWindow()
    {
        InitializeComponent();

        Title = Document.WindowTitle;
        _dialogs = new FileDialogService(this, Document.Codec);

        Canvas.Document = Document.Document;
        Canvas.AdjustmentFilter = Document.Filter;
        Canvas.ViewChanged += OnViewChanged;
        Canvas.Loaded += (_, _) => FitCanvas();

        // The canvas control marks its own pointer events handled while panning, so the brush has to
        // subscribe at the CanvasView level and ask for handled events too.
        Canvas.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCanvasPointerPressed), true);
        Canvas.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnCanvasPointerMoved), true);
        Canvas.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnCanvasPointerReleased), true);
        Canvas.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCanvasPointerCaptureLost), true);

        Layers.Attach(Document);
        Properties.Attach(Document);

        LabelFlyouts();

        Document.Changed += (_, _) => Canvas.Refresh();
        Document.DocumentReplaced += OnDocumentReplaced;
        Document.PropertyChanged += OnDocumentPropertyChanged;
        Closed += OnClosed;
    }

    /// <summary>
    /// Flyout content is not part of the window's visual tree, so the compiled bindings used elsewhere
    /// in the XAML do not reach it. These few items are labelled here instead, and relabelled on a
    /// language change.
    /// </summary>
    private void LabelFlyouts()
    {
        LocalizedStrings text = Text;

        string[] adjustments =
        [
            text.AdjustBrightnessContrast,
            text.AdjustHueSaturation,
            text.AdjustLevels,
            text.AdjustCurves
        ];

        for (int i = 0; i < AdjustmentFlyout.Items.Count && i < adjustments.Length; i++)
        {
            if (AdjustmentFlyout.Items[i] is MenuFlyoutItem item) item.Text = adjustments[i];
        }

        string[] fill =
        [
            text.ToolbarFillSelection,
            text.ToolbarFeatherMask,
            text.ToolbarInvertMask,
            text.ToolbarClearMask
        ];

        int index = 0;
        foreach (object entry in FillFlyout.Items)
        {
            if (entry is MenuFlyoutItem item && index < fill.Length) item.Text = fill[index++];
        }

        Localization.CultureChanged += (_, _) => LabelFlyouts();
    }

    /// <summary>Initialized before InitializeComponent so the compiled bindings in the XAML have a document.</summary>
    public DocumentViewModel Document { get; } = CreateDocument();

    /// <summary>The localisation surface the compiled bindings in this window read their text from.</summary>
    public LocalizedStrings Text => LocalizedStrings.Instance;

    private bool _paintingMask;

    private static DocumentViewModel CreateDocument()
    {
        var codec = new ImageCodec();
        return new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e) => await OpenAsync();

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        string? path = await _dialogs.PickExportPathAsync(Document.SuggestedExportName);
        if (path is null) return;

        try
        {
            Document.Export(path);
        }
        catch (Exception error)
        {
            await ShowMessageAsync("Export failed", error.Message);
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Document.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => Document.Redo();

    private void OnNewLayerClick(object sender, RoutedEventArgs e) => Document.AddLayer();

    private void OnDuplicateLayerClick(object sender, RoutedEventArgs e) => Document.DuplicateLayer(Document.SelectedLayer);

    private void OnDeleteLayerClick(object sender, RoutedEventArgs e) => Document.DeleteLayer(Document.SelectedLayer);

    private void OnAddAdjustmentLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out AdjustmentKind kind)) return;

        Document.AddAdjustmentLayer(kind);
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(Canvas.Zoom * 1.25);

    private void OnMaskBrushClick(object sender, RoutedEventArgs e)
    {
        // The toggle owns the state; read it back rather than tracking a second copy here.
        Document.MaskBrush.IsActive = MaskBrushButton.IsChecked == true;
    }

    private void OnContentAwareFillClick(object sender, RoutedEventArgs e) => Document.ContentFill.Fill();

    private void OnFeatherMaskClick(object sender, RoutedEventArgs e) => Document.MaskBrush.Feather(Document.MaskBrush.FeatherRadius);

    private void OnInvertMaskClick(object sender, RoutedEventArgs e) => Document.MaskBrush.Invert();

    private void OnClearMaskClick(object sender, RoutedEventArgs e) => Document.Selection.ClearMask();

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!Document.MaskBrush.IsActive) return;

        var point = e.GetCurrentPoint(Canvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        var document = ToDocument(point.Position);
        Document.MaskBrush.BeginStroke(document.X, document.Y);
        _paintingMask = true;
        Canvas.CapturePointer(e.Pointer);
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        var document = ToDocument(e.GetCurrentPoint(Canvas).Position);
        Document.MaskBrush.ContinueStroke(document.X, document.Y);
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        _paintingMask = false;
        Document.MaskBrush.EndStroke();
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        _paintingMask = false;
        Document.MaskBrush.EndStroke();
    }

    /// <summary>Control coordinates to canvas pixels, matching how the renderer places the document.</summary>
    private System.Numerics.Vector2 ToDocument(Windows.Foundation.Point position)
    {
        double zoom = Canvas.Zoom > 0 ? Canvas.Zoom : 1;
        return new System.Numerics.Vector2(
            (float)((position.X - Canvas.PanX) / zoom),
            (float)((position.Y - Canvas.PanY) / zoom));
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(Canvas.Zoom / 1.25);

    private void OnActualSizeClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(1);

    private void OnFitClick(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    private void OnViewChanged(object? sender, EventArgs e) => Document.SetZoom(Canvas.Zoom);

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_fitted || args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;

        _fitted = true;
        Canvas.FitToWindow();
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null) e.DragUIOverride.Caption = "Open image";
    }

    private async void OnCanvasDrop(object sender, DragEventArgs e)
    {
        string? path = await DroppedFile.FirstPathAsync(e.DataView);
        if (path is null) return;

        await OpenAsync(path);
    }

    private void OnDocumentReplaced(object? sender, CanvasDocument document)
    {
        Canvas.Document = document;
        if (Canvas.ActualWidth > 0) Canvas.FitToWindow();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(DocumentViewModel.WindowTitle))
        {
            Title = Document.WindowTitle;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args) => Application.Current.Exit();

    private async Task OpenAsync()
    {
        string? path = await _dialogs.PickImageAsync();
        if (path is not null) await OpenAsync(path);
    }

    private async Task OpenAsync(string path)
    {
        try
        {
            Document.Open(path);
        }
        catch (Exception error)
        {
            await ShowMessageAsync("Open failed", error.Message);
        }
    }

    private void FitCanvas()
    {
        if (Canvas.ActualWidth > 0) Canvas.FitToWindow();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = Content.XamlRoot
        };

        await dialog.ShowAsync();
    }
}
