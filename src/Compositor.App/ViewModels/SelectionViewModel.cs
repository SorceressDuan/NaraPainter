using Compositor.App.Services;
using Compositor.Models.Services;

namespace Compositor.App.ViewModels;

/// <summary>
/// The selection rectangle behind the mask workflow. The region is typed in canvas pixels; coverage is
/// built at canvas resolution and stored on the selected layer as its mask.
/// </summary>
public sealed class SelectionViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private readonly ISelectionMaskBuilder _masks;
    private int _shapeIndex;
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _feather;

    public SelectionViewModel(DocumentViewModel owner, ISelectionMaskBuilder masks)
    {
        _owner = owner;
        _masks = masks;
    }

    public IReadOnlyList<string> ShapeNames { get; } = ["Rectangle", "Ellipse"];

    public int ShapeIndex
    {
        get => _shapeIndex;
        set => SetProperty(ref _shapeIndex, Math.Clamp(value, 0, ShapeNames.Count - 1));
    }

    public double X
    {
        get => _x;
        set => SetProperty(ref _x, Round(value));
    }

    public double Y
    {
        get => _y;
        set => SetProperty(ref _y, Round(value));
    }

    public double Width
    {
        get => _width;
        set => SetProperty(ref _width, Round(value));
    }

    public double Height
    {
        get => _height;
        set => SetProperty(ref _height, Round(value));
    }

    public double Feather
    {
        get => _feather;
        set => SetProperty(ref _feather, double.IsFinite(value) ? Math.Clamp(Math.Round(value, 1), 0, 200) : 0);
    }

    public bool HasMask => _owner.SelectedLayer?.IsMasked == true;

    public string MaskLabel => HasMask ? "Mask applied to the selected layer" : "No mask on the selected layer";

    public void ResetToCanvas()
    {
        _x = 0;
        _y = 0;
        _width = _owner.Document.Width;
        _height = _owner.Document.Height;
        OnPropertyChanged(string.Empty);
    }

    public void ApplyMask()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null)
        {
            _owner.Status = "Select a layer before applying a mask";
            return;
        }

        var region = new SelectionRegion((SelectionShape)_shapeIndex, (int)_x, (int)_y, (int)_width, (int)_height);
        if (region.IsEmpty)
        {
            _owner.Status = "The selection is empty";
            return;
        }

        SetMask(layer, _masks.Build(region, _owner.Document.Width, _owner.Document.Height, _feather), "Apply Mask");
        _owner.Status = $"Mask applied to {layer.Name}";
    }

    public void SelectAll()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null)
        {
            _owner.Status = "Select a layer before applying a mask";
            return;
        }

        SetMask(layer, _masks.Full(_owner.Document.Width, _owner.Document.Height), "Select All");
        _owner.Status = $"Whole canvas selected on {layer.Name}";
    }

    public void ClearMask()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || !layer.IsMasked) return;

        SetMask(layer, null, "Clear Mask");
        _owner.Status = $"Mask cleared on {layer.Name}";
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasMask));
        OnPropertyChanged(nameof(MaskLabel));
    }

    private void SetMask(LayerViewModel layer, byte[]? mask, string label)
    {
        byte[]? before = layer.Model.Mask;
        if (ReferenceEquals(before, mask)) return;

        layer.AssignMask(mask);
        _owner.History.Push(new PropertyChange<byte[]?>(label, before, mask, layer.AssignMask));
        Refresh();
    }

    private static double Round(double value) => double.IsFinite(value) ? Math.Round(value) : 0;
}
