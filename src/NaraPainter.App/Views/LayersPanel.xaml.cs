using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace NaraPainter.App.Views;

public sealed partial class LayersPanel : UserControl
{
    private DocumentViewModel? _document;
    private bool _droppedIntoFolder;

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

    private void OnGroupLayers(object sender, RoutedEventArgs e) => _document?.GroupSelectedLayers();

    private void OnUngroupLayers(object sender, RoutedEventArgs e) => _document?.UngroupSelectedFolder();

    private void OnMergeDown(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is { IsGroup: true }) _document.MergeGroup();
        else _document.MergeDown();
    }

    private void OnMergeGroup(object sender, RoutedEventArgs e) => _document?.MergeGroup();

    /// <summary>Folds or opens the folder whose row carries the triangle.</summary>
    private void OnToggleFolder(object sender, RoutedEventArgs e)
    {
        if (LayerFor(sender) is { } folder) _document?.ToggleCollapsed(folder);
    }

    /// <summary>Only a folder accepts a row dropped onto it; anywhere else the list's own reorder wins.</summary>
    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (_document?.DraggedRow is null) return;
        if (sender is not FrameworkElement { DataContext: LayerViewModel target }) return;
        if (!target.IsGroup || ReferenceEquals(target, _document.DraggedRow)) return;

        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = Localization.Interpolate(Strings.LayersDropIntoHint, target.Name);
    }

    /// <summary>
    /// A row dropped onto a folder goes inside it; dropped anywhere else it simply lands in the stack
    /// order the list already applied.
    /// </summary>
    private void OnRowDrop(object sender, DragEventArgs e)
    {
        if (_document?.DraggedRow is not { } dragged) return;
        if (sender is not FrameworkElement { DataContext: LayerViewModel target }) return;
        if (!target.IsGroup || ReferenceEquals(target, dragged)) return;

        e.Handled = true;
        if (_document.MoveLayerInto(dragged, target)) _droppedIntoFolder = true;
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (_document is null) return;

        // The list's own reorder only shuffles rows; remembering which one is moving is what lets a drop
        // onto a folder mean "put this inside".
        _document.DraggedRow = args.Items.Count > 0 ? args.Items[0] as LayerViewModel : null;
        _droppedIntoFolder = false;
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_document is null) return;

        _document.DraggedRow = null;

        // A drop onto a folder already decided where the row goes; the list's own reordering would
        // otherwise undo it by shuffling the rows back into plain order.
        if (_droppedIntoFolder)
        {
            _droppedIntoFolder = false;
            return;
        }

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

    /// <summary>
    /// Opens the undo merge session for an opacity edit, so dragging the slider lands as one step rather
    /// than one per percent. The row shows a bare slider and number box instead of the shared slider
    /// control, so its two halves each say when they are being used.
    /// </summary>
    /// <remarks>
    /// Deliberately on focus rather than on a value change: a value change also fires when the row is
    /// merely being displayed, which would open a session nobody asked for and could merge unrelated
    /// edits into one undo step.
    /// </remarks>
    private void OnOpacityEditStarted(object sender, RoutedEventArgs e)
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
