using System.ComponentModel;
using System.Reflection;

namespace NaraPainter.App;

/// <summary>
/// The XAML face of <see cref="Strings"/>: one property per resource key, kept alive as a singleton and
/// bound declaratively, so XAML files hold no user-visible text of their own.
/// </summary>
/// <remarks>
/// Views bind these as <c>{x:Bind Text.ToolbarOpen, Mode=OneWay}</c>; the path is compiled, so a key
/// that does not exist is a build error rather than a blank label. View models reach the same object
/// through the Text property on their base class. On a language change every property raises
/// PropertyChanged, which is what makes the switch take effect without rebuilding the window.
/// </remarks>
public sealed class LocalizedStrings : INotifyPropertyChanged
{
    // Instance properties only: a static member such as Instance itself is not a resource key.
    private static readonly string[] Names =
    [
        .. typeof(LocalizedStrings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => property.Name)
    ];

    private LocalizedStrings() => Localization.CultureChanged += (_, _) => Refresh();

    public static LocalizedStrings Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    // Window, status bar and file names.
    public string AppTitle => Strings.AppTitle;

    public string AppUntitled => Strings.AppUntitled;

    public string AppWindowTitle => Strings.AppWindowTitle;

    public string AppModifiedMark => Strings.AppModifiedMark;

    public string CommonOn => Strings.CommonOn;

    public string CommonOff => Strings.CommonOff;

    public string StatusReady => Strings.StatusReady;

    public string StatusOpened => Strings.StatusOpened;

    public string StatusExported => Strings.StatusExported;

    public string StatusImported => Strings.StatusImported;

    public string StatusLayerAdded => Strings.StatusLayerAdded;

    public string StatusAdjustmentLayerAdded => Strings.StatusAdjustmentLayerAdded;

    public string StatusLayerDuplicated => Strings.StatusLayerDuplicated;

    public string StatusLayerDeleted => Strings.StatusLayerDeleted;

    public string StatusLayersReordered => Strings.StatusLayersReordered;

    public string StatusOneLayer => Strings.StatusOneLayer;

    public string StatusLayerCount => Strings.StatusLayerCount;

    // Toolbar.
    public string ToolbarOpen => Strings.ToolbarOpen;

    public string ToolbarOpenHint => Strings.ToolbarOpenHint;

    public string ToolbarExport => Strings.ToolbarExport;

    public string ToolbarExportHint => Strings.ToolbarExportHint;

    public string ToolbarUndo => Strings.ToolbarUndo;

    public string ToolbarUndoHint => Strings.ToolbarUndoHint;

    public string ToolbarRedo => Strings.ToolbarRedo;

    public string ToolbarRedoHint => Strings.ToolbarRedoHint;

    public string ToolbarNewLayer => Strings.ToolbarNewLayer;

    public string ToolbarNewLayerHint => Strings.ToolbarNewLayerHint;

    public string ToolbarDuplicate => Strings.ToolbarDuplicate;

    public string ToolbarDuplicateHint => Strings.ToolbarDuplicateHint;

    public string ToolbarDeleteLayer => Strings.ToolbarDeleteLayer;

    public string ToolbarDeleteHint => Strings.ToolbarDeleteHint;

    public string ToolbarAdjustmentLayer => Strings.ToolbarAdjustmentLayer;

    public string ToolbarAdjustmentLayerHint => Strings.ToolbarAdjustmentLayerHint;

    public string ToolbarZoomOut => Strings.ToolbarZoomOut;

    public string ToolbarZoomOutHint => Strings.ToolbarZoomOutHint;

    public string ToolbarZoomIn => Strings.ToolbarZoomIn;

    public string ToolbarZoomInHint => Strings.ToolbarZoomInHint;

    public string ToolbarFit => Strings.ToolbarFit;

    public string ToolbarFitHint => Strings.ToolbarFitHint;

    public string ToolbarActualSize => Strings.ToolbarActualSize;

    public string ToolbarActualSizeHint => Strings.ToolbarActualSizeHint;

    public string ToolbarMaskBrush => Strings.ToolbarMaskBrush;

    public string ToolbarMaskBrushHint => Strings.ToolbarMaskBrushHint;

