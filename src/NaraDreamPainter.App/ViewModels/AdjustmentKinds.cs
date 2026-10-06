using NaraDreamPainter.Models.Adjustments;

namespace NaraDreamPainter.App.ViewModels;

public enum AdjustmentKind
{
    BrightnessContrast,
    HueSaturation,
    Levels,
    Curves
}

public static class AdjustmentKinds
{
    /// <summary>Number of kinds, used to size the per-layer adjustment slots.</summary>
    public const int Count = 4;

    public static AdjustmentKind Of(AdjustmentSettings settings) => settings switch
    {
        BrightnessContrastSettings => AdjustmentKind.BrightnessContrast,
        HueSaturationSettings => AdjustmentKind.HueSaturation,
        LevelsSettings => AdjustmentKind.Levels,
        CurvesSettings => AdjustmentKind.Curves,
        _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.GetType().Name, "Unknown adjustment type.")
    };

    public static AdjustmentSettings Create(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.BrightnessContrast => new BrightnessContrastSettings(),
        AdjustmentKind.HueSaturation => new HueSaturationSettings(),
        AdjustmentKind.Levels => new LevelsSettings(),
        AdjustmentKind.Curves => new CurvesSettings(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public static string Name(AdjustmentKind kind) => kind switch
    {
        AdjustmentKind.BrightnessContrast => Strings.AdjustBrightnessContrast,
        AdjustmentKind.HueSaturation => Strings.AdjustHueSaturation,
        AdjustmentKind.Levels => Strings.AdjustLevels,
        AdjustmentKind.Curves => Strings.AdjustCurves,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
}
