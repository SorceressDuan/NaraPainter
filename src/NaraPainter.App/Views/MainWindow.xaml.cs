using System.ComponentModel;
using System.Globalization;
using NaraPainter.App.Services;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Pixels;
using NaraPainter.Models.Documents;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;
using Windows.System;

namespace NaraPainter.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly FileDialogService _dialogs;
    private readonly EventHandler _cultureChanged;
    private bool _fitted;

    public MainWindow()
    {
        InitializeComponent();

        Title = Document.WindowTitle;
        _dialogs = new FileDialogService(this, Document.Codec);

        Canvas.Document = Document.Document;
        Canvas.AdjustmentFilter = Document.Filter;
        Canvas.ViewChanged += OnViewChanged;
        Canvas.Loaded += (_, _) => FitCanvas();

        // The canvas control marks its own pointer events handled while panning, so the brush has to
        // subscribe at the CanvasView level and ask for handled events too.
        Canvas.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnCanvasPointerPressed), true);
        Canvas.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnCanvasPointerMoved), true);
        Canvas.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnCanvasPointerReleased), true);
        Canvas.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnCanvasPointerCaptureLost), true);

        Layers.Attach(Document);
        Properties.Attach(Document);

        LabelFlyouts();
        _cultureChanged = (_, _) => OnCultureChanged();
        Localization.CultureChanged += _cultureChanged;

        AddAccelerators();

        // Built after the handler is wired, so filling the list does not read as a user selection.
        FillLanguagePicker();
        LanguagePicker.SelectedIndex = LanguageCatalog.IndexOf(Localization.Culture);

        Document.Changed += (_, _) => Canvas.Refresh();
        Document.DocumentReplaced += OnDocumentReplaced;
        // The swatch is the one thing a colour reading needs beyond text, and the picked value only
        // arrives through the picker's own notification.
        Document.ColorPicker.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(ColorPickerViewModel.Sample) or nameof(ColorPickerViewModel.HasSample))
            {
                UpdateSampleSwatch();
            }
        };
        Document.PropertyChanged += OnDocumentPropertyChanged;
        Closed += OnClosed;
    }

    /// <summary>
    /// Everything a language change has to touch beyond the declarative bindings: the flyouts that
    /// live outside the visual tree, the view models that compose their own labels, and the title.
    /// </summary>
    private void OnCultureChanged()
    {
        LabelFlyouts();
        Document.RefreshLocalization();
        Title = Document.WindowTitle;

        int index = LanguageCatalog.IndexOf(Localization.Culture);
        if (LanguagePicker.SelectedIndex != index) LanguagePicker.SelectedIndex = index;

        RepaintSelections(Content as DependencyObject);
    }

    /// <summary>
    /// Nudges every open ComboBox into redrawing the text of its selected item.
    /// </summary>
    /// <remarks>
    /// A closed ComboBox renders its selection through a content presenter that only refreshes when
    /// the selection changes, so a switch that leaves the index where it was - the usual case, since
    /// the lists are in the same order in every language - leaves the old text on screen. Clearing the
    /// index and putting it back is what forces the redraw; the two-way binding hands the real value
    /// back on the next line.
    /// </remarks>
    private static void RepaintSelections(DependencyObject? node)
    {
        if (node is null) return;

        if (node is ComboBox combo && combo.SelectedIndex >= 0)
        {
            int selected = combo.SelectedIndex;
            combo.SelectedIndex = -1;
            combo.SelectedIndex = selected;
        }

        int count = VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < count; i++) RepaintSelections(VisualTreeHelper.GetChild(node, i));
    }

    private void FillLanguagePicker()
    {
        LanguagePicker.Items.Clear();
        foreach (LanguageOption option in LanguageCatalog.Options)
        {
            LanguagePicker.Items.Add(new ComboBoxItem { Content = option.DisplayName });
        }
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        int index = LanguagePicker.SelectedIndex;
        if (index < 0 || index >= LanguageCatalog.Options.Count) return;

        Localization.Culture = new CultureInfo(LanguageCatalog.Options[index].Name);
        Document.Status = Strings.StatusLanguageChanged;
    }

    /// <summary>
    /// Flyout content is not part of the window's visual tree, so the compiled bindings used elsewhere
    /// in the XAML do not reach it. These few items are labelled here instead; the constructor keeps
    /// one CultureChanged subscription alive so a language change relabels them.
    /// </summary>
    private void LabelFlyouts()
    {
        LocalizedStrings text = Text;

        string[] adjustments =
        [
            text.AdjustBrightnessContrast,
            text.AdjustHueSaturation,
            text.AdjustLevels,
            text.AdjustCurves
        ];

        for (int i = 0; i < AdjustmentFlyout.Items.Count && i < adjustments.Length; i++)
        {
            if (AdjustmentFlyout.Items[i] is MenuFlyoutItem item) item.Text = adjustments[i];
        }

        string[] fill =
        [
            text.ToolbarFillSelection,
            text.ToolbarFeatherMask,
            text.ToolbarInvertMask,
            text.ToolbarClearMask
        ];

        int index = 0;
        foreach (object entry in FillFlyout.Items)
        {
            if (entry is MenuFlyoutItem item && index < fill.Length) item.Text = fill[index++];
        }

        // Each entry carries its gesture, taken from the accelerator table rather than typed again, so
        // a menu cannot advertise a key that no longer does anything.
        string[] transform =
        [
            text.TransformRotateRight,
            text.TransformRotateLeft,
            text.TransformFlipHorizontal,
            text.TransformFlipVertical,
            text.TransformCropToSelection,
            text.TransformResize
        ];

        string[] transformActions =
        [
            nameof(OnAcceleratorRotateRight),
            nameof(OnAcceleratorRotateLeft),
            nameof(OnAcceleratorFlipHorizontal),
            nameof(OnAcceleratorFlipVertical),
            nameof(OnAcceleratorCrop),
            nameof(OnAcceleratorResizeCanvas)
        ];

        Label(TransformFlyout, transform, transformActions);

        string[] filters = [text.FilterGaussianBlur, text.FilterSharpen];
        string[] filterActions = [nameof(OnAcceleratorGaussianBlur), nameof(OnAcceleratorSharpen)];
        Label(FilterFlyout, filters, filterActions);
    }

    /// <summary>
    /// Names the menu items of a flyout in order, skipping its separators, and appends the gesture each
    /// one is bound to.
    /// </summary>
    private void Label(MenuFlyout flyout, string[] labels, string[] actions)
    {
        int index = 0;
        foreach (object entry in flyout.Items)
        {
            if (entry is not MenuFlyoutItem item || index >= labels.Length) continue;

            string hint = index < actions.Length ? HintFor(actions[index]) : string.Empty;
            item.Text = hint.Length > 0 ? $"{labels[index]}    {hint}" : labels[index];
            index++;
        }
    }

    /// <summary>Initialized before InitializeComponent so the compiled bindings in the XAML have a document.</summary>
    public DocumentViewModel Document { get; } = CreateDocument();

    /// <summary>The localisation surface the compiled bindings in this window read their text from.</summary>
    public LocalizedStrings Text => LocalizedStrings.Instance;

    private bool _paintingMask;

    private static DocumentViewModel CreateDocument()
    {
        var codec = new ImageCodec();
        return new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder());
    }

    private async void OnOpenClick(object sender, RoutedEventArgs e) => await OpenAsync();

    private async void OnExportClick(object sender, RoutedEventArgs e)
    {
        string? path = await _dialogs.PickExportPathAsync(Document.SuggestedExportName);
        if (path is null) return;

        (int width, int height, int quality)? options = await AskExportOptionsAsync(path);
        if (options is null) return;

        try
        {
            Document.Export(path, options.Value.width, options.Value.height, options.Value.quality);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            CrashReport.Write("export", error);
            await ShowMessageAsync(Strings.DialogExportFailed, Strings.DialogExportFailedDetail);
        }
    }

    /// <summary>
    /// Asks how big and how compressed. The size starts at the canvas, and a scale field keeps the
    /// two numbers in step so an export cannot end up stretched by accident.
    /// </summary>
    private async Task<(int Width, int Height, int Quality)?> AskExportOptionsAsync(string path)
    {
        int originalWidth = Document.Document.Width;
        int originalHeight = Document.Document.Height;

        var scaleBox = new NumberBox
        {
            Header = Strings.ExportScale,
            Value = 100,
            Minimum = 1,
            Maximum = 1600,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var widthBox = new NumberBox
        {
            Header = Strings.ResizeWidth,
            Value = originalWidth,
            Minimum = 1,
            Maximum = 40000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var heightBox = new NumberBox
        {
            Header = Strings.ResizeHeight,
            Value = originalHeight,
            Minimum = 1,
            Maximum = 40000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var qualityBox = new NumberBox
        {
            Header = Strings.ExportQuality,
            Value = 90,
            Minimum = 1,
            Maximum = 100,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        bool adjusting = false;
        scaleBox.ValueChanged += (_, args) =>
        {
            if (adjusting || double.IsNaN(args.NewValue)) return;
            adjusting = true;
            widthBox.Value = Math.Max(1, Math.Round(originalWidth * args.NewValue / 100));
            heightBox.Value = Math.Max(1, Math.Round(originalHeight * args.NewValue / 100));
            adjusting = false;
        };

        widthBox.ValueChanged += (_, args) =>
        {
            if (adjusting || double.IsNaN(args.NewValue)) return;
            adjusting = true;
            scaleBox.Value = Math.Round(args.NewValue / originalWidth * 100, 1);
            heightBox.Value = Math.Max(1, Math.Round(args.NewValue * originalHeight / originalWidth));
            adjusting = false;
        };

        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(new TextBlock
        {
            Text = Localization.Interpolate(Strings.ExportOriginalSize, $"{originalWidth} × {originalHeight}"),
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
        });
        fields.Children.Add(scaleBox);
        fields.Children.Add(widthBox);
        fields.Children.Add(heightBox);

        // Quality only means something for a lossy format, and the codec is the one that knows which.
        if (Document.Codec.Capabilities(Document.Codec.FormatFromPath(path)).DefaultQuality > 0) fields.Children.Add(qualityBox);

        var dialog = new ContentDialog
        {
            Title = Strings.ExportTitle,
            Content = fields,
            PrimaryButtonText = Strings.ResizeApply,
            CloseButtonText = Strings.ResizeCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily font)
        {
            dialog.FontFamily = font;
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return ((int)widthBox.Value, (int)heightBox.Value, (int)qualityBox.Value);
    }

    /// <summary>
    /// Declares the keyboard shortcuts once, on the window's content.
    /// </summary>
    /// <remarks>
    /// One table rather than scattered per-button lists: it keeps the gestures in a single place to
    /// read and to check for duplicates, and it lets the menu items show the same text the accelerator
    /// uses, so a hint cannot drift away from the key it describes. A control with focus that handles
    /// the key itself still wins, so typing in the layer name box or dragging a slider is unaffected.
    /// </remarks>
    private void AddAccelerators()
    {
        if (Content is not UIElement root) return;

        foreach (Shortcut shortcut in Shortcuts())
        {
            var accelerator = new KeyboardAccelerator { Key = shortcut.Key, Modifiers = shortcut.Modifiers };
            accelerator.Invoked += shortcut.Invoked;
            root.KeyboardAccelerators.Add(accelerator);
        }
    }

    /// <summary>A gesture, what it does, and the words the menu shows for it.</summary>
    internal sealed record Shortcut(
        VirtualKey Key,
        VirtualKeyModifiers Modifiers,
        string Hint,
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> Invoked)
    {
        /// <summary>What a menu item appends to its own text, such as "Gaussian Blur  Ctrl+Shift+B".</summary>
        public string MenuText(string label) => $"{label}    {Hint}";
    }

    internal Shortcut[] Shortcuts() =>    [
        new(VirtualKey.O, VirtualKeyModifiers.Control, "Ctrl+O", OnAcceleratorOpen),
        new(VirtualKey.E, VirtualKeyModifiers.Control, "Ctrl+E", OnAcceleratorExport),
        new(VirtualKey.Z, VirtualKeyModifiers.Control, "Ctrl+Z", OnAcceleratorUndo),
        new(VirtualKey.Y, VirtualKeyModifiers.Control, "Ctrl+Y", OnAcceleratorRedo),
        new(VirtualKey.N, VirtualKeyModifiers.Control, "Ctrl+N", OnAcceleratorNewLayer),
        new(VirtualKey.J, VirtualKeyModifiers.Control, "Ctrl+J", OnAcceleratorDuplicateLayer),
        new(VirtualKey.A, VirtualKeyModifiers.Control, "Ctrl+A", OnAcceleratorSelectAll),
        new(VirtualKey.D, VirtualKeyModifiers.Control, "Ctrl+D", OnAcceleratorDeselect),
        new(VirtualKey.Add, VirtualKeyModifiers.Control, "Ctrl++", OnAcceleratorZoomIn),
        new(VirtualKey.Subtract, VirtualKeyModifiers.Control, "Ctrl+-", OnAcceleratorZoomOut),
        new(VirtualKey.Number0, VirtualKeyModifiers.Control, "Ctrl+0", OnAcceleratorFit),
        new(VirtualKey.Number1, VirtualKeyModifiers.Control, "Ctrl+1", OnAcceleratorActualSize),
        new(VirtualKey.M, VirtualKeyModifiers.None, "M", OnAcceleratorMaskBrush),
        new(VirtualKey.Escape, VirtualKeyModifiers.None, "Esc", OnAcceleratorCancelStroke),

        // The tools added after the first release. C is deliberately avoided for the crop: this
        // program crops at once rather than entering a crop mode, and a bare letter would be too easy
        // to hit while a picture is open.
        new(VirtualKey.I, VirtualKeyModifiers.None, "I", OnAcceleratorColorPicker),
        new(VirtualKey.X, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+X", OnAcceleratorCrop),
        new(VirtualKey.L, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+L", OnAcceleratorRotateRight),
        new(VirtualKey.R, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+R", OnAcceleratorRotateLeft),
        new(VirtualKey.H, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+H", OnAcceleratorFlipHorizontal),
        new(VirtualKey.V, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+V", OnAcceleratorFlipVertical),
        new(VirtualKey.C, VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu, "Ctrl+Alt+C", OnAcceleratorResizeCanvas),
        new(VirtualKey.B, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+B", OnAcceleratorGaussianBlur),
        new(VirtualKey.U, VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift, "Ctrl+Shift+U", OnAcceleratorSharpen)
    ];

    /// <summary>The hint for one action, or an empty string when it has no shortcut.</summary>
    private string HintFor(string action)
    {
        // Actions are matched by their delegate so the table stays the only list of gestures.
        TypedEventHandler<KeyboardAccelerator, KeyboardAcceleratorInvokedEventArgs> handler = action switch
        {
            nameof(OnAcceleratorColorPicker) => OnAcceleratorColorPicker,
            nameof(OnAcceleratorCrop) => OnAcceleratorCrop,
            nameof(OnAcceleratorRotateRight) => OnAcceleratorRotateRight,
            nameof(OnAcceleratorRotateLeft) => OnAcceleratorRotateLeft,
            nameof(OnAcceleratorFlipHorizontal) => OnAcceleratorFlipHorizontal,
            nameof(OnAcceleratorFlipVertical) => OnAcceleratorFlipVertical,
            nameof(OnAcceleratorResizeCanvas) => OnAcceleratorResizeCanvas,
            nameof(OnAcceleratorGaussianBlur) => OnAcceleratorGaussianBlur,
            nameof(OnAcceleratorSharpen) => OnAcceleratorSharpen,
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "No shortcut is declared for that action.")
        };

        return Shortcuts().First(shortcut => shortcut.Invoked == handler).Hint;
    }

    private async void OnAboutClick(object sender, RoutedEventArgs e) => await AboutDialog.ShowAsync(Content.XamlRoot);

    /// <summary>
    /// Asks for a new canvas size and applies it. The two fields stay in step by default, because
    /// stretching a picture by accident is the more common mistake.
    /// </summary>
    private async Task ResizeCanvasAsync()
    {
        int originalWidth = Document.Document.Width;
        int originalHeight = Document.Document.Height;

        var widthBox = new NumberBox
        {
            Header = Strings.ResizeWidth,
            Value = originalWidth,
            Minimum = 1,
            Maximum = 20000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var heightBox = new NumberBox
        {
            Header = Strings.ResizeHeight,
            Value = originalHeight,
            Minimum = 1,
            Maximum = 20000,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
        };

        var keepRatio = new CheckBox { Content = Strings.ResizeKeepRatio, IsChecked = true };
        bool adjusting = false;

        widthBox.ValueChanged += (_, args) =>
        {
            if (adjusting || keepRatio.IsChecked != true || double.IsNaN(args.NewValue)) return;
            adjusting = true;
            heightBox.Value = Math.Max(1, Math.Round(args.NewValue * originalHeight / originalWidth));
            adjusting = false;
        };

        heightBox.ValueChanged += (_, args) =>
        {
            if (adjusting || keepRatio.IsChecked != true || double.IsNaN(args.NewValue)) return;
            adjusting = true;
            widthBox.Value = Math.Max(1, Math.Round(args.NewValue * originalWidth / originalHeight));
            adjusting = false;
        };

        var fields = new StackPanel { Spacing = 8 };
        fields.Children.Add(widthBox);
        fields.Children.Add(heightBox);
        fields.Children.Add(keepRatio);

        var dialog = new ContentDialog
        {
            Title = Strings.ResizeTitle,
            Content = fields,
            PrimaryButtonText = Strings.ResizeApply,
            CloseButtonText = Strings.ResizeCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = Content.XamlRoot
        };

        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily font)
        {
            dialog.FontFamily = font;
        }

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Document.Transform.ResizeCanvas((int)widthBox.Value, (int)heightBox.Value);
    }

    /// <summary>Puts the sampled colour on the clipboard as hex, which is how it gets used elsewhere.</summary>
    private void OnCopyColorClick(object sender, RoutedEventArgs e)
    {
        if (!Document.ColorPicker.HasSample) return;

        var package = new DataPackage();
        package.SetText(Document.ColorPicker.Hex);
        Clipboard.SetContent(package);
        Document.Status = Strings.ColorPickerCopied;
    }

    private void OnAcceleratorColorPicker(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.ColorPicker.IsActive = !Document.ColorPicker.IsActive;
        ColorPickerButton.IsChecked = Document.ColorPicker.IsActive;
    }

    private void OnAcceleratorCrop(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CropToSelection();
    }

    private void OnAcceleratorRotateRight(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.Rotate(QuarterTurn.Clockwise);
    }

    private void OnAcceleratorRotateLeft(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.Rotate(QuarterTurn.CounterClockwise);
    }

    private void OnAcceleratorFlipHorizontal(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.Flip(FlipAxis.Horizontal);
    }

    private void OnAcceleratorFlipVertical(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.Flip(FlipAxis.Vertical);
    }

    private void OnAcceleratorResizeCanvas(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ResizeCanvasAsync();
    }

    private void OnAcceleratorGaussianBlur(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.GaussianBlur(FilterRadius);
    }

    private void OnAcceleratorSharpen(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Transform.Sharpen(FilterRadius, FilterAmount);
    }

    private void OnRotateRightClick(object sender, RoutedEventArgs e) => Document.Transform.Rotate(QuarterTurn.Clockwise);

    private void OnRotateLeftClick(object sender, RoutedEventArgs e) => Document.Transform.Rotate(QuarterTurn.CounterClockwise);

    private void OnFlipHorizontalClick(object sender, RoutedEventArgs e) => Document.Transform.Flip(FlipAxis.Horizontal);

    private void OnFlipVerticalClick(object sender, RoutedEventArgs e) => Document.Transform.Flip(FlipAxis.Vertical);

    private void OnCropToSelectionClick(object sender, RoutedEventArgs e) => CropToSelection();

    /// <summary>
    /// Crops to the selection, or says why it cannot. Shared by the menu item and the shortcut so the
    /// two cannot drift apart.
    /// </summary>
    private void CropToSelection()
    {
        if (Document.Selection.HasRegion)
        {
            Document.Transform.CropToSelection();
            return;
        }

        Document.Status = Strings.TransformCropNeedsSelection;
    }

    private async void OnResizeCanvasClick(object sender, RoutedEventArgs e) => await ResizeCanvasAsync();

    private void OnColorPickerClick(object sender, RoutedEventArgs e)
    {
        Document.ColorPicker.IsActive = ColorPickerButton.IsChecked == true;
    }

    private void OnGaussianBlurClick(object sender, RoutedEventArgs e) => Document.Transform.GaussianBlur(FilterRadius);

    private void OnSharpenClick(object sender, RoutedEventArgs e) => Document.Transform.Sharpen(FilterRadius, FilterAmount);

    /// <summary>The two filter actions share one radius and one amount, which is what the panel shows.</summary>
    private double FilterRadius => BlurRadiusSlider.Value;

    private double FilterAmount => SharpenAmountSlider.Value;
    private void UpdateSampleSwatch()
    {
        if (!Document.ColorPicker.HasSample)
        {
            SampleSwatch.Background = null;
            return;
        }

        Rgba32 sample = Document.ColorPicker.Sample;
        SampleSwatch.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(sample.A, sample.R, sample.G, sample.B));
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => Document.Undo();

    /// <summary>
    /// The keyboard entry points. Each one marks the event handled so the key does not also reach the
    /// control that has focus, which would otherwise scroll a list or move a slider as well.
    /// </summary>
    private void OnAcceleratorOpen(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnOpenClick(this, new RoutedEventArgs());
    }

    private void OnAcceleratorExport(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnExportClick(this, new RoutedEventArgs());
    }

    private void OnAcceleratorUndo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Undo();
    }

    private void OnAcceleratorRedo(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Redo();
    }

    private void OnAcceleratorNewLayer(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.AddLayer();
    }

    private void OnAcceleratorDuplicateLayer(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.DuplicateLayer(Document.SelectedLayer);
    }

    private void OnAcceleratorSelectAll(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Selection.SelectAll();
    }

    private void OnAcceleratorDeselect(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.Selection.Clear();
    }

    private void OnAcceleratorZoomIn(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Canvas.ZoomTo(Canvas.Zoom * 1.25);
    }

    private void OnAcceleratorZoomOut(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Canvas.ZoomTo(Canvas.Zoom / 1.25);
    }

    private void OnAcceleratorFit(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Canvas.FitToWindow();
    }

    private void OnAcceleratorActualSize(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Canvas.ZoomTo(1);
    }

    private void OnAcceleratorMaskBrush(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Document.MaskBrush.IsActive = !Document.MaskBrush.IsActive;
        MaskBrushButton.IsChecked = Document.MaskBrush.IsActive;
    }

    /// <summary>Escape abandons the stroke in progress rather than committing it to the mask.</summary>
    private void OnAcceleratorCancelStroke(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_paintingMask) return;

        args.Handled = true;
        _paintingMask = false;
        Document.MaskBrush.CancelStroke();
    }

    private void OnRedoClick(object sender, RoutedEventArgs e) => Document.Redo();

    private void OnNewLayerClick(object sender, RoutedEventArgs e) => Document.AddLayer();

    private void OnDuplicateLayerClick(object sender, RoutedEventArgs e) => Document.DuplicateLayer(Document.SelectedLayer);

    private void OnDeleteLayerClick(object sender, RoutedEventArgs e) => Document.DeleteLayer(Document.SelectedLayer);

    private void OnAddAdjustmentLayerClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not string tag) return;
        if (!Enum.TryParse(tag, out AdjustmentKind kind)) return;

        Document.AddAdjustmentLayer(kind);
    }

    private void OnZoomInClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(Canvas.Zoom * 1.25);

    private void OnMaskBrushClick(object sender, RoutedEventArgs e)
    {
        // The toggle owns the state; read it back rather than tracking a second copy here.
        Document.MaskBrush.IsActive = MaskBrushButton.IsChecked == true;
    }

    private void OnContentAwareFillClick(object sender, RoutedEventArgs e) => Document.ContentFill.Fill();

    private void OnFeatherMaskClick(object sender, RoutedEventArgs e) => Document.MaskBrush.Feather(Document.MaskBrush.FeatherRadius);

    private void OnInvertMaskClick(object sender, RoutedEventArgs e) => Document.MaskBrush.Invert();

    private void OnClearMaskClick(object sender, RoutedEventArgs e) => Document.Selection.ClearMask();

    private void OnCanvasPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Canvas);
        if (!point.Properties.IsLeftButtonPressed) return;

        // The eyedropper reads one pixel and does not capture the pointer: there is no stroke to follow.
        if (Document.ColorPicker.IsActive)
        {
            var sampled = ToDocument(point.Position);
            Document.ColorPicker.Pick(sampled.X, sampled.Y);
            return;
        }

        if (!Document.MaskBrush.IsActive) return;

        var document = ToDocument(point.Position);
        Document.MaskBrush.BeginStroke(document.X, document.Y);
        _paintingMask = true;
        Canvas.CapturePointer(e.Pointer);
    }

    private void OnCanvasPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        var document = ToDocument(e.GetCurrentPoint(Canvas).Position);
        Document.MaskBrush.ContinueStroke(document.X, document.Y);
    }

    private void OnCanvasPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        _paintingMask = false;
        Document.MaskBrush.EndStroke();
        Canvas.ReleasePointerCapture(e.Pointer);
    }

    private void OnCanvasPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (!_paintingMask) return;

        _paintingMask = false;
        Document.MaskBrush.EndStroke();
    }

    /// <summary>Control coordinates to canvas pixels, matching how the renderer places the document.</summary>
    private System.Numerics.Vector2 ToDocument(Windows.Foundation.Point position)
    {
        double zoom = Canvas.Zoom > 0 ? Canvas.Zoom : 1;
        return new System.Numerics.Vector2(
            (float)((position.X - Canvas.PanX) / zoom),
            (float)((position.Y - Canvas.PanY) / zoom));
    }

    private void OnZoomOutClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(Canvas.Zoom / 1.25);

    private void OnActualSizeClick(object sender, RoutedEventArgs e) => Canvas.ZoomTo(1);

    private void OnFitClick(object sender, RoutedEventArgs e) => Canvas.FitToWindow();

    private void OnViewChanged(object? sender, EventArgs e) => Document.SetZoom(Canvas.Zoom);

    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_fitted || args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;

        _fitted = true;
        Canvas.FitToWindow();
    }

    private void OnCanvasDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;

        e.AcceptedOperation = DataPackageOperation.Copy;
        if (e.DragUIOverride is not null) e.DragUIOverride.Caption = Strings.DropOpenImage;
    }

    private async void OnCanvasDrop(object sender, DragEventArgs e)
    {
        string? path = await DroppedFile.FirstPathAsync(e.DataView);
        if (path is null) return;

        await OpenAsync(path);
    }

    private void OnDocumentReplaced(object? sender, CanvasDocument document)
    {
        Canvas.Document = document;
        if (Canvas.ActualWidth > 0) Canvas.FitToWindow();
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName == nameof(DocumentViewModel.WindowTitle))
        {
            Title = Document.WindowTitle;
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        Localization.CultureChanged -= _cultureChanged;
        Application.Current.Exit();
    }

    private async Task OpenAsync()
    {
        string? path = await _dialogs.PickImageAsync();
        if (path is not null) await OpenAsync(path);
    }

    private async Task OpenAsync(string path)
    {
        try
        {
            Document.Open(path);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // A damaged file, an unsupported format or a path that went away all end up here. The
            // reader's own message is not something to show a user, so the dialog is fixed text and
            // the detail goes to the crash log instead.
            CrashReport.Write("open", error);
            await ShowMessageAsync(Strings.DialogOpenFailed, Strings.DialogOpenFailedDetail);
        }
    }

    private void FitCanvas()
    {
        if (Canvas.ActualWidth > 0) Canvas.FitToWindow();
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = Strings.DialogOk,
            XamlRoot = Content.XamlRoot
        };

        // A dialog is hosted outside the window's tree, so AppFontFamily does not reach it by
        // inheritance the way it does for the panels.
        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily font)
        {
            dialog.FontFamily = font;
        }

        await dialog.ShowAsync();
    }
}
