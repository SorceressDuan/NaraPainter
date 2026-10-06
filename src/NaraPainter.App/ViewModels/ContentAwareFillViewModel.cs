using NaraPainter.App.Services;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Services;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// Content-aware fill. The selection decides what gets replaced: with one it fills that region, and
/// with none it fills wherever the selected layer's mask hides pixels, which is how a painted mask
/// turns into something the fill can work on.
/// </summary>
public sealed class ContentAwareFillViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private readonly ISelectionMaskBuilder _masks;
    private double _radius = 3;
    private bool _useNikolai;

    internal ContentAwareFillViewModel(DocumentViewModel owner, ISelectionMaskBuilder masks)
    {
        _owner = owner;
        _masks = masks;
    }

    /// <summary>How far around the hole OpenCV samples from, in pixels.</summary>
    public double Radius
    {
        get => _radius;
        set => SetProperty(ref _radius, double.IsFinite(value) ? Math.Clamp(Math.Round(value, 1), 1, 50) : 3);
    }

    /// <summary>OpenCV's two inpainting methods differ in how they propagate texture; Telea is the default.</summary>
    public bool UseNikolai
    {
        get => _useNikolai;
        set => SetProperty(ref _useNikolai, value);
    }

    public bool CanFill => _owner.SelectedLayer is { IsAdjustment: false, Model.Pixels: not null };

    public void Fill()
    {
        LayerViewModel? layer = _owner.SelectedLayer;
        if (layer is null || layer.IsAdjustment || layer.Source is null)
        {
            _owner.Status = Strings.FillSelectPixelLayer;
            return;
        }

        byte[]? holes = BuildHoles();
        if (holes is null)
        {
            _owner.Status = Strings.FillNeedsTarget;
            return;
        }

        if (!HasHoles(holes))
        {
            _owner.Status = Strings.FillNothingToFill;
            return;
        }

        PixelBuffer before = layer.Source;
        PixelBuffer after = ContentAwareFill.Fill(before, holes, _radius, _useNikolai ? 1 : 0);
        layer.ReplacePixels(after);

        _owner.History.Push(new PropertyChange<PixelBuffer>(Strings.UndoContentFill, before, after, layer.ReplacePixels));
        _owner.NotifyChanged();
        _owner.Status = Strings.FillDone;
    }

    public void Refresh() => OnPropertyChanged(nameof(CanFill));

    /// <summary>
    /// The coverage the fill treats as the hole: the selection when there is one, otherwise the
    /// inverse of the layer mask, because the masked-out pixels are the ones that need inventing.
    /// </summary>
    private byte[]? BuildHoles()
    {
        int width = _owner.Document.Width;
        int height = _owner.Document.Height;

        if (_owner.Selection.HasRegion)
        {
            var region = new SelectionRegion(
                _owner.Selection.Shape,
                (int)_owner.Selection.X,
                (int)_owner.Selection.Y,
                (int)_owner.Selection.Width,
                (int)_owner.Selection.Height);
            if (!region.IsEmpty) return _masks.Build(region, width, height, _owner.Selection.Feather);
        }

        if (_owner.SelectedLayer?.Model.Mask is { } mask && mask.Length == width * height)
        {
            var holes = new byte[mask.Length];
            for (int i = 0; i < mask.Length; i++) holes[i] = (byte)(255 - mask[i]);
            return holes;
        }

        return null;
    }

    private static bool HasHoles(byte[] holes)
    {
        foreach (byte value in holes)
        {
            if (value > 0) return true;
        }
        return false;
    }
}
