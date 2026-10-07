namespace NaraPainter.App;

/// <summary>
/// Typed access to every UI string. Keys are grouped by the surface they appear on, which is also how
/// the .resx files are ordered, so a missing translation is easy to spot on both sides.
/// </summary>
/// <remarks>
/// These stay as expression-bodied properties rather than fields: a field would capture the culture at
/// type-initialisation time and keep showing the old language after a switch. The property name is the
/// resource key without its underscores (Toolbar_Open -> ToolbarOpen), which LocalizationTests asserts;
/// XAML binds the same names through <see cref="LocalizedStrings"/>. See docs/modules/LOCALIZATION.md
/// before adding a key.
/// </remarks>
public static class Strings
{
    // Window, status bar and file names.
    public static string AppTitle => Localization.Get("App_Title");

    public static string AppUntitled => Localization.Get("App_Untitled");

    public static string AppWindowTitle => Localization.Get("App_WindowTitle");

    public static string AppModifiedMark => Localization.Get("App_ModifiedMark");

    public static string CommonOn => Localization.Get("Common_On");

    public static string CommonOff => Localization.Get("Common_Off");

    public static string StatusReady => Localization.Get("Status_Ready");

    public static string StatusOpened => Localization.Get("Status_Opened");

    public static string StatusExported => Localization.Get("Status_Exported");

    public static string StatusImported => Localization.Get("Status_Imported");

    public static string StatusLayerAdded => Localization.Get("Status_LayerAdded");

    public static string StatusAdjustmentLayerAdded => Localization.Get("Status_AdjustmentLayerAdded");

    public static string StatusLayerDuplicated => Localization.Get("Status_LayerDuplicated");

    public static string StatusLayerDeleted => Localization.Get("Status_LayerDeleted");

    public static string StatusLayersReordered => Localization.Get("Status_LayersReordered");

    public static string StatusOneLayer => Localization.Get("Status_OneLayer");

    public static string StatusLayerCount => Localization.Get("Status_LayerCount");

    // Toolbar. The hint keys repeat the keyboard shortcut the accelerator already carries.
    public static string ToolbarOpen => Localization.Get("Toolbar_Open");

    public static string ToolbarOpenHint => Localization.Get("Toolbar_OpenHint");

    public static string ToolbarExport => Localization.Get("Toolbar_Export");

    public static string ToolbarExportHint => Localization.Get("Toolbar_ExportHint");

    public static string ToolbarUndo => Localization.Get("Toolbar_Undo");

    public static string ToolbarUndoHint => Localization.Get("Toolbar_UndoHint");

    public static string ToolbarRedo => Localization.Get("Toolbar_Redo");

    public static string ToolbarRedoHint => Localization.Get("Toolbar_RedoHint");

    public static string ToolbarNewLayer => Localization.Get("Toolbar_NewLayer");

    public static string ToolbarNewLayerHint => Localization.Get("Toolbar_NewLayerHint");

    public static string ToolbarDuplicate => Localization.Get("Toolbar_Duplicate");

    public static string ToolbarDuplicateHint => Localization.Get("Toolbar_DuplicateHint");

    public static string ToolbarDeleteLayer => Localization.Get("Toolbar_DeleteLayer");

    public static string ToolbarDeleteHint => Localization.Get("Toolbar_DeleteHint");

    public static string ToolbarAdjustmentLayer => Localization.Get("Toolbar_AdjustmentLayer");

    public static string ToolbarAdjustmentLayerHint => Localization.Get("Toolbar_AdjustmentLayerHint");

    public static string ToolbarZoomOut => Localization.Get("Toolbar_ZoomOut");

    public static string ToolbarZoomOutHint => Localization.Get("Toolbar_ZoomOutHint");

    public static string ToolbarZoomIn => Localization.Get("Toolbar_ZoomIn");

    public static string ToolbarZoomInHint => Localization.Get("Toolbar_ZoomInHint");

