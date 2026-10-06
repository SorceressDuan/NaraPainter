using NaraDreamPainter.App.Services;
using NaraDreamPainter.Models.Adjustments;
using NaraDreamPainter.Models.Layers;
using NaraDreamPainter.Models.Pixels;
using NaraDreamPainter.Models.Services;

namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// One layer of the stack. Property setters apply straight to the model and hand the undo stack a pair
/// of callbacks that replay either value; the continuous edits (sliders, rename boxes) collapse into a
/// single step through the merge keys.
/// </summary>
public sealed class LayerViewModel : ObservableObject
{
    private readonly Layer _layer;
    private readonly DocumentViewModel _owner;
    private readonly IAdjustmentFilter _filter;
    private readonly AdjustmentSettings?[] _adjustments = new AdjustmentSettings?[AdjustmentKinds.Count];
    private PixelBuffer? _source;

    // Zero means no edit is open, so each change of its own. BeginOpacityEdit moves to a fresh number
    // and every change until the next one shares it, which is what turns a drag into one undo step.
    private int _opacitySession;
    private int _nameSession;
    private int _opacityKey;
    private int _nameKey;

    internal LayerViewModel(Layer layer, DocumentViewModel owner, IAdjustmentFilter filter)
    {
        _layer = layer;
        _owner = owner;
        _filter = filter;

        // The pixels as they came in. Adjustments are recomputed from this buffer, so the stack stays
        // non destructive until the layer is exported.
        _source = layer.Pixels;
        if (layer.Adjustment is not null) _adjustments[(int)AdjustmentKinds.Of(layer.Adjustment)] = layer.Adjustment;
    }

    public Layer Model => _layer;

    internal DocumentViewModel Owner => _owner;

    internal PixelBuffer? Source => _source;

    public bool IsAdjustment => _layer.IsAdjustment;

    public AdjustmentKind? LayerKind => _layer.Adjustment is null ? null : AdjustmentKinds.Of(_layer.Adjustment);

    public string ContentLabel => _layer.Adjustment is not null
        ? AdjustmentKinds.Name(AdjustmentKinds.Of(_layer.Adjustment))
        : _layer.Pixels is null ? Strings.LayersEmpty : $"{_layer.Pixels.Width} × {_layer.Pixels.Height}";

    public string Name
    {
        get => _layer.Name;
        set
        {
            string name = string.IsNullOrWhiteSpace(value) ? Strings.LayersDefaultName : value.Trim();
            if (name == _layer.Name) return;

            string previous = _layer.Name;
            SetName(name);
            _owner.History.Push(new PropertyChange<string>(Strings.UndoRenameLayer, previous, name, SetName, MergeKey("name", _nameSession, ref _nameKey)));
        }
    }

    public bool IsVisible
    {
        get => _layer.IsVisible;
        set
        {
            if (_layer.IsVisible == value) return;

            bool previous = _layer.IsVisible;
            SetVisible(value);
            _owner.History.Push(new PropertyChange<bool>(value ? Strings.UndoShowLayer : Strings.UndoHideLayer, previous, value, SetVisible));
        }
    }

    /// <summary>Layer opacity in percent, which is how the slider and the row label show it.</summary>
    public double Opacity
    {
        get => _layer.Opacity * 100;
        set
        {
            if (!double.IsFinite(value)) return;

            double next = Math.Round(Math.Clamp(value, 0, 100), 1);
            double previous = _layer.Opacity * 100;
            if (Math.Abs(previous - next) < 0.05) return;

            SetOpacity(next);
            _owner.History.Push(new PropertyChange<double>(Strings.UndoLayerOpacity, previous, next, SetOpacity, MergeKey("opacity", _opacitySession, ref _opacityKey)));
        }
    }

    /// <summary>
    /// Its own instance rather than a shared one. Two panels show this picker at the same time, and a
    /// list bound into two controls at once leaves one of them without a selection.
    /// </summary>
    public IReadOnlyList<string> BlendModeNames => Strings.BlendModeNames;

    public int BlendModeIndex
    {
        get => (int)_layer.BlendMode;
        set
        {
            if (value < 0 || value >= BlendModeCatalog.Count) return;

            var mode = (BlendMode)value;
            if (mode == _layer.BlendMode) return;

            BlendMode previous = _layer.BlendMode;
            SetBlendMode(mode);
            _owner.History.Push(new PropertyChange<BlendMode>(Strings.UndoBlendMode, previous, mode, SetBlendMode));
        }
    }

