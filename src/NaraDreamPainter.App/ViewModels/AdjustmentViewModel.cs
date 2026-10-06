using System.Collections.ObjectModel;
using NaraDreamPainter.App.Services;
using NaraDreamPainter.Models.Adjustments;

namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// Drives the adjustment controls for the selected layer. Every edit lands on that layer's stack, and
/// the values are mirrored here so sliders, number boxes and the curve list all read one source.
/// A drag is a single undo step: BeginEdit opens a new merge key, and the edits during the drag fold
/// into the entry that key produced.
/// </summary>
public sealed class AdjustmentViewModel : ObservableObject
{
    private readonly RangeAdjustment[] _ranges = new RangeAdjustment[HueSaturationSettings.RangeCount];
    private readonly LevelRange[] _levels = new LevelRange[LevelsChannelCount];
    private readonly ObservableCollection<CurvePointViewModel> _curvePoints = [];

    private const int LevelsChannelCount = 4;

    private LayerViewModel? _target;
    private CurvesSettings _curves = new();
    private double _brightness;
    private double _contrast;
    private bool _colorize;
    private int _rangeIndex;
    private int _levelsChannelIndex;
    private int _curveChannelIndex;
    private int _selectedCurvePointIndex = -1;
    private int _session;

    /// <summary>Rebuilt on each read so a language change is picked up; order matches ColorRange.</summary>
    public IReadOnlyList<string> RangeNames => LocalizedLists.RangeNames;

    /// <summary>Order matches LevelsChannel and CurvesSettings' channel order.</summary>
    public IReadOnlyList<string> ChannelNames => LocalizedLists.ChannelNames;

    public LayerViewModel? Target
    {
        get => _target;
        set
        {
            if (ReferenceEquals(_target, value)) return;

            _target = value;
            Reload();
        }
    }

    public string TargetLabel => _target is null ? Strings.PropertiesNoLayer : $"{_target.Name} · {_target.ContentLabel}";

    public bool CanEditBrightnessContrast => CanEdit(AdjustmentKind.BrightnessContrast);

    public bool CanEditHueSaturation => CanEdit(AdjustmentKind.HueSaturation);

    public bool CanEditLevels => CanEdit(AdjustmentKind.Levels);

    public bool CanEditCurves => CanEdit(AdjustmentKind.Curves);

    public double Brightness
    {
        get => _brightness;
        set
        {
            if (!double.IsFinite(value)) return;

            double next = Math.Round(Math.Clamp(value, BrightnessContrastSettings.MinValue, BrightnessContrastSettings.MaxValue), 1);
            if (Math.Abs(_brightness - next) < 0.05) return;

            _brightness = next;
            OnPropertyChanged();
            ApplyBrightnessContrast();
        }
    }

    public double Contrast
    {
        get => _contrast;
        set
        {
            if (!double.IsFinite(value)) return;

            double next = Math.Round(Math.Clamp(value, BrightnessContrastSettings.MinValue, BrightnessContrastSettings.MaxValue), 1);
            if (Math.Abs(_contrast - next) < 0.05) return;

            _contrast = next;
            OnPropertyChanged();
            ApplyBrightnessContrast();
        }
    }

