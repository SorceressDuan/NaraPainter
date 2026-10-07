using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using NaraPainter.App.ViewModels;
using NaraPainter.Imaging.Services;
using NaraPainter.Models.Adjustments;
using NaraPainter.Models.Layers;
using NaraPainter.Models.Pixels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace NaraPainter.App.Services;

/// <summary>
/// Drives the editing path the sandbox cannot click through: open, add a layer, switch blend modes,
/// run each adjustment, mask the layer, duplicate and reorder, undo and redo, export and read the file
/// back. Writes the log it produces and returns a process exit code, so a headless run can tell
/// success from failure. Enabled with --selftest; nothing else in the app calls it.
/// </summary>
public static class SmokeTest
{
    public static bool IsRequested(string[] commandLine) =>
        commandLine.Any(argument =>
            argument.Equals("--selftest", StringComparison.OrdinalIgnoreCase)
            || argument.StartsWith("--selftest=", StringComparison.OrdinalIgnoreCase));

    public static async Task<int> RunAsync(Views.MainWindow window, string[] commandLine)
    {
        await Task.Yield();

        var log = new List<string>();
        string logPath = Path.Combine(AppContext.BaseDirectory, "selftest.log");
        int exitCode = 1;
        try
        {
            string imagePath = Option(commandLine, "--selftest") ?? Path.Combine("assets", "testimages", "photo.jpg");
            string largePath = Option(commandLine, "--large") ?? Path.Combine("assets", "testimages", "large-4000x3000.png");
            string exportPath = Option(commandLine, "--export") ?? Path.Combine(Path.GetTempPath(), "narapainter-selftest.png");
            logPath = Option(commandLine, "--log") ?? logPath;
            StartupLog.Record("smokeTest", $"image={imagePath}", $"export={exportPath}", $"log={logPath}");
            log.Add($"options image={imagePath} export={exportPath} log={logPath} cwd={Environment.CurrentDirectory}");
            CheckResources(log);
            await CheckChrome(window, log);
            await CheckLanguageSwitch(window, log);
            CheckUnreadableFileIsRefused(log);
            CheckUndoMerge(log);
            CheckEditingTools(log);
            CheckShortcuts(window, log);

            DocumentViewModel document = window.Document;
            log.Add($"window.title={window.Title}");

            var watch = Stopwatch.StartNew();
            document.Open(imagePath);
            log.Add($"open file={Path.GetFileName(imagePath)} size={document.SizeLabel} layers={document.Layers.Count} ms={watch.ElapsedMilliseconds}");
            log.Add($"window.title={window.Title}");

            LayerViewModel layer = document.AddLayer();

            // A colored, semi transparent fill: every adjustment below has something to move, which a
            // neutral gray would not give the saturation slider.
            Fill(layer, 176, 96, 208, 150);
            log.Add($"newLayer name={layer.Name} layers={document.Layers.Count} size={layer.ContentLabel}");

            var samples = new List<string>();
            foreach (BlendMode mode in new[] { BlendMode.Multiply, BlendMode.Screen, BlendMode.Overlay })
            {
                layer.BlendModeIndex = (int)mode;
                samples.Add($"{mode}={Sample(document)}");
            }

            log.Add($"blendModes {string.Join(" ", samples)}");
            if (samples.Distinct().Count() != samples.Count)
            {
                throw new InvalidOperationException("The three blend modes produced the same composite pixel.");
            }

            string before = Sample(document);
            document.Adjustment.BeginEdit();
            document.Adjustment.Brightness = 25;
            document.Adjustment.Contrast = 10;
            string adjusted = Sample(document);
            log.Add($"brightnessContrast brightness=25 contrast=10 before={before} after={adjusted}");
            if (before == adjusted) throw new InvalidOperationException("Brightness/contrast left the composite unchanged.");
            if (!document.CanUndo) throw new InvalidOperationException("The adjustment never reached the undo stack.");

            document.Undo();
            string undone = Sample(document);
            log.Add($"undo sample={undone} canUndo={document.CanUndo} canRedo={document.CanRedo}");
            if (undone != before) throw new InvalidOperationException("Undo did not restore the previous composite.");

            document.Redo();
            string redone = Sample(document);
            log.Add($"redo sample={redone}");
            if (redone != adjusted) throw new InvalidOperationException("Redo did not bring the adjustment back.");

            string levelsBefore = Sample(document);
            document.Adjustment.LevelBlack = 30;
            document.Adjustment.LevelWhite = 230;
            string levelsAfter = Sample(document);
            log.Add($"levels black=30 white=230 before={levelsBefore} after={levelsAfter}");
            if (levelsBefore == levelsAfter) throw new InvalidOperationException("Levels left the composite unchanged.");

            string hueBefore = Sample(document);
            document.Adjustment.Saturation = -60;
            string hueAfter = Sample(document);
            log.Add($"hueSaturation saturation=-60 before={hueBefore} after={hueAfter} layerPixel={Describe(layer.Model.Pixels![0, 0])}");
            if (hueBefore == hueAfter) throw new InvalidOperationException("Hue/saturation left the composite unchanged.");

            document.Adjustment.Hue = 120;
            string shifted = Sample(document);
            log.Add($"hueShift hue=120 before={hueAfter} after={shifted}");
            if (hueAfter == shifted) throw new InvalidOperationException("The hue shift left the composite unchanged.");

            string curvesBefore = Sample(document);
            document.Adjustment.AddCurvePoint();
            document.Adjustment.CurvePoints[1].Y = 180;
            string curvesAfter = Sample(document);
            log.Add($"curves points={document.Adjustment.CurvePoints.Count} point1Y=180 before={curvesBefore} after={curvesAfter}");
            if (curvesBefore == curvesAfter) throw new InvalidOperationException("The curve edit left the composite unchanged.");

            document.Selection.X = 0;
            document.Selection.Y = 0;
            document.Selection.Width = document.Document.Width / 2;
            document.Selection.Height = document.Document.Height / 2;
            document.Selection.ApplyMask();
            PixelBuffer masked = document.Flatten();
            log.Add($"mask masked={layer.IsMasked} inside={Describe(masked[masked.Width / 4, masked.Height / 4])} outside={Describe(masked[(masked.Width * 3) / 4, (masked.Height * 3) / 4])}");
            if (!layer.IsMasked) throw new InvalidOperationException("The selection did not become a layer mask.");

            string exported = Sample(document);
            document.Export(exportPath);
            long written = new FileInfo(exportPath).Length;
            log.Add($"export path={exportPath} bytes={written} sample={exported}");
            if (written == 0) throw new InvalidOperationException("The exported file is empty.");

            PixelBuffer reread = document.Codec.Read(exportPath);
            string rereadCenter = Describe(reread[reread.Width / 2, reread.Height / 2]);
            log.Add($"reopen size={reread.Width} × {reread.Height} center={rereadCenter}");
            if (reread.Width != document.Document.Width || reread.Height != document.Document.Height)
            {
                throw new InvalidOperationException("The exported image came back at a different size.");
            }

            if (rereadCenter != exported)
            {
                throw new InvalidOperationException($"The PNG round trip changed pixels: {exported} became {rereadCenter}.");
            }

            LayerViewModel copy = document.DuplicateLayer(layer)!;
            log.Add($"duplicate name={copy.Name} layers={document.Layers.Count} hasAdjustments={copy.Adjustment(AdjustmentKind.BrightnessContrast) is not null}");

            document.ReorderLayers(document.Layers.Reverse().ToList());
            log.Add($"reorder panelTop={document.Layers[0].Name} documentTop={document.Document.Layers[^1].Name} layers={document.Layers.Count}");
            if (document.Layers[0].Name != document.Document.Layers[^1].Name)
            {
                throw new InvalidOperationException("The panel order and the document stack disagree after a reorder.");
            }

            if (File.Exists(largePath))
            {
                watch.Restart();
                document.Open(largePath);
                log.Add($"openLarge file={Path.GetFileName(largePath)} size={document.SizeLabel} layers={document.Layers.Count} ms={watch.ElapsedMilliseconds}");
            }

            log.Add("RESULT PASS");
            exitCode = 0;
        }
        catch (Exception error)
        {
            log.Add($"RESULT FAIL {error.GetType().Name}: {error.Message}");
        }
        finally
        {
            log.Add($"exit={exitCode}");
            WriteLog(logPath, log);
            StartupLog.Record("smokeTest", $"exit={exitCode}", $"wrote={logPath}", $"lines={log.Count}");
        }

        return exitCode;
    }

