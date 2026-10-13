using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;

namespace NaraPainter.App.Views;

public sealed partial class LayersPanel : UserControl
{
    private DocumentViewModel? _document;

    /// <summary>The row currently wearing a drop marker, so it can be cleared again.</summary>
    private FrameworkElement? _marked;
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
        if (_document is null) return;
        if (_document.SelectedLayer is null) { _document.ReportNoSelection(); return; }
        _document.DuplicateLayer(_document.SelectedLayer);
    }

    private void OnDeleteLayer(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is null) { _document.ReportNoSelection(); return; }
        _document.DeleteLayer(_document.SelectedLayer);
    }

    private void OnMoveLayerUp(object sender, RoutedEventArgs e) => Move(-1);

    private void OnMoveLayerDown(object sender, RoutedEventArgs e) => Move(1);

    private void OnGroupLayers(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is null) { _document.ReportNoSelection(); return; }
        _document.GroupSelectedLayers();
    }

    private void OnUngroupLayers(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is not { IsGroup: true }) { _document.ReportNoSelection(); return; }
        _document.UngroupSelectedFolder();
    }

    private void OnMergeDown(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is null) { _document.ReportNoSelection(); return; }
        if (_document.SelectedLayer is { IsGroup: true }) _document.MergeGroup();
        else _document.MergeDown();
    }

    private void OnMergeGroup(object sender, RoutedEventArgs e)
    {
        if (_document is null) return;
        if (_document.SelectedLayer is not { IsGroup: true }) { _document.ReportNoSelection(); return; }
        _document.MergeGroup();
    }

    /// <summary>Folds or opens the folder whose row carries the triangle.</summary>
    private void OnToggleFolder(object sender, RoutedEventArgs e)
    {
        if (LayerFor(sender) is { } folder) _document?.ToggleCollapsed(folder);
    }

    /// <summary>
    /// Reads where in the row the pointer is and shows what a drop there would do: a line along the
    /// top or bottom edge for a place in the stack, a wash over the row for going inside a folder.
    /// </summary>
    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (_document?.DraggedRow is not { } dragged) return;
        if (sender is not FrameworkElement { DataContext: LayerViewModel target } row) return;

        ClearDropMarker();
        if (ReferenceEquals(target, dragged) || _document.IsDescendantOfRow(target, dragged))
        {
            e.AcceptedOperation = DataPackageOperation.None;
            return;
        }

        DocumentViewModel.RowDrop where = ZoneFor(e, row, target);
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = Localization.Interpolate(
            where == DocumentViewModel.RowDrop.Into ? Strings.LayersDropIntoHint : Strings.LayersDropBetweenHint,
            target.Name);
        ShowDropMarker(row, where);
    }

    /// <summary>
    /// Which third of the row the pointer is in: the outer thirds stack the row above or below, the
    /// middle one puts it inside, and only a folder has a middle that means anything.
    /// </summary>
    private static DocumentViewModel.RowDrop ZoneFor(DragEventArgs e, FrameworkElement row, LayerViewModel target)
    {
        double height = row.ActualHeight;
        double y = e.GetPosition(row).Y;

        // A row this short has no thirds worth aiming at, so it stacks.
        if (height < 24 || y < height / 3) return DocumentViewModel.RowDrop.Above;
        if (y > height * 2 / 3) return DocumentViewModel.RowDrop.Below;
        return target.IsGroup ? DocumentViewModel.RowDrop.Into : DocumentViewModel.RowDrop.Above;
    }

    private void ShowDropMarker(FrameworkElement row, DocumentViewModel.RowDrop where)
    {
        if (MarkerIn(row) is not { } marker) return;

        marker.Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        switch (where)
        {
            case DocumentViewModel.RowDrop.Above:
                marker.Height = 2;
                marker.VerticalAlignment = VerticalAlignment.Top;
                break;
            case DocumentViewModel.RowDrop.Below:
                marker.Height = 2;
                marker.VerticalAlignment = VerticalAlignment.Bottom;
                break;
            default:
                // Inside: a wash the height of the row rather than a line.
                marker.Height = double.NaN;
                marker.VerticalAlignment = VerticalAlignment.Stretch;
                marker.Opacity = 0.35;
                break;
        }

        _marked = row;
        marker.Visibility = Visibility.Visible;
    }

    private void ClearDropMarker()
    {
        if (_marked is null) return;
        if (MarkerIn(_marked) is { } marker)
        {
            marker.Visibility = Visibility.Collapsed;
            marker.Opacity = 1;
        }

        _marked = null;
    }

    /// <summary>The marker in a row's template, found by name since rows share one template.</summary>
    private static Border? MarkerIn(FrameworkElement row) => row.FindName("DropMarker") as Border;

    private void OnRowDragLeave(object sender, DragEventArgs e) => ClearDropMarker();

    /// <summary>Puts the dragged row where the marker was showing.</summary>
    private void OnRowDrop(object sender, DragEventArgs e)
    {
        ClearDropMarker();
        if (_document?.DraggedRow is not { } dragged) return;
        if (sender is not FrameworkElement { DataContext: LayerViewModel target } row) return;

        e.Handled = true;
        DocumentViewModel.RowDrop where = ZoneFor(e, row, target);
        if (!_document.MoveLayerTo(dragged, target, where)) e.Handled = false;
    }

    private void OnDragItemsStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (_document is null) return;

        // The list's own reorder only shuffles rows; remembering which one is moving is what lets a
        // drop say where it should go. The starting arrangement is kept too, because that reorder
        // rewrites the row order while the pointer moves and a drop has to be measured from the state
        // the user was looking at.
        _document.DraggedRow = args.Items.Count > 0 ? args.Items[0] as LayerViewModel : null;
        _document.BeginDrag();
        ClearDropMarker();
    }

    private void OnDragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_document is null) return;

        ClearDropMarker();
        _document.DraggedRow = null;

        // A drop already placed its row and recorded the step. The list has also shuffled its own order
        // underneath; putting the rows back the way the drop left them is what stops that shuffle from
        // reaching the document.
        _document.SettleRows();
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
        if (_document?.SelectedLayer is not { } layer)
        {
            _document?.ReportNoSelection();
            return;
        }

        var order = _document.Layers.ToList();
        int index = order.IndexOf(layer);
        int target = index + panelDelta;
        if (index < 0 || target < 0 || target >= order.Count)
        {
            // The row is already at the top or the bottom of the stack.
            _document.ReportCannotMove();
            return;
        }

        (order[index], order[target]) = (order[target], order[index]);
        _document.ReorderLayers(order);
    }

    private static LayerViewModel? LayerFor(object sender) =>
        (sender as FrameworkElement)?.DataContext as LayerViewModel;
}
