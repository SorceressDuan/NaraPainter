using System.ComponentModel;
using NaraDreamPainter.App.Services;
using NaraDreamPainter.App.ViewModels;
using NaraDreamPainter.Imaging.Services;
using NaraDreamPainter.Models.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

        Layers.Attach(Document);
        Properties.Attach(Document);

        Document.Changed += (_, _) => Canvas.Refresh();
        Document.DocumentReplaced += OnDocumentReplaced;
        Document.PropertyChanged += OnDocumentPropertyChanged;
        Closed += OnClosed;
    }

    /// <summary>Initialized before InitializeComponent so the compiled bindings in the XAML have a document.</summary>
    public DocumentViewModel Document { get; } = CreateDocument();

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
