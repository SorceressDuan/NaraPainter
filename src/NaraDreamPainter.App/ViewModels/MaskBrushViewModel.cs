using NaraDreamPainter.App.Services;
using NaraDreamPainter.Imaging.Services;
using NaraDreamPainter.Models.Services;

namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// Paints on the selected layer's mask.
/// </summary>
/// <remarks>
/// A stroke mutates the mask array in place as the pointer moves, so the canvas updates live, but the
/// undo step is built from a snapshot taken when the stroke started. That keeps a drag of any length
/// as one entry in the history rather than one per pointer event. Painting on a layer that has no mask
/// yet gives it a fully opaque one first, so the brush always has something to write into.
/// </remarks>
public sealed class MaskBrushViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private readonly ISelectionMaskBuilder _masks;
    private LayerViewModel? _layer;
    private byte[]? _before;
    private bool _isActive;
    private int _size = 40;
    private double _hardness = 0.75;
    private double _opacity = 1;
    private bool _erase;
    private double _featherRadius = 8;
    private double _lastX;
    private double _lastY;
    private bool _hasLast;

    internal MaskBrushViewModel(DocumentViewModel owner, ISelectionMaskBuilder masks)
    {
        _owner = owner;
        _masks = masks;
    }

    /// <summary>True while the mask brush is the active tool, which is when the canvas paints.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!SetProperty(ref _isActive, value)) return;
            if (!value) CancelStroke();
            _owner.Status = value ? Strings.MaskBrushOn : Strings.StatusReady;
        }
    }

    /// <summary>Brush diameter in canvas pixels.</summary>
    public int Size
    {
        get => _size;
        set => SetProperty(ref _size, Math.Clamp(value, 1, 1000));
    }

    /// <summary>0 is a fully soft edge, 1 is a hard one.</summary>
    public double Hardness
    {
        get => _hardness;
        set => SetProperty(ref _hardness, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.75);
    }

    /// <summary>How much coverage one pass lays down, 0 to 1.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1);
    }

    /// <summary>Erase paints the mask away instead of filling it in.</summary>
    public bool Erase
    {
        get => _erase;
        set => SetProperty(ref _erase, value);
    }

    /// <summary>Radius the Feather button blurs the mask edge by.</summary>
    public double FeatherRadius
    {
        get => _featherRadius;
        set => SetProperty(ref _featherRadius, double.IsFinite(value) ? Math.Clamp(Math.Round(value, 1), 0.5, 200) : 8);
    }

    public bool HasMask => _owner.SelectedLayer?.IsMasked == true;

    /// <summary>Starts a stroke at a document-space point. Safe to call again while one is running.</summary>
    public void BeginStroke(double documentX, double documentY)
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || layer.IsAdjustment)
        {
            _owner.Status = Strings.MaskSelectPixelLayer;
            return;
        }

        if (_layer is not null) EndStroke();

        _before = layer.Model.Mask is { } existing ? (byte[])existing.Clone() : null;
        if (layer.Model.Mask is null) layer.AssignMask(_masks.Full(_owner.Document.Width, _owner.Document.Height));

        _layer = layer;
        _hasLast = false;
        ContinueStroke(documentX, documentY);
    }

    public void ContinueStroke(double documentX, double documentY)
    {
        if (_layer?.Model.Mask is not { } mask) return;

        var point = ((int)Math.Round(documentX), (int)Math.Round(documentY));

        // MaskService splices the pair with capsules of its own, so this only has to supply the
        // previous event's position.
        var points = _hasLast
            ? new List<(int X, int Y)> { ((int)Math.Round(_lastX), (int)Math.Round(_lastY)), point }
            : [point];

        MaskService.Paint(mask, _owner.Document.Width, _owner.Document.Height, points, _size / 2, _hardness, _opacity, _erase);

        _lastX = point.Item1;
        _lastY = point.Item2;
        _hasLast = true;

        _layer.RefreshMaskState();
        _owner.NotifyChanged();
    }

    /// <summary>Commits the stroke as a single undo entry.</summary>
    public void EndStroke()
    {
        LayerViewModel? layer = _layer;
        _layer = null;
        _hasLast = false;
        if (layer?.Model.Mask is null) return;

        byte[]? after = layer.Model.Mask;
        byte[]? before = _before;
        _before = null;
        if (before is not null && before.AsSpan().SequenceEqual(after)) return;

        _owner.History.Push(new PropertyChange<byte[]?>(Strings.UndoPaintMask, before, after, layer.AssignMask));
        Refresh();
    }

    /// <summary>Throws the in-progress stroke away, used when the tool is switched off mid-drag.</summary>
    public void CancelStroke()
    {
        LayerViewModel? layer = _layer;
        _layer = null;
        _hasLast = false;
        if (layer is null) return;

        layer.AssignMask(_before);
        _before = null;
        _owner.NotifyChanged();
    }

    /// <summary>Softens the whole mask edge. A blur of the mask's own size leaves the interior alone.</summary>
    public void Feather(double radius)
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer?.Model.Mask is not { } mask)
        {
            _owner.Status = Strings.MaskNoMaskToFeather;
            return;
        }

        if (!double.IsFinite(radius) || radius <= 0) return;

        var before = (byte[])mask.Clone();
        MaskService.Feather(mask, _owner.Document.Width, _owner.Document.Height, radius);
        var after = layer.Model.Mask;
        _owner.History.Push(new PropertyChange<byte[]?>(Strings.UndoFeatherMask, before, after, layer.AssignMask));
        layer.RefreshMaskState();
        _owner.NotifyChanged();
        _owner.Status = Strings.MaskFeathered;
    }

    public void Invert()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer?.Model.Mask is not { } mask)
        {
            _owner.Status = Strings.MaskNoMaskToInvert;
            return;
        }

        var before = (byte[])mask.Clone();
        MaskService.Invert(mask);
        var after = layer.Model.Mask;
        _owner.History.Push(new PropertyChange<byte[]?>(Strings.UndoInvertMask, before, after, layer.AssignMask));
        layer.RefreshMaskState();
        _owner.NotifyChanged();
        _owner.Status = Strings.MaskInverted;
    }

    public void Refresh() => OnPropertyChanged(nameof(HasMask));
}
