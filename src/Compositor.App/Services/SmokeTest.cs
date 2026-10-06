using System.Diagnostics;
using System.Text;
using Compositor.App.ViewModels;
using Compositor.Models.Adjustments;
using Compositor.Models.Layers;
using Compositor.Models.Pixels;

namespace Compositor.App.Services;

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
            string exportPath = Option(commandLine, "--export") ?? Path.Combine(Path.GetTempPath(), "compositor-selftest.png");
            logPath = Option(commandLine, "--log") ?? logPath;
            StartupLog.Record("smokeTest", $"image={imagePath}", $"export={exportPath}", $"log={logPath}");
            log.Add($"options image={imagePath} export={exportPath} log={logPath} cwd={Environment.CurrentDirectory}");

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

    private static string Sample(DocumentViewModel document)
    {
        PixelBuffer pixels = document.Flatten();
        return Describe(pixels[pixels.Width / 2, pixels.Height / 2]);
    }

    private static string Describe(Rgba32 pixel) => $"{pixel.R},{pixel.G},{pixel.B},{pixel.A}";

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