    public static string ToolbarFit => Localization.Get("Toolbar_Fit");

    public static string ToolbarFitHint => Localization.Get("Toolbar_FitHint");

    public static string ToolbarActualSize => Localization.Get("Toolbar_ActualSize");

    public static string ToolbarActualSizeHint => Localization.Get("Toolbar_ActualSizeHint");

    public static string ToolbarMaskBrush => Localization.Get("Toolbar_MaskBrush");

    public static string ToolbarMaskBrushHint => Localization.Get("Toolbar_MaskBrushHint");

    public static string ToolbarContentAwareFill => Localization.Get("Toolbar_ContentAwareFill");

    public static string ToolbarContentAwareFillHint => Localization.Get("Toolbar_ContentAwareFillHint");

    public static string ToolbarFillSelection => Localization.Get("Toolbar_FillSelection");

    public static string ToolbarFeatherMask => Localization.Get("Toolbar_FeatherMask");

    public static string ToolbarInvertMask => Localization.Get("Toolbar_InvertMask");

    public static string ToolbarClearMask => Localization.Get("Toolbar_ClearMask");

    // Layers panel.
    public static string LayersTitle => Localization.Get("Layers_Title");

    public static string LayersVisibilityHint => Localization.Get("Layers_VisibilityHint");

    public static string LayersAdjustmentBadge => Localization.Get("Layers_AdjustmentBadge");

    public static string LayersOpacity => Localization.Get("Layers_Opacity");

    public static string LayersAddHint => Localization.Get("Layers_AddHint");

    public static string LayersDuplicateHint => Localization.Get("Layers_DuplicateHint");

    public static string LayersDeleteHint => Localization.Get("Layers_DeleteHint");

    public static string LayersMoveUpHint => Localization.Get("Layers_MoveUpHint");

    public static string LayersMoveDownHint => Localization.Get("Layers_MoveDownHint");

    public static string LayersEmpty => Localization.Get("Layers_Empty");

    public static string LayersDefaultName => Localization.Get("Layers_DefaultName");

    public static string LayersNameFormat => Localization.Get("Layers_NameFormat");

    public static string LayersCopyNameFormat => Localization.Get("Layers_CopyNameFormat");

    public static string LayersImportHint => Localization.Get("Layers_ImportHint");

    public static string LayersImportFailed => Localization.Get("Layers_ImportFailed");

    // Properties panel.
    public static string PropertiesTitle => Localization.Get("Properties_Title");

    public static string PropertiesLayer => Localization.Get("Properties_Layer");

    public static string PropertiesName => Localization.Get("Properties_Name");

    public static string PropertiesBlendMode => Localization.Get("Properties_BlendMode");

    public static string PropertiesOpacity => Localization.Get("Properties_Opacity");

    public static string PropertiesVisible => Localization.Get("Properties_Visible");

    public static string PropertiesAdjustments => Localization.Get("Properties_Adjustments");

    public static string PropertiesSelectionAndMask => Localization.Get("Properties_SelectionAndMask");

    public static string PropertiesNoLayer => Localization.Get("Properties_NoLayer");

    public static string PropertiesShape => Localization.Get("Properties_Shape");

    public static string PropertiesX => Localization.Get("Properties_X");

    public static string PropertiesY => Localization.Get("Properties_Y");

    public static string PropertiesWidth => Localization.Get("Properties_Width");

    public static string PropertiesHeight => Localization.Get("Properties_Height");

    public static string PropertiesFeather => Localization.Get("Properties_Feather");

    public static string PropertiesApplyAsMask => Localization.Get("Properties_ApplyAsMask");

    public static string PropertiesSelectAll => Localization.Get("Properties_SelectAll");

    public static string PropertiesClearMask => Localization.Get("Properties_ClearMask");

    public static string PropertiesReset => Localization.Get("Properties_Reset");