    public string ToolbarContentAwareFill => Strings.ToolbarContentAwareFill;

    public string ToolbarContentAwareFillHint => Strings.ToolbarContentAwareFillHint;

    public string ToolbarFillSelection => Strings.ToolbarFillSelection;

    public string ToolbarFeatherMask => Strings.ToolbarFeatherMask;

    public string ToolbarInvertMask => Strings.ToolbarInvertMask;

    public string ToolbarClearMask => Strings.ToolbarClearMask;

    // Layers panel.
    public string LayersTitle => Strings.LayersTitle;

    public string LayersVisibilityHint => Strings.LayersVisibilityHint;

    public string LayersAdjustmentBadge => Strings.LayersAdjustmentBadge;

    public string LayersOpacity => Strings.LayersOpacity;

    public string LayersAddHint => Strings.LayersAddHint;

    public string LayersDuplicateHint => Strings.LayersDuplicateHint;

    public string LayersDeleteHint => Strings.LayersDeleteHint;

    public string LayersMoveUpHint => Strings.LayersMoveUpHint;

    public string LayersMoveDownHint => Strings.LayersMoveDownHint;

    public string LayersEmpty => Strings.LayersEmpty;

    public string LayersDefaultName => Strings.LayersDefaultName;

    public string LayersNameFormat => Strings.LayersNameFormat;

    public string LayersCopyNameFormat => Strings.LayersCopyNameFormat;

    public string LayersImportHint => Strings.LayersImportHint;

    public string LayersImportFailed => Strings.LayersImportFailed;

    // Properties panel.
    public string PropertiesTitle => Strings.PropertiesTitle;

    public string PropertiesLayer => Strings.PropertiesLayer;

    public string PropertiesName => Strings.PropertiesName;

    public string PropertiesBlendMode => Strings.PropertiesBlendMode;

    public string PropertiesOpacity => Strings.PropertiesOpacity;

    public string PropertiesVisible => Strings.PropertiesVisible;

    public string PropertiesAdjustments => Strings.PropertiesAdjustments;

    public string PropertiesSelectionAndMask => Strings.PropertiesSelectionAndMask;

    public string PropertiesNoLayer => Strings.PropertiesNoLayer;

    public string PropertiesShape => Strings.PropertiesShape;

    public string PropertiesX => Strings.PropertiesX;

    public string PropertiesY => Strings.PropertiesY;

    public string PropertiesWidth => Strings.PropertiesWidth;

    public string PropertiesHeight => Strings.PropertiesHeight;

    public string PropertiesFeather => Strings.PropertiesFeather;

    public string PropertiesApplyAsMask => Strings.PropertiesApplyAsMask;

    public string PropertiesSelectAll => Strings.PropertiesSelectAll;

    public string PropertiesClearMask => Strings.PropertiesClearMask;

    public string PropertiesReset => Strings.PropertiesReset;

    public string PropertiesAddPoint => Strings.PropertiesAddPoint;

    public string PropertiesRemovePoint => Strings.PropertiesRemovePoint;

    public string PropertiesChannel => Strings.PropertiesChannel;

    public string PropertiesRange => Strings.PropertiesRange;

    public string PropertiesBrightness => Strings.PropertiesBrightness;

    public string PropertiesContrast => Strings.PropertiesContrast;

    public string PropertiesHue => Strings.PropertiesHue;

    public string PropertiesSaturation => Strings.PropertiesSaturation;

    public string PropertiesLightness => Strings.PropertiesLightness;

    public string PropertiesColorize => Strings.PropertiesColorize;

    public string PropertiesBlackPoint => Strings.PropertiesBlackPoint;

    public string PropertiesWhitePoint => Strings.PropertiesWhitePoint;

    public string PropertiesGamma => Strings.PropertiesGamma;

    public string PropertiesOutputBlack => Strings.PropertiesOutputBlack;

    public string PropertiesOutputWhite => Strings.PropertiesOutputWhite;

    // Adjustment names.
    public string AdjustBrightnessContrast => Strings.AdjustBrightnessContrast;

    public string AdjustHueSaturation => Strings.AdjustHueSaturation;