    public int RangeIndex
    {
        get => _rangeIndex;
        set
        {
            if (value < 0 || value >= _ranges.Length || _rangeIndex == value) return;

            _rangeIndex = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Hue));
            OnPropertyChanged(nameof(Saturation));
            OnPropertyChanged(nameof(Lightness));
        }
    }

    public double Hue
    {
        get => _ranges[_rangeIndex].Hue;
        set
        {
            if (double.IsFinite(value)) SetRange(hue: value);
        }
    }

    public double Saturation
    {
        get => _ranges[_rangeIndex].Saturation;
        set
        {
            if (double.IsFinite(value)) SetRange(saturation: value);
        }
    }

    public double Lightness
    {
        get => _ranges[_rangeIndex].Lightness;
        set
        {
            if (double.IsFinite(value)) SetRange(lightness: value);
        }
    }

    public bool Colorize
    {
        get => _colorize;
        set
        {
            if (_colorize == value) return;

            _colorize = value;
            OnPropertyChanged();
            ApplyHueSaturation();
        }
    }

    public int LevelsChannelIndex
    {
        get => _levelsChannelIndex;
        set
        {
            if (value < 0 || value >= _levels.Length || _levelsChannelIndex == value) return;

            _levelsChannelIndex = value;
            OnPropertyChanged();
            RaiseLevelValues();
        }
    }

    public double LevelBlack
    {
        get => _levels[_levelsChannelIndex].Black;
        set
        {
            if (double.IsFinite(value)) SetLevel(range => range with { Black = value });
        }
    }

    public double LevelWhite
    {
        get => _levels[_levelsChannelIndex].White;
        set
        {
            if (double.IsFinite(value)) SetLevel(range => range with { White = value });
        }
    }

    public double LevelGamma
    {
        get => _levels[_levelsChannelIndex].Gamma;
        set
        {
            if (double.IsFinite(value)) SetLevel(range => range with { Gamma = value });
        }
    }

    public double LevelOutputBlack
    {
        get => _levels[_levelsChannelIndex].OutputBlack;
        set
        {
            if (double.IsFinite(value)) SetLevel(range => range with { OutputBlack = value });
        }
    }

    public double LevelOutputWhite
    {
        get => _levels[_levelsChannelIndex].OutputWhite;
        set
        {
            if (double.IsFinite(value)) SetLevel(range => range with { OutputWhite = value });
        }
    }

    public ObservableCollection<CurvePointViewModel> CurvePoints => _curvePoints;

    public int CurveChannelIndex
    {
        get => _curveChannelIndex;
        set
        {
            if (value < 0 || value >= CurvesSettings.ChannelCount || _curveChannelIndex == value) return;

            _curveChannelIndex = value;
            OnPropertyChanged();
            LoadCurvePoints();
        }
    }

    public int SelectedCurvePointIndex
    {
        get => _selectedCurvePointIndex;
        set
        {
            if (SetProperty(ref _selectedCurvePointIndex, value)) OnPropertyChanged(nameof(CanRemoveCurvePoint));
        }
    }

    public bool CanRemoveCurvePoint =>
        _selectedCurvePointIndex >= 0
        && _selectedCurvePointIndex < _curvePoints.Count
        && !_curvePoints[_selectedCurvePointIndex].IsEndpoint;

    /// <summary>Opens a new merge key, so the edits that follow leave one undo step behind.</summary>
    public void BeginEdit() => _session++;

    public void Reset(AdjustmentKind kind)
    {
        if (!CanEdit(kind) || _target is null) return;

        AdjustmentSettings? settings = _target.IsAdjustment ? AdjustmentKinds.Create(kind) : null;
        Store(kind, settings, Localization.Interpolate(Strings.UndoResetAdjustment, AdjustmentKinds.Name(kind)));
        Reload();
    }

    public void AddCurvePoint()
    {
        if (_curvePoints.Count >= CurvesSettings.MaxPoints) return;

        int gap = -1;
        double widest = 0;
        for (int i = 0; i < _curvePoints.Count - 1; i++)
        {
            double span = _curvePoints[i + 1].X - _curvePoints[i].X;
            if (span > widest)
            {
                widest = span;
                gap = i;
            }
        }

        if (gap < 0 || widest < 2) return;

        double x = Math.Round((_curvePoints[gap].X + _curvePoints[gap + 1].X) / 2);
        double y = Math.Round((_curvePoints[gap].Y + _curvePoints[gap + 1].Y) / 2);

        var points = _curvePoints.Select(point => point.ToPoint()).ToList();
        points.Insert(gap + 1, new CurvePoint(x, y));
        if (!ApplyCurve(points)) return;

        LoadCurvePoints();
        SelectedCurvePointIndex = gap + 1;
    }

    public void RemoveCurvePoint()
    {
        if (!CanRemoveCurvePoint) return;

        var points = _curvePoints.Select(point => point.ToPoint()).ToList();
        points.RemoveAt(_selectedCurvePointIndex);
        if (!ApplyCurve(points)) return;

        LoadCurvePoints();
    }

    internal bool TryMovePoint(CurvePointViewModel point, double x, double y)
    {
        int index = _curvePoints.IndexOf(point);
        if (index < 0) return false;

        double targetX = point.X;
        double targetY = Math.Clamp(y, 0, 255);
        if (!point.IsEndpoint)
        {
            double lower = _curvePoints[index - 1].X + 1;
            double upper = _curvePoints[index + 1].X - 1;
            if (upper < lower) return false;

            targetX = Math.Clamp(x, lower, upper);
        }

        var points = _curvePoints
            .Select(current => ReferenceEquals(current, point) ? new CurvePoint(targetX, targetY) : current.ToPoint())
            .ToList();
        return ApplyCurve(points);
    }

    private bool CanEdit(AdjustmentKind kind) => _target is not null && (!_target.IsAdjustment || _target.LayerKind == kind);

    private void SetRange(double? hue = null, double? saturation = null, double? lightness = null)
    {
        RangeAdjustment current = _ranges[_rangeIndex];
        var next = new RangeAdjustment(
            Math.Clamp(hue ?? current.Hue, -180, 180),
            Math.Clamp(saturation ?? current.Saturation, -100, 100),
            Math.Clamp(lightness ?? current.Lightness, -100, 100));
        if (next == current) return;

        _ranges[_rangeIndex] = next;
        OnPropertyChanged(nameof(Hue));
        OnPropertyChanged(nameof(Saturation));
        OnPropertyChanged(nameof(Lightness));
        ApplyHueSaturation();
    }

    private void SetLevel(Func<LevelRange, LevelRange> change)
    {
        LevelRange current = _levels[_levelsChannelIndex];
        LevelRange next = change(current).Normalized();
        if (next == current) return;

        _levels[_levelsChannelIndex] = next;
        RaiseLevelValues();
        ApplyLevels();
    }

    private void RaiseLevelValues()
    {
        OnPropertyChanged(nameof(LevelBlack));
        OnPropertyChanged(nameof(LevelWhite));
        OnPropertyChanged(nameof(LevelGamma));
        OnPropertyChanged(nameof(LevelOutputBlack));
        OnPropertyChanged(nameof(LevelOutputWhite));
    }

    private void ApplyBrightnessContrast()
    {
        var settings = new BrightnessContrastSettings(_brightness, _contrast);
        Store(AdjustmentKind.BrightnessContrast, settings.IsIdentity ? null : settings, Strings.AdjustBrightnessContrast);
    }

    private void ApplyHueSaturation()
    {
        HueSaturationSettings settings = BuildHueSaturation();
        Store(AdjustmentKind.HueSaturation, !_colorize && settings.IsIdentity ? null : settings, Strings.AdjustHueSaturation);
    }

    private HueSaturationSettings BuildHueSaturation()
    {
        var settings = new HueSaturationSettings { Colorize = _colorize };
        for (int i = 0; i < _ranges.Length; i++)
        {
            if (_ranges[i] != default) settings = settings.With((ColorRange)i, _ranges[i]);
        }
        return settings;
    }

    private void ApplyLevels()
    {
        var settings = new LevelsSettings(_levels[0], _levels[1], _levels[2], _levels[3]);
        Store(AdjustmentKind.Levels, settings.IsIdentity ? null : settings, Strings.AdjustLevels);
    }

    private bool ApplyCurve(IReadOnlyList<CurvePoint> points)
    {
        try
        {
            CurvesSettings updated = _curves.With((LevelsChannel)_curveChannelIndex, points);
            _curves = updated;
            Store(AdjustmentKind.Curves, updated.IsIdentity ? null : updated, Strings.AdjustCurves);
            return true;
        }
        catch (ArgumentException)
        {
            // With() rejects handles that are out of order, which is how a drag past a neighbour is
            // refused: the caller keeps the value it had.
            return false;
        }
    }

    private void LoadCurvePoints()
    {
        _curvePoints.Clear();
        IReadOnlyList<CurvePoint> points = _curves.Points((LevelsChannel)_curveChannelIndex);
        for (int i = 0; i < points.Count; i++)
        {
            _curvePoints.Add(new CurvePointViewModel(points[i], i == 0 || i == points.Count - 1, this));
        }

        SelectedCurvePointIndex = -1;
    }

    /// <summary>Applies one edit and records it. The undo callback carries the layer it was made on.</summary>
    private void Store(AdjustmentKind kind, AdjustmentSettings? settings, string label)
    {
        LayerViewModel? target = _target;
        if (target is null) return;

        AdjustmentSettings? before = target.Adjustment(kind);
        target.SetAdjustment(kind, settings);
        AdjustmentSettings? after = target.Adjustment(kind);
        if (Equals(before, after)) return;

        target.Owner.History.Push(new PropertyChange<AdjustmentSettings?>(
            label,
            before,
            after,
            value =>
            {
                target.SetAdjustment(kind, value);
                if (ReferenceEquals(_target, target)) Reload();
            },
            $"adjust:{target.Model.Id}:{kind}:{_session}"));
    }

    private void Reload()
    {
        var brightness = _target?.Adjustment(AdjustmentKind.BrightnessContrast) as BrightnessContrastSettings
            ?? new BrightnessContrastSettings();
        _brightness = brightness.Brightness;
        _contrast = brightness.Contrast;

        var hue = _target?.Adjustment(AdjustmentKind.HueSaturation) as HueSaturationSettings;
        _colorize = hue?.Colorize ?? false;
        for (int i = 0; i < _ranges.Length; i++) _ranges[i] = hue is null ? default : hue[(ColorRange)i];

        var levels = _target?.Adjustment(AdjustmentKind.Levels) as LevelsSettings;
        for (int i = 0; i < _levels.Length; i++) _levels[i] = levels is null ? LevelRange.Identity : levels[(LevelsChannel)i];

        _curves = _target?.Adjustment(AdjustmentKind.Curves) as CurvesSettings ?? new CurvesSettings();
        LoadCurvePoints();

        OnPropertyChanged(string.Empty);
    }

    /// <summary>Re-reads the labels that come from resources after a language change.</summary>
    public void RefreshLocalization()
    {
        foreach (CurvePointViewModel point in _curvePoints) point.RefreshLocalization();
        OnPropertyChanged(string.Empty);
    }
}
