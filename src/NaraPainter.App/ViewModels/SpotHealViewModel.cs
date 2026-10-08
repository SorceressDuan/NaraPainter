using NaraPainter.App.Services;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// Spot healing: paints a coverage mask over the selected layer as the pointer drags, then rebuilds
/// the covered pixels from the texture around them when the stroke ends.
/// </summary>
/// <remarks>
/// The work happens on release rather than per sample, so a drag of any length is one history entry
/// and the kernel sees the finished stroke instead of a growing one. That is what upstream does too
/// (<c>legacy/Compositor/Document/BrushStroke.swift</c>, <c>heal()</c>). Until release the canvas
/// still shows the untouched layer: this only carries the coverage.
/// </remarks>
public sealed class SpotHealViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private readonly Random _seeds = new();
    private byte[]? _coverage;
    private LayerViewModel? _layer;
    private int _coverageWidth;
    private int _coverageHeight;
    private bool _isActive;
    private int _size = 40;
    private double _hardness = 1;
    private double _opacity = 1;
    private int _mode = SpotHeal.ContentAware;
    private double _lastX;
    private double _lastY;
    private bool _hasLast;

    internal SpotHealViewModel(DocumentViewModel owner)
    {
        _owner = owner;
    }

    /// <summary>True while spot healing owns the pointer.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!SetProperty(ref _isActive, value)) return;

            if (!value)
            {
                CancelStroke();
                return;
            }

            // The mask brush and the picker also want the pointer. Turn both off so a healing drag
            // cannot land on a mask or read a colour by accident.
            _owner.MaskBrush.IsActive = false;
            _owner.ColorPicker.IsActive = false;
            _owner.Status = Strings.SpotHealOn;
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
        set => SetProperty(ref _hardness, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1);
    }

    /// <summary>How much of the healed result replaces what was there, 0 to 1.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1);
    }

    /// <summary>One of the <see cref="SpotHeal"/> modes, as the picker's index.</summary>
    public int ModeIndex
    {
        get => _mode;
        set
        {
            if (value < 0 || value > SpotHeal.ProximityMatch) return;
            SetProperty(ref _mode, value);
        }
    }

    /// <summary>Mode labels for the picker, in <see cref="SpotHeal"/> order.</summary>
    public IReadOnlyList<string> ModeNames => LocalizedLists.SpotHealModes;

    public bool CanHeal => _owner.SelectedLayer is { IsAdjustment: false, Source: not null };

    /// <summary>Starts a stroke at a document-space point. Safe to call again while one is running.</summary>
    public void BeginStroke(double documentX, double documentY)
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || layer.IsAdjustment || layer.Source is null)
        {
            _owner.Status = Strings.SpotHealSelectPixelLayer;
            return;
        }

        if (_layer is not null) EndStroke();

        // The mask has to be the size of the layer's own pixels, not the canvas: a cropped or
        // resized layer is smaller than the document and the kernel works in its coordinates.
        _coverageWidth = layer.SourceWidth;
        _coverageHeight = layer.SourceHeight;
        _coverage = new byte[checked(_coverageWidth * _coverageHeight)];
        _layer = layer;
        _hasLast = false;
        ContinueStroke(documentX, documentY);
    }

    public void ContinueStroke(double documentX, double documentY)
    {
        if (_layer?.Source is null || _coverage is null) return;

        // Pointer coordinates are document space and the mask is layer space. They only agree when the
        // layer covers the canvas; a layer left smaller by an edit is skipped rather than smeared along
        // its edge, which is the honest reading of a point that is not over the layer at all.
        if (_coverageWidth != _owner.Document.Width || _coverageHeight != _owner.Document.Height)
        {
            _lastX = documentX;
            _lastY = documentY;
            _hasLast = true;
            return;
        }

        var point = ((int)Math.Round(documentX), (int)Math.Round(documentY));

        // MaskService splices the pair with capsules of its own, so this only has to supply the
        // previous event's position.
        var points = _hasLast
            ? new List<(int X, int Y)> { ((int)Math.Round(_lastX), (int)Math.Round(_lastY)), point }
            : [point];

        MaskService.Paint(_coverage, _coverageWidth, _coverageHeight, points, _size / 2, _hardness, _opacity, erase: false);

        _lastX = documentX;
        _lastY = documentY;
        _hasLast = true;
    }

    /// <summary>Heals the stroke as a single undo entry.</summary>
    public void EndStroke()
    {
        LayerViewModel? layer = _layer;
        byte[]? coverage = _coverage;
        _layer = null;
        _coverage = null;
        _coverageWidth = 0;
        _coverageHeight = 0;
        _hasLast = false;

        if (layer?.Source is not { } source || coverage is null) return;

        PixelBuffer? healed = SpotHealBrush.Heal(source, coverage, (float)_opacity, _mode, (uint)_seeds.Next());

        if (healed is null)
        {
            _owner.Status = Strings.SpotHealNothing;
            return;
        }

        PixelBuffer before = source;
        layer.ReplacePixels(healed);
        _owner.History.Push(new PropertyChange<PixelBuffer>(Strings.UndoSpotHealing, before, healed, layer.ReplacePixels));
        _owner.Status = Strings.SpotHealDone;
    }

    /// <summary>Throws the in-progress stroke away, used when the tool is switched off mid-drag.</summary>
    public void CancelStroke()
    {
        _layer = null;
        _coverage = null;
        _coverageWidth = 0;
        _coverageHeight = 0;
        _hasLast = false;
    }

    public void Refresh() => OnPropertyChanged(nameof(CanHeal));
}
