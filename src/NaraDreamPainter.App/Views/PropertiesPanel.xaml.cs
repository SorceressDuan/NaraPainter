using NaraDreamPainter.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace NaraDreamPainter.App.Views;

public sealed partial class PropertiesPanel : UserControl
{
    private DocumentViewModel? _document;

    public PropertiesPanel()
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

    private void OnAdjustmentEditStarted(object sender, EventArgs e) => _document?.Adjustment.BeginEdit();

    private void OnNameGotFocus(object sender, RoutedEventArgs e)
    {
        if (_document?.SelectedLayer is { } layer) layer.BeginNameEdit();
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (_document?.SelectedLayer is { } layer) layer.Name = layer.Name.Trim();
    }

    private void OnOpacityEditStarted(object sender, EventArgs e)
    {
        if (_document?.SelectedLayer is { } layer) layer.BeginOpacityEdit();
    }

    private void OnResetBrightnessContrast(object sender, RoutedEventArgs e) =>
        _document?.Adjustment.Reset(AdjustmentKind.BrightnessContrast);

    private void OnResetHueSaturation(object sender, RoutedEventArgs e) =>
        _document?.Adjustment.Reset(AdjustmentKind.HueSaturation);

    private void OnResetLevels(object sender, RoutedEventArgs e) =>
        _document?.Adjustment.Reset(AdjustmentKind.Levels);

    private void OnResetCurves(object sender, RoutedEventArgs e) =>
        _document?.Adjustment.Reset(AdjustmentKind.Curves);

    private void OnAddCurvePoint(object sender, RoutedEventArgs e) => _document?.Adjustment.AddCurvePoint();

    private void OnRemoveCurvePoint(object sender, RoutedEventArgs e) => _document?.Adjustment.RemoveCurvePoint();

    private void OnApplyMask(object sender, RoutedEventArgs e) => _document?.Selection.ApplyMask();

    private void OnSelectAll(object sender, RoutedEventArgs e) => _document?.Selection.SelectAll();

    private void OnClearMask(object sender, RoutedEventArgs e) => _document?.Selection.ClearMask();

    private void OnClearSelection(object sender, RoutedEventArgs e) => _document?.Selection.Clear();

    private void OnFeatherMask(object sender, RoutedEventArgs e)
    {
        if (_document is { } document) document.MaskBrush.Feather(document.MaskBrush.FeatherRadius);
    }

    private void OnInvertMask(object sender, RoutedEventArgs e) => _document?.MaskBrush.Invert();

    private void OnContentAwareFill(object sender, RoutedEventArgs e) => _document?.ContentFill.Fill();
}
