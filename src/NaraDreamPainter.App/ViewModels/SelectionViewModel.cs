using NaraDreamPainter.App.Services;
using NaraDreamPainter.Models.Services;

namespace NaraDreamPainter.App.ViewModels;

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

    public SelectionShape Shape => (SelectionShape)_shapeIndex;

    /// <summary>
    /// Whether the typed region actually covers something. The tool starts at zero size, and an empty
    /// region has to read as "no selection" rather than "a selection of nothing".
    /// </summary>
    public bool HasRegion => _width >= 1 && _height >= 1;

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
        set
        {
            if (!SetProperty(ref _width, Round(value))) return;
            OnPropertyChanged(nameof(HasRegion));
        }
    }

    public double Height
    {
        get => _height;
        set
        {
            if (!SetProperty(ref _height, Round(value))) return;
            OnPropertyChanged(nameof(HasRegion));
        }
    }

    public double Feather
    {
        get => _feather;
        set => SetProperty(ref _feather, double.IsFinite(value) ? Math.Clamp(Math.Round(value, 1), 0, 200) : 0);
    }

    public bool HasMask => _owner.SelectedLayer?.IsMasked == true;

    public string MaskLabel => HasMask ? Localization.Get("Selection_MaskApplied") : Localization.Get("Selection_NoMask");

    /// <summary>Whether the typed region or a painted mask gives the fill something to work on.</summary>
    public string SelectionLabel
    {
        get
        {
            if (HasRegion) return Localization.Get("Selection_Active");
            return HasMask ? Localization.Get("Selection_FromMask") : Localization.Get("Selection_None");
        }
    }

    public void ResetToCanvas()
    {
        _x = 0;
        _y = 0;
        _width = _owner.Document.Width;
        _height = _owner.Document.Height;
        OnPropertyChanged(string.Empty);
    }

    public void Clear()
    {
        _x = 0;
        _y = 0;
        _width = 0;
        _height = 0;
        OnPropertyChanged(string.Empty);
    }

    public void ApplyMask()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null)
        {
            _owner.Status = Localization.Get("Status_SelectLayerForMask");
            return;
        }

        var region = new SelectionRegion((SelectionShape)_shapeIndex, (int)_x, (int)_y, (int)_width, (int)_height);
        if (region.IsEmpty)
        {
            _owner.Status = Localization.Get("Status_SelectionEmpty");
            return;
        }

        SetMask(layer, _masks.Build(region, _owner.Document.Width, _owner.Document.Height, _feather), Localization.Get("Undo_ApplyMask"));
        _owner.Status = Localization.Format("Status_MaskApplied", layer.Name);
    }

    public void SelectAll()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null)
        {
            _owner.Status = Localization.Get("Status_SelectLayerForMask");
            return;
        }

        SetMask(layer, _masks.Full(_owner.Document.Width, _owner.Document.Height), Localization.Get("Undo_SelectAll"));
        _owner.Status = Localization.Format("Status_SelectAll", layer.Name);
    }

    public void ClearMask()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || !layer.IsMasked) return;

        SetMask(layer, null, Localization.Get("Undo_ClearMask"));
        _owner.Status = Localization.Format("Status_MaskCleared", layer.Name);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasMask));
        OnPropertyChanged(nameof(MaskLabel));
        OnPropertyChanged(nameof(HasRegion));
        OnPropertyChanged(nameof(SelectionLabel));
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
