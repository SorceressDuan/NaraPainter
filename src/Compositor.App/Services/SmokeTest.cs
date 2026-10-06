using System.Diagnostics;
using System.Text;
using Compositor.App.ViewModels;
using Compositor.Models.Layers;
using Compositor.Models.Pixels;

namespace Compositor.App.Services;

/// <summary>
/// Drives the editing path the sandbox cannot click through: open, add a layer, switch blend modes,
/// apply an adjustment, undo and redo it, export and read the file back. Writes the log it produces
/// and returns a process exit code, so a headless run can tell success from failure.
/// Enabled with --selftest; nothing else in the app calls it.
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
        string imagePath = Option(commandLine, "--selftest") ?? Path.Combine("assets", "testimages", "photo.jpg");
        string largePath = Option(commandLine, "--large") ?? Path.Combine("assets", "testimages", "large-4000x3000.png");
        string exportPath = Option(commandLine, "--export") ?? Path.Combine(Path.GetTempPath(), "compositor-selftest.png");
        string logPath = Option(commandLine, "--log") ?? Path.Combine(AppContext.BaseDirectory, "selftest.log");

        int exitCode = 1;
        try
        {
            DocumentViewModel document = window.Document;
            log.Add($"window.title={window.Title}");

            var watch = Stopwatch.StartNew();
            document.Open(imagePath);
            log.Add($"open file={Path.GetFileName(imagePath)} size={document.SizeLabel} layers={document.Layers.Count} ms={watch.ElapsedMilliseconds}");

            LayerViewModel layer = document.AddLayer();
            Fill(layer, 128, 128, 128, 160);
            log.Add($"newLayer name={layer.Name} layers={document.Layers.Count} sample={layer.ContentLabel}");

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
            document.Adjustment.BeginEdit(AdjustmentKind.BrightnessContrast);
            document.Adjustment.Brightness = 25;
            document.Adjustment.Contrast = 10;
            string adjusted = Sample(document);
            log.Add($"adjust brightness=25 contrast=10 before={before} after={adjusted}");
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

            document.Export(exportPath);
            long written = new FileInfo(exportPath).Length;
            log.Add($"export path={exportPath} bytes={written}");
            if (written == 0) throw new InvalidOperationException("The exported file is empty.");

            PixelBuffer reread = document.Codec.Read(exportPath);
            log.Add($"reopen size={reread.Width} × {reread.Height} center={Describe(reread[reread.Width / 2, reread.Height / 2])}");
            if (reread.Width != document.Document.Width || reread.Height != document.Document.Height)
            {
                throw new InvalidOperationException("The exported image came back at a different size.");
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

    private static void WriteLog(string path, List<string> lines)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        File.WriteAllLines(path, lines, Encoding.UTF8);
    }
}