    public bool IsMasked => _layer.Mask is not null;

    public string MaskLabel => _layer.Mask is null ? Strings.MaskNone : Strings.MaskApplied;

    /// <summary>Re-reads the labels that come from resources after a language change.</summary>
    internal void RefreshLocalization()
    {
        OnPropertyChanged(nameof(ContentLabel));
        OnPropertyChanged(nameof(BlendModeNames));
        OnPropertyChanged(nameof(MaskLabel));
    }

    public void BeginOpacityEdit() => _opacitySession++;

    public void BeginNameEdit() => _nameSession++;

    /// <summary>
    /// The key two edits of one property have to share before the history collapses them.
    /// </summary>
    /// <remarks>
    /// An open edit gives every change the same key, so a drag lands as one undo step. With no edit
    /// open each change gets a key of its own, because otherwise a second value set outside a drag -
    /// an arrow key, a typed number - would silently merge into the one before it.
    /// </remarks>
    private string MergeKey(string property, int session, ref int issued)
    {
        if (session > 0) return $"layer:{_layer.Id}:{property}:{session}";

        issued++;
        return $"layer:{_layer.Id}:{property}:solo:{issued}";
    }

    internal AdjustmentSettings? Adjustment(AdjustmentKind kind) => _adjustments[(int)kind];

    /// <summary>
    /// Stores one adjustment of the layer's stack and re-runs the stack over the source pixels. An
    /// adjustment layer holds a single recipe instead, which the compositor applies while flattening.
    /// </summary>
    internal void SetAdjustment(AdjustmentKind kind, AdjustmentSettings? settings)
    {
        if (IsAdjustment)
        {
            if (settings is null || AdjustmentKinds.Of(settings) != LayerKind) return;

            _adjustments[(int)kind] = settings;
            _layer.Adjustment = settings;
            _owner.NotifyChanged();
            return;
        }

        _adjustments[(int)kind] = settings;
        RebuildPixels();
    }

    internal void CopyAdjustmentsFrom(LayerViewModel source)
    {
        Array.Copy(source._adjustments, _adjustments, _adjustments.Length);
        RebuildPixels();
    }

    internal void AssignMask(byte[]? mask)
    {
        _layer.Mask = mask;
        OnPropertyChanged(nameof(IsMasked));
        OnPropertyChanged(nameof(MaskLabel));
        _owner.NotifyChanged();
    }

    internal void RefreshMaskState()
    {
        OnPropertyChanged(nameof(IsMasked));
        OnPropertyChanged(nameof(MaskLabel));
    }

    /// <summary>
    /// Swaps the pixels an edit works from and re-derives the layer. The adjustment stack is replayed
    /// over the new buffer, so undoing a destructive pixel edit also restores what the adjustments
    /// produced from the old one.
    /// </summary>
    internal void ReplacePixels(PixelBuffer pixels)
    {
        _source = pixels;
        RebuildPixels();
    }

    private void SetName(string value)
    {
        _layer.Name = value;
        OnPropertyChanged(nameof(Name));
        _owner.NotifyChanged();
    }

    private void SetVisible(bool value)
    {
        _layer.IsVisible = value;
        OnPropertyChanged(nameof(IsVisible));
        _owner.NotifyChanged();
    }

    private void SetOpacity(double percent)
    {
        _layer.Opacity = Math.Clamp(percent, 0, 100) / 100.0;
        OnPropertyChanged(nameof(Opacity));
        _owner.NotifyChanged();
    }

    private void SetBlendMode(BlendMode mode)
    {
        _layer.BlendMode = mode;
        OnPropertyChanged(nameof(BlendModeIndex));
        _owner.NotifyChanged();
    }

    private void RebuildPixels()
    {
        if (_source is null) return;

        var stack = new List<AdjustmentSettings>();
        foreach (AdjustmentSettings? settings in _adjustments)
        {
            if (settings is not null) stack.Add(settings);
        }

        _layer.Pixels = stack.Count == 0 ? _source : _filter.ApplyAll(_source, stack);
        OnPropertyChanged(nameof(ContentLabel));
        _owner.NotifyChanged();
    }
}
