using NaraPainter.Models.Pixels;

namespace NaraPainter.App.ViewModels;

/// <summary>Reads the colour under a point on the canvas and shows it as hex and as channels.</summary>
/// <remarks>
/// The sample comes from the flattened document rather than the selected layer, because that is what
/// the user is looking at: a pixel that reads one colour on screen and another in the panel would just
/// look broken.
/// </remarks>
public sealed class ColorPickerViewModel : ObservableObject
{
    private readonly DocumentViewModel _owner;
    private Rgba32 _sample;
    private bool _isActive;
    private bool _hasSample;

    internal ColorPickerViewModel(DocumentViewModel owner) => _owner = owner;

    /// <summary>While this is on, a click on the canvas samples instead of painting.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (!SetProperty(ref _isActive, value)) return;
            if (!value) return;

            // The two tools both want the pointer, and the mask brush is the one that can be left on
            // by accident, so switching to the picker turns it off.
            _owner.MaskBrush.IsActive = false;
        }
    }

    public bool HasSample
    {
        get => _hasSample;
        private set
        {
            if (SetProperty(ref _hasSample, value)) OnPropertyChanged(nameof(Hex));
        }
    }

    public Rgba32 Sample
    {
        get => _sample;
        private set
        {
            _sample = value;
            OnPropertyChanged(nameof(Sample));
            OnPropertyChanged(nameof(Hex));
            OnPropertyChanged(nameof(Channels));
        }
    }

    public string Hex => _hasSample ? $"#{_sample.R:X2}{_sample.G:X2}{_sample.B:X2}" : string.Empty;

    public string Channels => _hasSample ? $"R {_sample.R}  G {_sample.G}  B {_sample.B}" : string.Empty;

    /// <summary>Samples the composited image at a point in document coordinates.</summary>
    public void Pick(double documentX, double documentY)
    {
        PixelBuffer canvas = _owner.Document.Flatten();
        int x = (int)Math.Floor(documentX);
        int y = (int)Math.Floor(documentY);
        if ((uint)x >= (uint)canvas.Width || (uint)y >= (uint)canvas.Height) return;

        Sample = canvas[x, y];
        HasSample = true;
        _owner.Status = Localization.Interpolate(Strings.StatusPickedColor, Hex);
    }
}
