using NaraPainter.App.Services;
using NaraPainter.Models.Services;

namespace NaraPainter.App.ViewModels;

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

    /// <summary>Kept as one instance so a language change does not disturb the picker's selection.</summary>
    public IReadOnlyList<string> ShapeNames => LocalizedLists.ShapeNames;

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

    public string MaskLabel => HasMask ? Strings.SelectionMaskApplied : Strings.SelectionNoMask;

    /// <summary>Whether the typed region or a painted mask gives the fill something to work on.</summary>
    public string SelectionLabel
    {
        get
        {
            if (HasRegion) return Strings.SelectionActive;
            return HasMask ? Strings.SelectionFromMask : Strings.SelectionNone;
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
            _owner.Status = Strings.StatusSelectLayerForMask;
            return;
        }

        var region = new SelectionRegion((SelectionShape)_shapeIndex, (int)_x, (int)_y, (int)_width, (int)_height);
        if (region.IsEmpty)
        {
            _owner.Status = Strings.StatusSelectionEmpty;
            return;
        }

        SetMask(layer, _masks.Build(region, _owner.Document.Width, _owner.Document.Height, _feather), Strings.UndoApplyMask);
        _owner.Status = Localization.Interpolate(Strings.StatusMaskApplied, layer.Name);
    }

    public void SelectAll()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null)
        {
            _owner.Status = Strings.StatusSelectLayerForMask;
            return;
        }

        SetMask(layer, _masks.Full(_owner.Document.Width, _owner.Document.Height), Strings.UndoSelectAll);
        _owner.Status = Localization.Interpolate(Strings.StatusSelectAll, layer.Name);
    }

    public void ClearMask()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || !layer.IsMasked) return;

        SetMask(layer, null, Strings.UndoClearMask);
        _owner.Status = Localization.Interpolate(Strings.StatusMaskCleared, layer.Name);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(HasMask));
        OnPropertyChanged(nameof(MaskLabel));
        OnPropertyChanged(nameof(HasRegion));
        OnPropertyChanged(nameof(SelectionLabel));
    }

    /// <summary>Re-reads the labels that come from resources after a language change.</summary>
    public void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ShapeNames));
        Refresh();
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