    public string AdjustLevels => Strings.AdjustLevels;

    public string AdjustCurves => Strings.AdjustCurves;

    public string ChannelRgb => Strings.ChannelRgb;

    public string ChannelRed => Strings.ChannelRed;

    public string ChannelGreen => Strings.ChannelGreen;

    public string ChannelBlue => Strings.ChannelBlue;

    public string RangeMaster => Strings.RangeMaster;

    public string RangeReds => Strings.RangeReds;

    public string RangeYellows => Strings.RangeYellows;

    public string RangeGreens => Strings.RangeGreens;

    public string RangeCyans => Strings.RangeCyans;

    public string RangeBlues => Strings.RangeBlues;

    public string RangeMagentas => Strings.RangeMagentas;

    public string CurveShadows => Strings.CurveShadows;

    public string CurveHighlights => Strings.CurveHighlights;

    public string CurvePointFormat => Strings.CurvePointFormat;

    // Blend modes.
    public string BlendModeNormal => Strings.BlendModeNormal;

    public string BlendModeDarken => Strings.BlendModeDarken;

    public string BlendModeMultiply => Strings.BlendModeMultiply;

    public string BlendModeColorBurn => Strings.BlendModeColorBurn;

    public string BlendModeLinearBurn => Strings.BlendModeLinearBurn;

    public string BlendModeLighten => Strings.BlendModeLighten;

    public string BlendModeScreen => Strings.BlendModeScreen;

    public string BlendModeColorDodge => Strings.BlendModeColorDodge;

    public string BlendModeLinearDodge => Strings.BlendModeLinearDodge;

    public string BlendModeOverlay => Strings.BlendModeOverlay;

    public string BlendModeSoftLight => Strings.BlendModeSoftLight;

    public string BlendModeHardLight => Strings.BlendModeHardLight;

    public string BlendModeVividLight => Strings.BlendModeVividLight;

    public string BlendModeLinearLight => Strings.BlendModeLinearLight;

    public string BlendModePinLight => Strings.BlendModePinLight;

    public string BlendModeHardMix => Strings.BlendModeHardMix;

    public string BlendModeDifference => Strings.BlendModeDifference;

    public string BlendModeExclusion => Strings.BlendModeExclusion;

    public string BlendModeSubtract => Strings.BlendModeSubtract;

    public string BlendModeDivide => Strings.BlendModeDivide;

    public string BlendModeHue => Strings.BlendModeHue;

    public string BlendModeSaturation => Strings.BlendModeSaturation;

    public string BlendModeColor => Strings.BlendModeColor;

    public string BlendModeLuminosity => Strings.BlendModeLuminosity;

    // Selection and mask.
    public string ShapeRectangle => Strings.ShapeRectangle;

    public string ShapeEllipse => Strings.ShapeEllipse;

    public string MaskNone => Strings.MaskNone;

    public string MaskApplied => Strings.MaskApplied;

    public string SelectionClear => Strings.SelectionClear;

    public string SelectionNone => Strings.SelectionNone;

    public string SelectionActive => Strings.SelectionActive;

    public string SelectionMaskApplied => Strings.SelectionMaskApplied;

    public string SelectionNoMask => Strings.SelectionNoMask;

    public string SelectionFromMask => Strings.SelectionFromMask;

    public string StatusSelectLayerForMask => Strings.StatusSelectLayerForMask;

    public string StatusSelectionEmpty => Strings.StatusSelectionEmpty;

    public string StatusMaskApplied => Strings.StatusMaskApplied;

    public string StatusSelectAll => Strings.StatusSelectAll;

    public string StatusMaskCleared => Strings.StatusMaskCleared;

    // Mask brush.
    public string MaskBrush => Strings.MaskBrush;

    public string MaskBrushOn => Strings.MaskBrushOn;

    public string MaskPaint => Strings.MaskPaint;

    public string MaskErase => Strings.MaskErase;

    public string MaskSize => Strings.MaskSize;

    public string MaskHardness => Strings.MaskHardness;

    public string MaskOpacity => Strings.MaskOpacity;

    public string MaskFeather => Strings.MaskFeather;

