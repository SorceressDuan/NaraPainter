using NaraDreamPainter.App.Services;
using NaraDreamPainter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace NaraDreamPainter.App.Views;

public sealed partial class LayersPanel : UserControl
{
    private DocumentViewModel? _document;

    public LayersPanel()
    {
        InitializeComponent();
    }

    /// <summary>The localisation surface the compiled bindings in this panel read their text from.</summary>
    public LocalizedStrings Text => LocalizedStrings.Instance;

    public void Attach(DocumentViewModel document)
    {
        _document = document;
        DataContext = document;
    }

    private void OnAddLayer(object sender, RoutedEventArgs e) => _document?.AddLayer();

    private void OnDuplicateLayer(object sender, RoutedEventArgs e)
    {
        if (_document is not null) _document.DuplicateLayer(_document.SelectedLayer);
    }

    private void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_document is not null) _document.DeleteLayer(_document.SelectedLayer);
    }

    private void OnMoveLayerUp(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveLayerDown(object sender, RoutedEventArgs e) => Move(1);

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_document is null) return;

        // The list has already moved the rows; the document follows whatever order is showing.
        _document.ReorderLayers(_document.Layers.ToList());
    }

    private void OnNameGotFocus(object sender, RoutedEventArgs e)
    {
        if (LayerFor(sender) is { } layer) layer.BeginNameEdit();
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (LayerFor(sender) is { } layer) layer.Name = layer.Name.Trim();
    }

    private void OnOpacityEditStarted(object sender, EventArgs e)
    {
        if (LayerFor(sender) is { } layer) layer.BeginOpacityEdit();
    }

    private void OnPanelDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null) e.DragUIOverride.Caption = Strings.LayersImportHint;
    }

    private async void OnPanelDrop(object sender, DragEventArgs e)
    {
        if (_document is null) return;

        string? path = await DroppedFile.FirstPathAsync(e.DataView);
        if (path is null) return;

        try
        {
            _document.ImportLayer(path);
        }
        catch (Exception error)
        {
            _document.Status = Localization.Interpolate(Strings.LayersImportFailed, Path.GetFileName(path), error.Message);
        }
    }

    private void Move(int panelDelta)
    {
        if (_document?.SelectedLayer is not { } layer) return;

        var order = _document.Layers.ToList();
        int index = order.IndexOf(layer);
        int target = index + panelDelta;
        if (index < 0 || target < 0 || target >= order.Count) return;

        (order[index], order[target]) = (order[target], order[index]);
        _document.ReorderLayers(order);
    }

    private static LayerViewModel? LayerFor(object sender) =>
        (sender as FrameworkElement)?.DataContext as LayerViewModel;
}