    public static string PropertiesAddPoint => Localization.Get("Properties_AddPoint");

    public static string PropertiesRemovePoint => Localization.Get("Properties_RemovePoint");

    public static string PropertiesChannel => Localization.Get("Properties_Channel");

    public static string PropertiesRange => Localization.Get("Properties_Range");

    public static string PropertiesBrightness => Localization.Get("Properties_Brightness");

    public static string PropertiesContrast => Localization.Get("Properties_Contrast");

    public static string PropertiesHue => Localization.Get("Properties_Hue");

    public static string PropertiesSaturation => Localization.Get("Properties_Saturation");

    public static string PropertiesLightness => Localization.Get("Properties_Lightness");

    public static string PropertiesColorize => Localization.Get("Properties_Colorize");

    public static string PropertiesBlackPoint => Localization.Get("Properties_BlackPoint");

    public static string PropertiesWhitePoint => Localization.Get("Properties_WhitePoint");

    public static string PropertiesGamma => Localization.Get("Properties_Gamma");

    public static string PropertiesOutputBlack => Localization.Get("Properties_OutputBlack");

    public static string PropertiesOutputWhite => Localization.Get("Properties_OutputWhite");

    // Adjustment names, shared by the toolbar menu, the panel headers, layer names and history.
    public static string AdjustBrightnessContrast => Localization.Get("Adjust_BrightnessContrast");

    public static string AdjustHueSaturation => Localization.Get("Adjust_HueSaturation");

    public static string AdjustLevels => Localization.Get("Adjust_Levels");

    public static string AdjustCurves => Localization.Get("Adjust_Curves");

    public static string ChannelRgb => Localization.Get("Channel_Rgb");

    public static string ChannelRed => Localization.Get("Channel_Red");

    public static string ChannelGreen => Localization.Get("Channel_Green");

    public static string ChannelBlue => Localization.Get("Channel_Blue");

    public static string RangeMaster => Localization.Get("Range_Master");

    public static string RangeReds => Localization.Get("Range_Reds");

    public static string RangeYellows => Localization.Get("Range_Yellows");

    public static string RangeGreens => Localization.Get("Range_Greens");

    public static string RangeCyans => Localization.Get("Range_Cyans");

    public static string RangeBlues => Localization.Get("Range_Blues");

    public static string RangeMagentas => Localization.Get("Range_Magentas");

    public static string CurveShadows => Localization.Get("Curve_Shadows");

    public static string CurveHighlights => Localization.Get("Curve_Highlights");

    public static string CurvePointFormat => Localization.Get("Curve_PointFormat");

    // The 24 blend modes, in enum order because the picker binds SelectedIndex to the mode.
    public static string BlendModeNormal => Localization.Get("BlendMode_Normal");

    public static string BlendModeDarken => Localization.Get("BlendMode_Darken");

    public static string BlendModeMultiply => Localization.Get("BlendMode_Multiply");

    public static string BlendModeColorBurn => Localization.Get("BlendMode_ColorBurn");

    public static string BlendModeLinearBurn => Localization.Get("BlendMode_LinearBurn");

    public static string BlendModeLighten => Localization.Get("BlendMode_Lighten");

    public static string BlendModeScreen => Localization.Get("BlendMode_Screen");

    public static string BlendModeColorDodge => Localization.Get("BlendMode_ColorDodge");

    public static string BlendModeLinearDodge => Localization.Get("BlendMode_LinearDodge");

    public static string BlendModeOverlay => Localization.Get("BlendMode_Overlay");

    public static string BlendModeSoftLight => Localization.Get("BlendMode_SoftLight");

    public static string BlendModeHardLight => Localization.Get("BlendMode_HardLight");

    public static string BlendModeVividLight => Localization.Get("BlendMode_VividLight");

    public static string BlendModeLinearLight => Localization.Get("BlendMode_LinearLight");

    public static string BlendModePinLight => Localization.Get("BlendMode_PinLight");