    public string MaskFeatherRadius => Strings.MaskFeatherRadius;

    public string MaskInvert => Strings.MaskInvert;

    public string MaskPainted => Strings.MaskPainted;

    public string MaskFeathered => Strings.MaskFeathered;

    public string MaskInverted => Strings.MaskInverted;

    public string MaskNoMaskToFeather => Strings.MaskNoMaskToFeather;

    public string MaskNoMaskToInvert => Strings.MaskNoMaskToInvert;

    public string MaskSelectPixelLayer => Strings.MaskSelectPixelLayer;

    // Content-aware fill.
    public string FillContentAware => Strings.FillContentAware;

    public string FillRadius => Strings.FillRadius;

    public string FillUseNikolai => Strings.FillUseNikolai;

    public string FillRun => Strings.FillRun;

    public string FillDone => Strings.FillDone;

    public string FillNothingToFill => Strings.FillNothingToFill;

    public string FillNeedsTarget => Strings.FillNeedsTarget;

    public string FillSelectPixelLayer => Strings.FillSelectPixelLayer;

    // History entries.
    public string UndoNewLayer => Strings.UndoNewLayer;

    public string UndoDeleteLayer => Strings.UndoDeleteLayer;

    public string UndoDuplicateLayer => Strings.UndoDuplicateLayer;

    public string UndoImportLayer => Strings.UndoImportLayer;

    public string UndoReorderLayers => Strings.UndoReorderLayers;

    public string UndoRenameLayer => Strings.UndoRenameLayer;

    public string UndoShowLayer => Strings.UndoShowLayer;

    public string UndoHideLayer => Strings.UndoHideLayer;

    public string UndoLayerOpacity => Strings.UndoLayerOpacity;

    public string UndoBlendMode => Strings.UndoBlendMode;

    public string UndoNewAdjustmentLayer => Strings.UndoNewAdjustmentLayer;

    public string UndoResetAdjustment => Strings.UndoResetAdjustment;

    public string UndoApplyMask => Strings.UndoApplyMask;

    public string UndoSelectAll => Strings.UndoSelectAll;

    public string UndoClearMask => Strings.UndoClearMask;

    public string UndoPaintMask => Strings.UndoPaintMask;

    public string UndoFeatherMask => Strings.UndoFeatherMask;

    public string UndoInvertMask => Strings.UndoInvertMask;

    public string UndoContentFill => Strings.UndoContentFill;

    // Picker entries, error dialogs and the drag captions.
    public string FormatPng => Strings.FormatPng;

    public string FormatJpeg => Strings.FormatJpeg;

    public string FormatWebP => Strings.FormatWebP;

    public string FormatBmp => Strings.FormatBmp;

    public string FormatTiff => Strings.FormatTiff;

    public string DialogOk => Strings.DialogOk;

    public string DialogOpenFailed => Strings.DialogOpenFailed;

    public string DialogExportFailed => Strings.DialogExportFailed;

    public string DropOpenImage => Strings.DropOpenImage;

    public string AboutTitle => Strings.AboutTitle;

    public string DialogOpenFailedDetail => Strings.DialogOpenFailedDetail;

    public string DialogExportFailedDetail => Strings.DialogExportFailedDetail;

    public string AboutUpstream => Strings.AboutUpstream;

    public string AboutDesignerLabel => Strings.AboutDesignerLabel;

    public string AboutLicense => Strings.AboutLicense;

    public string CrashMessage => Strings.CrashMessage;

    public string AboutVersion => Strings.AboutVersion;

    public string AboutUpstreamLink => Strings.AboutUpstreamLink;

    public string UndoOpenImage => Strings.UndoOpenImage;

    public string AboutUnofficial => Strings.AboutUnofficial;

    public string StatusLanguageChanged => Strings.StatusLanguageChanged;

    public string LanguageLabel => Strings.LanguageLabel;

    public string LanguageChinese => Strings.LanguageChinese;

    public string LanguageEnglish => Strings.LanguageEnglish;

    /// <summary>Tells every binding it has a new value. Called by the constructor on a culture change.</summary>
    public void Refresh()
    {
        foreach (string name in Names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