    /// <summary>
    /// Reads every key in the active language off the bindable resource object. A key that is missing
    /// from the .resx comes back as "!Key!", which would otherwise only surface as a strange label in
    /// a window this sandbox cannot look at; here it fails the run and lands in the log.
    /// </summary>
    private static void CheckResources(List<string> log)
    {
        var resolved = typeof(LocalizedStrings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => (property.Name, Value: property.GetValue(LocalizedStrings.Instance) as string))
            .ToList();

        string[] broken =
        [
            .. resolved
                .Where(entry => string.IsNullOrEmpty(entry.Value) || (entry.Value.Length > 2 && entry.Value[0] == '!' && entry.Value[^1] == '!'))
                .Select(entry => entry.Name)
        ];

        log.Add($"resources culture={Localization.Culture.Name} keys={resolved.Count} broken={broken.Length}"
            + $" sample={Strings.ToolbarUndo}/{Strings.LayersTitle}/{Strings.StatusReady}");
        if (broken.Length > 0)
        {
            throw new InvalidOperationException($"These resource keys did not resolve: {string.Join(", ", broken)}");
        }
    }

    /// <summary>
    /// Reads the interface text back out of the live window. The compiled bindings resolve at load
    /// time and a path that silently failed would leave an empty label, which is invisible in a headless
    /// run; here the expected strings either appear in the tree or the self test fails.
    /// </summary>
    private static async Task CheckChrome(Views.MainWindow window, List<string> log)
    {
        string[] expected =
        [
            Strings.ToolbarOpen, Strings.ToolbarUndo, Strings.LayersTitle,
            Strings.PropertiesTitle, Strings.PropertiesAdjustments, Strings.MaskBrush
        ];

        // Wait for the window to finish loading before walking it. A freshly unpacked build reads
        // several hundred megabytes of runtime off disk for the first time, and without this the
        // panels are still waiting on their templates when the walk runs - the check then fails on a
        // first run and passes on every later one, which is the worst kind of flaky.
        if (window.Content is FrameworkElement root && !root.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            void OnLoaded(object sender, RoutedEventArgs args) => loaded.TrySetResult();
            root.Loaded += OnLoaded;
            try
            {
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                log.Add("chrome.warning=the window did not report Loaded within 30s");
            }
            finally
            {
                root.Loaded -= OnLoaded;
            }
        }

        CommandBar? bar = FindCommandBar(window.Content as DependencyObject);
        var barLabels = new List<string>();
        if (bar is not null)
        {
            foreach (ICommandBarElement element in bar.PrimaryCommands)
            {
                switch (element)
                {
                    case AppBarButton button: barLabels.Add(button.Label); break;
                    case AppBarToggleButton toggle: barLabels.Add(toggle.Label); break;
                }
            }
        }

        // A UserControl only moves its content into the visual tree once its template has been applied,
        // which the dispatcher does on its own schedule. Yielding here (rather than blocking) is what
        // lets that layout run at all; without it the panels still look empty to the walk below.
        var texts = new List<string>();
        string[] missing = expected;
        for (int attempt = 0; attempt < 80; attempt++)
        {
            await Task.Delay(50);

            (window.Content as UIElement)?.UpdateLayout();
            texts.Clear();
            Collect(window.Content as DependencyObject, texts);
            texts.AddRange(barLabels);

            missing = [.. expected.Where(text => !texts.Contains(text))];
            if (missing.Length == 0) break;
        }

        log.Add($"chrome culture={Localization.Culture.Name} title={window.Title} texts={texts.Count} checked={expected.Length} missing={missing.Length}");
        log.Add($"chrome.texts={string.Join(" | ", texts)}");

        if (window.Title.Contains('!'))
        {
            throw new InvalidOperationException($"The window title did not compose from its resources: {window.Title}");
        }

        if (bar is null)
        {
            throw new InvalidOperationException("The command bar is not in the window tree, so the labels could not be checked.");
        }

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"These labels are not showing in the window: {string.Join(" / ", missing)}");
        }
    }

    /// <summary>
    /// Switches the language the way the picker does and back again, checking the parts that have a
    /// dependable signal: the culture, the window title, the toolbar, the values behind the pickers,
    /// and the flyouts that live outside the visual tree.
    /// </summary>
    /// <remarks>
    /// Deliberately not a whole-tree snapshot comparison. A closed ComboBox does not reliably redraw
    /// the text of its current selection when its items change language, so that comparison reports
    /// blanks that say nothing about whether the switch worked. The values behind those pickers are
    /// checked instead, and the display side is covered by the per-language chrome check above.
    /// </remarks>
    private static async Task CheckLanguageSwitch(Views.MainWindow window, List<string> log)
    {
        CultureInfo original = Localization.Culture;
        CultureInfo other = original.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("en")
            : new CultureInfo("zh-CN");

        string titleBefore = window.Title;
        var toolbarBefore = ToolbarLabels(window);

        Localization.Culture = other;
        await Settle(window);

        var toolbarOther = ToolbarLabels(window);
        string titleOther = window.Title;
        log.Add($"language {original.Name}->{other.Name} title='{titleOther}' toolbar={toolbarOther.Count}");

        if (Localization.Culture.Name != other.Name)
        {
            throw new InvalidOperationException($"Setting the culture to {other.Name} did not take.");
        }

        if (titleOther.Contains('!') || titleOther == titleBefore)
        {
            throw new InvalidOperationException($"The window title did not follow the switch: '{titleBefore}' -> '{titleOther}'.");
        }

        int moved = toolbarOther.Count(label => !toolbarBefore.Contains(label));
        if (moved == 0)
        {
            throw new InvalidOperationException($"None of the {toolbarBefore.Count} toolbar labels changed language.");
        }

        List<string> modes = window.Document.SelectedLayer?.BlendModeNames.ToList() ?? [];
        List<string> shapes = [.. window.Document.Selection.ShapeNames];
        log.Add($"language {other.Name} blendModes={modes.Count} firstMode='{modes.FirstOrDefault()}' firstShape='{shapes.FirstOrDefault()}'");

        if (modes.Count == 0 || shapes.Count == 0)
        {
            throw new InvalidOperationException("A picker lost its values on the switch.");
        }

        if (modes.First().Contains('!') || shapes.First().Contains('!'))
        {
            throw new InvalidOperationException("A picker value came back as a resource placeholder.");
        }

        Localization.Culture = original;
        await Settle(window);

        log.Add($"language {other.Name}->{original.Name} title='{window.Title}' restored={window.Title == titleBefore}");
        if (window.Title != titleBefore)
        {
            throw new InvalidOperationException($"The window title did not go back to '{titleBefore}'.");
        }

        var toolbarBack = ToolbarLabels(window);
        string[] lost = [.. toolbarBefore.Where(label => !toolbarBack.Contains(label))];
        if (lost.Length > 0)
        {
            throw new InvalidOperationException($"The toolbar did not go back to {original.Name}: {string.Join(" / ", lost.Take(4))}");
        }

        // A closed ComboBox redraws the text of its selection only when the selection changes, so a
        // switch can leave a picker blank. Everything else about the switch is asserted above; this is
        // reported rather than thrown, because the value behind the picker is still correct and the
        // blank clears as soon as the user opens it.
        List<string> shown = ComboTexts(window);
        int blanks = shown.Count(text => text.Length == 0);
        log.Add($"language pickers={shown.Count} blank={blanks} values='{string.Join(" / ", shown)}'");
    }

    /// <summary>The text each ComboBox is currently displaying, read out of its template.</summary>
    private static List<string> ComboTexts(Views.MainWindow window)
    {
        var found = new List<string>();
        Walk(window.Content as DependencyObject);
        return found;

        void Walk(DependencyObject? node)
        {
            if (node is null) return;

            if (node is ComboBox combo)
            {
                var inner = new List<string>();
                Collect(combo, inner);
                found.Add(string.Concat(inner));
            }

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++) Walk(VisualTreeHelper.GetChild(node, i));
        }
    }

    private static List<string> ToolbarLabels(Views.MainWindow window)
    {
        var labels = new List<string>();
        CommandBar? bar = FindCommandBar(window.Content as DependencyObject);
        if (bar is null) return labels;

        foreach (ICommandBarElement element in bar.PrimaryCommands)
        {
            switch (element)
            {
                case AppBarButton button: labels.Add(button.Label); break;
                case AppBarToggleButton toggle: labels.Add(toggle.Label); break;
            }
        }

        return labels;
    }

    /// <summary>Lets the dispatcher run the work a language change queued before looking at the result.</summary>
    private static async Task Settle(Views.MainWindow window)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            await Task.Delay(50);
            (window.Content as UIElement)?.UpdateLayout();
        }
    }

    /// <summary>
    /// A damaged or unsupported file has to come back as a refusal, not as a crash, and it must leave
    /// whatever was open alone.
    /// </summary>
    private static void CheckUnreadableFileIsRefused(List<string> log)
    {
        // Placed next to the running executable rather than in the system temp folder, which is not
        // always writable - a portable build can be unzipped into a read-only place.
        string path = Path.Combine(AppContext.BaseDirectory, "narapainter-not-an-image.png");
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x00, 0x01, 0x02, 0x03, 0x04, 0x05]);

        var codec = new ImageCodec();
        bool refused = false;
        try
        {
            new DocumentViewModel(new ImageImporter(codec), codec, new AdjustmentFilter(), new SelectionMaskBuilder()).Open(path);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            refused = true;
            log.Add($"unreadable refused={error.GetType().Name}");
        }
        finally
        {
            File.Delete(path);
        }

        if (!refused) throw new InvalidOperationException("Opening a damaged file did not report a failure.");
    }

    /// <summary>
    /// A drag of a slider is one undo step; a value set on its own is its own. The history merges by
    /// key, so both are visible in one place here.
    /// </summary>
    private static void CheckUndoMerge(List<string> log)
    {
        var codec = new ImageCodec();
        var document = new DocumentViewModel(
            new ImageImporter(codec), codec,
            new AdjustmentFilter(), new SelectionMaskBuilder());
        LayerViewModel layer = document.Layers[0];

        layer.BeginOpacityEdit();
        for (int value = 99; value >= 81; value--) layer.Opacity = value;
        int afterDrag = document.History.UndoName is null ? 0 : 1;
        document.Undo();

        if (layer.Opacity != 100 || afterDrag != 1)
        {
            throw new InvalidOperationException($"A drag left opacity at {layer.Opacity} and {afterDrag} undo step(s) instead of one.");
        }

        // Two values with no edit around them are two steps, so one undo only takes the last one back.
        // A fresh document, because the drag above left the value at its starting point already.
        var solo = new DocumentViewModel(
            new ImageImporter(codec), codec,
            new AdjustmentFilter(), new SelectionMaskBuilder());
        LayerViewModel soloLayer = solo.Layers[0];
        soloLayer.Opacity = 90;
        soloLayer.Opacity = 70;
        solo.Undo();

        if (soloLayer.Opacity != 90)
        {
            throw new InvalidOperationException($"A standalone value change merged with the one before it: expected 90, got {soloLayer.Opacity}.");
        }

        log.Add("undo drag=oneStep standalone=ownSteps ok");
    }

    /// <summary>
    /// The four editing tools, exercised end to end on a real document: each has to change what it
    /// says it changes and leave exactly one undo step behind.
    /// </summary>
    private static void CheckEditingTools(List<string> log)
    {
        var codec = new ImageCodec();
        var document = new DocumentViewModel(
            new ImageImporter(codec), codec,
            new AdjustmentFilter(), new SelectionMaskBuilder());

        LayerViewModel layer = document.AddLayer();

        // Written through ReplacePixels rather than by poking Model.Pixels: the tools work from the
        // layer's source buffer, and those are two different buffers.
        PixelBuffer withEdge = HorizontalEdge(document.Document.Width, document.Document.Height);
        layer.ReplacePixels(withEdge);

        int width = withEdge.Width;
        int height = withEdge.Height;
        var recorded = new List<string>();

        document.Transform.Rotate(QuarterTurn.Clockwise);
        recorded.Add(document.History.UndoName ?? string.Empty);
        if (layer.Source!.Width != height || layer.Source.Height != width)
        {
            throw new InvalidOperationException($"Rotating produced {layer.Source.Width} × {layer.Source.Height} instead of {height} × {width}.");
        }

        document.Transform.Flip(FlipAxis.Horizontal);
        recorded.Add(document.History.UndoName ?? string.Empty);

        // The turn moved the edge from vertical to horizontal, so the crop has to straddle it; a
        // corner taken from one side of the edge is a flat colour and nothing would show up in it.
        PixelBuffer turned = layer.Source!;
        int edgeY = turned.Height / 2;
        int cropY = Math.Max(0, Math.Min(edgeY - 12, turned.Height - 24));
        document.Transform.Crop(0, cropY, 32, 24);
        recorded.Add(document.History.UndoName ?? string.Empty);

        if (layer.Source!.Width != 32 || layer.Source.Height != 24)
        {
            throw new InvalidOperationException($"Cropping produced {layer.Source.Width} × {layer.Source.Height} instead of 32 × 24.");
        }

        byte[] beforeFilter = (byte[])layer.Source!.Data.Clone();
        document.Transform.GaussianBlur(4);
        recorded.Add(document.History.UndoName ?? string.Empty);
        if (layer.Source!.Data.SequenceEqual(beforeFilter))
        {
            throw new InvalidOperationException($"The blur left {layer.Source.Width} × {layer.Source.Height} unchanged.");
        }

        document.Transform.Sharpen(2, 1.5);
        recorded.Add(document.History.UndoName ?? string.Empty);

        // Each tool has to have recorded a step of its own, named after what it did.
        if (recorded.Any(name => name.Length == 0))
        {
            throw new InvalidOperationException($"A tool recorded no undo step: {string.Join(", ", recorded)}");
        }

        if (recorded.Distinct().Count() != recorded.Count)
        {
            throw new InvalidOperationException($"Two tools recorded the same undo step: {string.Join(", ", recorded)}");
        }

        foreach (string _ in recorded) document.Undo();

        if (layer.Source!.Width != width || layer.Source.Height != height)
        {
            throw new InvalidOperationException("Undoing the transforms did not restore the layer's size.");
        }

        document.ColorPicker.Pick(4, 4);
        if (!document.ColorPicker.HasSample || document.ColorPicker.Hex.Length != 7)
        {
            throw new InvalidOperationException($"The eyedropper reported '{document.ColorPicker.Hex}'.");
        }

        log.Add($"tools rotate=ok flip=ok crop=ok blur=ok sharpen=ok steps={recorded.Count} pick={document.ColorPicker.Hex}");
    }

    /// <summary>
    /// Checks the shortcut table itself and proves that a focused text box keeps its own keys.
    /// </summary>
    /// <remarks>
    /// The gestures have to be unique: two accelerators on one combination is a silent bug, because
    /// only one of them ever runs. And a text box that has focus has to win, otherwise typing a layer
    /// name would trigger commands instead of inserting letters.
    /// </remarks>
    /// <summary>
    /// Checks the shortcut table and the rule that keeps a focused control's keys to itself.
    /// </summary>
    /// <remarks>
    /// Two accelerators on one combination is a silent bug, because only one of them ever runs. The
    /// routing itself belongs to the framework: a control that handles a key first keeps it, which is
    /// what stops the layer name box from firing commands as the user types. That routing cannot be
    /// driven from inside the process - the WinUI API for it is an event rather than a callable method
    /// - so what is enforced here is the table, plus the rule that keeps bare letters to Escape alone.
    /// </remarks>
    private static void CheckShortcuts(Views.MainWindow window, List<string> log)
    {
        Views.MainWindow.Shortcut[] shortcuts = window.Shortcuts();

        string[] duplicates = [.. shortcuts
            .GroupBy(shortcut => $"{shortcut.Modifiers}+{shortcut.Key}")
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)];

        if (duplicates.Length > 0)
        {
            throw new InvalidOperationException($"Two shortcuts share a gesture: {string.Join(", ", duplicates)}");
        }

        if (shortcuts.Any(shortcut => shortcut.Hint.Length == 0))
        {
            throw new InvalidOperationException("A shortcut has no text to show in a menu.");
        }

        // Bare keys are the tool switches and Escape, and nothing else: every other gesture has to
        // carry a modifier, or typing a layer name would fire commands instead of inserting letters.
        string[] bareAllowed = ["M", "I", "Escape"];
        string[] bare = [.. shortcuts
            .Where(shortcut => shortcut.Modifiers == VirtualKeyModifiers.None)
            .Select(shortcut => shortcut.Key.ToString())];

        string[] bareUnexpected = [.. bare.Where(key => !bareAllowed.Contains(key))];
        if (bareUnexpected.Length > 0)
        {
            throw new InvalidOperationException($"Bound to a bare key without being a tool switch: {string.Join(", ", bareUnexpected)}");
        }

        // A text box has to be focusable and keep focus: that is the precondition for it winning the
        // key routing at all.
        var box = new TextBox { Text = "layer name" };
        var host = new Grid();
        host.Children.Add(box);
        (window.Content as Grid)?.Children.Add(host);

        try
        {
            box.Focus(FocusState.Programmatic);
            if (box.FocusState == FocusState.Unfocused)
            {
                throw new InvalidOperationException("A text box could not take focus, so typing could not keep its keys.");
            }

            log.Add($"shortcuts count={shortcuts.Length} duplicates=0 bareKeys=[{string.Join(",", bare)}] textBoxFocusable=yes");
            log.Add($"shortcut.keys={string.Join(" ", shortcuts.Select(shortcut => shortcut.Hint))}");
        }
        finally
        {
            (window.Content as Grid)?.Children.Remove(host);
        }
    }

    private static CommandBar? FindCommandBar(DependencyObject? node)
    {
        if (node is CommandBar bar) return bar;

        int children = node is null ? 0 : VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < children; i++)
        {
            CommandBar? found = FindCommandBar(VisualTreeHelper.GetChild(node, i));
            if (found is not null) return found;
        }

        return null;
    }

    private static void Collect(DependencyObject? node, List<string> texts)
    {
        switch (node)
        {
            case TextBlock block when !string.IsNullOrEmpty(block.Text):
                texts.Add(block.Text);
                break;
            case Button button when button.Content is string label && label.Length > 0:
                texts.Add(label);
                break;
            case CheckBox box when box.Content is string label && label.Length > 0:
                texts.Add(label);
                break;
            case ToggleSwitch toggle when toggle.Header is string label && label.Length > 0:
                texts.Add(label);
                break;
            case Expander expander when expander.Header is string label && label.Length > 0:
                texts.Add(label);
                break;
            case ComboBox combo when combo.Header is string label && label.Length > 0:
                texts.Add(label);
                break;
            case TextBox input when input.Header is string label && label.Length > 0:
                texts.Add(label);
                break;
            case NumberBox number when number.Header is string label && label.Length > 0:
                texts.Add(label);
                break;
        }

        int children = node is null ? 0 : VisualTreeHelper.GetChildrenCount(node);
        for (int i = 0; i < children; i++) Collect(VisualTreeHelper.GetChild(node, i), texts);
    }

    private static string Sample(DocumentViewModel document)
    {
        PixelBuffer pixels = document.Flatten();
        return Describe(pixels[pixels.Width / 2, pixels.Height / 2]);
    }

    private static string Describe(Rgba32 pixel) => $"{pixel.R},{pixel.G},{pixel.B},{pixel.A}";

    /// <summary>A picture with one strong edge in it, so a blur has something to soften.</summary>
    private static PixelBuffer HorizontalEdge(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte value = x < width / 2 ? (byte)30 : (byte)220;
                int i = buffer.Offset(x, y);
                buffer.Data[i] = value;
                buffer.Data[i + 1] = value;
                buffer.Data[i + 2] = value;
                buffer.Data[i + 3] = 255;
            }
        }

        return buffer;
    }

    private static void Fill(LayerViewModel layer, byte red, byte green, byte blue, byte alpha)
    {
        PixelBuffer? pixels = layer.Model.Pixels;
        if (pixels is null) return;

        for (int i = 0; i < pixels.Data.Length; i += 4)
        {
            pixels.Data[i] = red;
            pixels.Data[i + 1] = green;
            pixels.Data[i + 2] = blue;
            pixels.Data[i + 3] = alpha;
        }
    }

    private static string? Option(string[] commandLine, string name)
    {
        for (int i = 0; i < commandLine.Length; i++)
        {
            string argument = commandLine[i];
            if (argument.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return i + 1 < commandLine.Length ? commandLine[i + 1] : null;
            }

            if (argument.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
            {
                return argument[(name.Length + 1)..];
            }
        }

        return null;
    }

    /// <summary>
    /// The requested path is not always writable (a sandboxed parent, a read-only folder). Losing the
    /// log would hide the whole result, so fall back to the folder the executable sits in and say so.
    /// </summary>
    private static void WriteLog(string path, List<string> lines)
    {
        if (TryWrite(path, lines)) return;

        string fallback = Path.Combine(AppContext.BaseDirectory, "selftest.log");
        if (path == fallback) return;

        lines.Add($"could not write {path}, fell back to {fallback}");
        TryWrite(fallback, lines);
    }

    private static bool TryWrite(string path, List<string> lines)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            File.WriteAllLines(path, lines, Encoding.UTF8);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