    public static string BlendModeHardMix => Localization.Get("BlendMode_HardMix");

    public static string BlendModeDifference => Localization.Get("BlendMode_Difference");

    public static string BlendModeExclusion => Localization.Get("BlendMode_Exclusion");

    public static string BlendModeSubtract => Localization.Get("BlendMode_Subtract");

    public static string BlendModeDivide => Localization.Get("BlendMode_Divide");

    public static string BlendModeHue => Localization.Get("BlendMode_Hue");

    public static string BlendModeSaturation => Localization.Get("BlendMode_Saturation");

    public static string BlendModeColor => Localization.Get("BlendMode_Color");

    public static string BlendModeLuminosity => Localization.Get("BlendMode_Luminosity");

    // Selection and mask.
    public static string ShapeRectangle => Localization.Get("Shape_Rectangle");

    public static string ShapeEllipse => Localization.Get("Shape_Ellipse");

    public static string MaskNone => Localization.Get("Mask_None");

    public static string MaskApplied => Localization.Get("Mask_Applied");

    public static string SelectionClear => Localization.Get("Selection_Clear");

    public static string SelectionNone => Localization.Get("Selection_None");

    public static string SelectionActive => Localization.Get("Selection_Active");

    public static string SelectionMaskApplied => Localization.Get("Selection_MaskApplied");

    public static string SelectionNoMask => Localization.Get("Selection_NoMask");

    public static string SelectionFromMask => Localization.Get("Selection_FromMask");

    public static string StatusSelectLayerForMask => Localization.Get("Status_SelectLayerForMask");

    public static string StatusSelectionEmpty => Localization.Get("Status_SelectionEmpty");

    public static string StatusMaskApplied => Localization.Get("Status_MaskApplied");

    public static string StatusSelectAll => Localization.Get("Status_SelectAll");

    public static string StatusMaskCleared => Localization.Get("Status_MaskCleared");

    // Mask brush.
    public static string MaskBrush => Localization.Get("Mask_Brush");

    public static string MaskBrushOn => Localization.Get("Mask_BrushOn");

    public static string MaskPaint => Localization.Get("Mask_Paint");

    public static string MaskErase => Localization.Get("Mask_Erase");

    public static string MaskSize => Localization.Get("Mask_Size");

    public static string MaskHardness => Localization.Get("Mask_Hardness");

    public static string MaskOpacity => Localization.Get("Mask_Opacity");

    public static string MaskFeather => Localization.Get("Mask_Feather");

    public static string MaskFeatherRadius => Localization.Get("Mask_FeatherRadius");

    public static string MaskInvert => Localization.Get("Mask_Invert");

    public static string MaskPainted => Localization.Get("Mask_Painted");

    public static string MaskFeathered => Localization.Get("Mask_Feathered");

    public static string MaskInverted => Localization.Get("Mask_Inverted");

    public static string MaskNoMaskToFeather => Localization.Get("Mask_NoMaskToFeather");

    public static string MaskNoMaskToInvert => Localization.Get("Mask_NoMaskToInvert");

    public static string MaskSelectPixelLayer => Localization.Get("Mask_SelectPixelLayer");

    // Content-aware fill.
    public static string FillContentAware => Localization.Get("Fill_ContentAware");

    public static string FillRadius => Localization.Get("Fill_Radius");

    public static string FillUseNikolai => Localization.Get("Fill_UseNikolai");

    public static string FillRun => Localization.Get("Fill_Run");

    public static string FillDone => Localization.Get("Fill_Done");

    public static string FillNothingToFill => Localization.Get("Fill_NothingToFill");

    public static string FillNeedsTarget => Localization.Get("Fill_NeedsTarget");

    public static string FillSelectPixelLayer => Localization.Get("Fill_SelectPixelLayer");

    // History entries. Not shown today; named in the language of the session that recorded them.
    public static string DialogExportFailedDetail => Localization.Get("Dialog_ExportFailedDetail");

