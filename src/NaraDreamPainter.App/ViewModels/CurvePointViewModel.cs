using NaraDreamPainter.Models.Adjustments;

namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// One handle of a curve channel. Edits are routed through the adjustment view model, which validates
/// the handle order and rebuilds the whole channel; a rejected edit leaves the handle where it was.
/// </summary>
public sealed class CurvePointViewModel : ObservableObject
{
    private readonly AdjustmentViewModel _owner;
    private double _x;
    private double _y;

    internal CurvePointViewModel(CurvePoint point, bool isEndpoint, AdjustmentViewModel owner)
    {
        _owner = owner;
        _x = point.X;
        _y = point.Y;
        IsEndpoint = isEndpoint;
    }

    public bool IsEndpoint { get; }

    /// <summary>The two ends stay pinned at 0 and 255; only interior handles can be moved sideways.</summary>
    public bool CanEditX => !IsEndpoint;

    public string Label => IsEndpoint
        ? (_x <= 0 ? Strings.CurveShadows : Strings.CurveHighlights)
        : Localization.Format(Strings.CurvePointFormat, Math.Round(_x));

    internal void RefreshLocalization() => OnPropertyChanged(nameof(Label));

    public double X
    {
        get => _x;
        set
        {
            if (IsEndpoint || !double.IsFinite(value) || Math.Abs(_x - value) < 0.5) return;

            double x = Math.Round(value);
            if (!_owner.TryMovePoint(this, x, _y)) return;

            _x = x;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Label));
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (!double.IsFinite(value)) return;

            double y = Math.Clamp(Math.Round(value), 0, 255);
            if (Math.Abs(_y - y) < 0.5) return;
            if (!_owner.TryMovePoint(this, _x, y)) return;

            _y = y;
            OnPropertyChanged();
        }
    }

    internal CurvePoint ToPoint() => new(_x, _y);
}