    public static string DialogOpenFailedDetail => Localization.Get("Dialog_OpenFailedDetail");

    public static string CrashMessage => Localization.Get("Crash_Message");

    public static string AboutTitle => Localization.Get("About_Title");

    public static string AboutVersion => Localization.Get("About_Version");

    public static string AboutLicense => Localization.Get("About_License");

    public static string AboutUpstream => Localization.Get("About_Upstream");

    public static string AboutUpstreamLink => Localization.Get("About_UpstreamLink");

    public static string AboutUnofficial => Localization.Get("About_Unofficial");

    public static string AboutDesignerLabel => Localization.Get("About_DesignerLabel");

    public static string UndoCropLayer => Localization.Get("Undo_CropLayer");

    public static string UndoRotateRight => Localization.Get("Undo_RotateRight");

    public static string UndoRotateLeft => Localization.Get("Undo_RotateLeft");

    public static string UndoFlipHorizontal => Localization.Get("Undo_FlipHorizontal");

    public static string UndoFlipVertical => Localization.Get("Undo_FlipVertical");

    public static string UndoResizeCanvas => Localization.Get("Undo_ResizeCanvas");

    public static string StatusTransformed => Localization.Get("Status_Transformed");

    public static string StatusResized => Localization.Get("Status_Resized");

    public static string UndoGaussianBlur => Localization.Get("Undo_GaussianBlur");

    public static string UndoSharpen => Localization.Get("Undo_Sharpen");

    public static string StatusPickedColor => Localization.Get("Status_PickedColor");

    public static string ToolbarFilters => Localization.Get("Toolbar_Filters");

    public static string FilterGaussianBlur => Localization.Get("Filter_GaussianBlur");

    public static string FilterSharpen => Localization.Get("Filter_Sharpen");

    public static string FilterRadius => Localization.Get("Filter_Radius");

    public static string FilterAmount => Localization.Get("Filter_Amount");

    public static string ToolbarTransform => Localization.Get("Toolbar_Transform");

    public static string TransformRotateRight => Localization.Get("Transform_RotateRight");

    public static string TransformRotateLeft => Localization.Get("Transform_RotateLeft");

    public static string TransformFlipHorizontal => Localization.Get("Transform_FlipHorizontal");

    public static string TransformFlipVertical => Localization.Get("Transform_FlipVertical");

    public static string TransformCropToSelection => Localization.Get("Transform_CropToSelection");

    public static string TransformResize => Localization.Get("Transform_Resize");

    public static string ResizeTitle => Localization.Get("Resize_Title");

    public static string ResizeWidth => Localization.Get("Resize_Width");

    public static string ResizeHeight => Localization.Get("Resize_Height");

    public static string ResizeKeepRatio => Localization.Get("Resize_KeepRatio");

    public static string ResizeApply => Localization.Get("Resize_Apply");

    public static string ResizeCancel => Localization.Get("Resize_Cancel");

    public static string ToolbarColorPicker => Localization.Get("Toolbar_ColorPicker");

    public static string ColorPickerCopy => Localization.Get("ColorPicker_Copy");

    public static string ColorPickerCopied => Localization.Get("ColorPicker_Copied");

    public static string TransformCropNeedsSelection => Localization.Get("Transform_CropNeedsSelection");

    public static string ExportTitle => Localization.Get("Export_Title");

    public static string ExportSize => Localization.Get("Export_Size");

    public static string ExportOriginalSize => Localization.Get("Export_OriginalSize");

    public static string ExportQuality => Localization.Get("Export_Quality");

    public static string ExportScale => Localization.Get("Export_Scale");

    public static string UndoOpenImage => Localization.Get("Undo_OpenImage");

    public static string UndoNewLayer => Localization.Get("Undo_NewLayer");

    public static string UndoDeleteLayer => Localization.Get("Undo_DeleteLayer");

    public static string UndoDuplicateLayer => Localization.Get("Undo_DuplicateLayer");

    public static string UndoImportLayer => Localization.Get("Undo_ImportLayer");

    public static string UndoReorderLayers => Localization.Get("Undo_ReorderLayers");

    public static string UndoRenameLayer => Localization.Get("Undo_RenameLayer");

    public static string UndoShowLayer => Localization.Get("Undo_ShowLayer");

    public static string UndoHideLayer => Localization.Get("Undo_HideLayer");

    public static string UndoLayerOpacity => Localization.Get("Undo_LayerOpacity");

    public static string UndoBlendMode => Localization.Get("Undo_BlendMode");

    public static string UndoNewAdjustmentLayer => Localization.Get("Undo_NewAdjustmentLayer");

    public static string UndoResetAdjustment => Localization.Get("Undo_ResetAdjustment");

    public static string UndoApplyMask => Localization.Get("Undo_ApplyMask");

    public static string UndoSelectAll => Localization.Get("Undo_SelectAll");

    public static string UndoClearMask => Localization.Get("Undo_ClearMask");

    public static string UndoPaintMask => Localization.Get("Undo_PaintMask");

    public static string UndoFeatherMask => Localization.Get("Undo_FeatherMask");

    public static string UndoInvertMask => Localization.Get("Undo_InvertMask");

    public static string UndoContentFill => Localization.Get("Undo_ContentFill");

    // Picker entries, error dialogs and the drag captions.
    public static string FormatPng => Localization.Get("Format_Png");

    public static string FormatJpeg => Localization.Get("Format_Jpeg");

    public static string FormatWebP => Localization.Get("Format_WebP");

    public static string FormatBmp => Localization.Get("Format_Bmp");

    public static string FormatTiff => Localization.Get("Format_Tiff");

    public static string DialogOk => Localization.Get("Dialog_Ok");

    public static string DialogOpenFailed => Localization.Get("Dialog_OpenFailed");

    public static string DialogExportFailed => Localization.Get("Dialog_ExportFailed");

    public static string DropOpenImage => Localization.Get("Drop_OpenImage");

    /// <summary>
    /// Name lists for the enum-backed pickers. Rebuilt on each read rather than cached so a language
    /// change is picked up; the order matches LevelsChannel, ColorRange and SelectionShape.
    /// </summary>
    public static IReadOnlyList<string> ChannelNames => [ChannelRgb, ChannelRed, ChannelGreen, ChannelBlue];

    public static IReadOnlyList<string> RangeNames =>
        [RangeMaster, RangeReds, RangeYellows, RangeGreens, RangeCyans, RangeBlues, RangeMagentas];

    // Language picker. The two language names are not translated on purpose: a picker is only usable
    // if each entry reads in its own language, whichever one is currently active.
    public static string StatusLanguageChanged => Localization.Get("Status_LanguageChanged");

    public static string LanguageLabel => Localization.Get("Language_Label");

    public static string LanguageChinese => Localization.Get("Language_Chinese");

    public static string LanguageEnglish => Localization.Get("Language_English");
    public static IReadOnlyList<string> ShapeNames => [ShapeRectangle, ShapeEllipse];

    public static IReadOnlyList<string> BlendModeNames =>
    [
        BlendModeNormal, BlendModeDarken, BlendModeMultiply, BlendModeColorBurn, BlendModeLinearBurn,
        BlendModeLighten, BlendModeScreen, BlendModeColorDodge, BlendModeLinearDodge, BlendModeOverlay,
        BlendModeSoftLight, BlendModeHardLight, BlendModeVividLight, BlendModeLinearLight, BlendModePinLight,
        BlendModeHardMix, BlendModeDifference, BlendModeExclusion, BlendModeSubtract, BlendModeDivide,
        BlendModeHue, BlendModeSaturation, BlendModeColor, BlendModeLuminosity
    ];
}
