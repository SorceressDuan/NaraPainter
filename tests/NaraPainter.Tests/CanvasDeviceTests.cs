using Microsoft.Graphics.Canvas;
using NaraPainter.Compositing.Rendering;
using NaraPainter.Models.Text;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Guards the Win2D device that drawing is built on.
/// </summary>
/// <remarks>
/// <c>CanvasDevice.GetSharedDevice()</c> hands back one device for the whole process, shared with the
/// canvas control that paints the window. Disposing it takes the app down on the next frame, so
/// nothing here owns it and nothing here may take it away.
/// </remarks>
public class CanvasDeviceTests
{
    [Fact]
    public void RenderingTextLeavesTheSharedDeviceUsable()
    {
        // Reproduces the crash: rasterizing a run used to dispose the process-wide device on the way
        // out, and the next frame was drawn into a dead one.
        CanvasDevice device = CanvasDevice.GetSharedDevice();
        string before = Describe(device);

        var style = new TextStyle(Content: "那菈绘梦", FontFamily: "Microsoft YaHei UI", FontSize: 32);

        Assert.NotNull(TextRasterizer.Render(style, 240, 120, 10, 10));

        // The device must still answer for itself after the render.
        Assert.Equal(before, Describe(device));
    }

    [Fact]
    public void TheSharedDeviceSurvivesRepeatedRenders()
    {
        CanvasDevice device = CanvasDevice.GetSharedDevice();
        string before = Describe(device);

        for (int i = 0; i < 3; i++)
        {
            var style = new TextStyle(Content: $"run {i}", FontSize: 24);
            Assert.NotNull(TextRasterizer.Render(style, 200, 100, 5, 5));
        }

        Assert.Equal(before, Describe(device));
    }

    /// <summary>
    /// Asks the device the very thing the crash log shows failing, and reports what it said. Reading
    /// the limit is what a disposed device refuses, so reaching a value at all proves it is alive.
    /// </summary>
    private static string Describe(CanvasDevice device)
    {
        int limit = device.MaximumBitmapSizeInPixels;
        Assert.True(limit > 0, "The shared device reported no maximum bitmap size.");
        return $"{limit}/{device.IsDeviceLost()}";
    }

    [Fact]
    public void NothingDisposesTheSharedCanvasDevice()
    {
        // The behaviour above catches the one call site that had it. This catches the next one, because
        // the same line reads perfectly reasonable until a frame is drawn after it.
        // Written in pieces so this file does not match its own pattern.
        string call = "CanvasDevice.Get" + "Shared" + "Device()";

        string[] offenders = [.. SourceFiles()
            .SelectMany(file => TestFiles.ReadLines(file)
                .Select((line, index) => (file, line, number: index + 1)))
            .Where(hit => hit.line.Contains(call, StringComparison.Ordinal)
                && hit.line.TrimStart().StartsWith("using", StringComparison.Ordinal))
            .Select(hit => $"{Path.GetRelativePath(RepositoryRoot(), hit.file)}:{hit.number}: {hit.line.Trim()}")];

        Assert.True(offenders.Length == 0,
            "The shared canvas device belongs to the process and must not be disposed:"
            + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    private static IEnumerable<string> SourceFiles()
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "legacy", ".tools", ".git", ".vs", "bin", "obj", "dist"
        };
        var pending = new Stack<string>();
        pending.Push(Path.Combine(RepositoryRoot(), "src"));

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(directory, "*.cs")) yield return file;
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!skipped.Contains(Path.GetFileName(child))) pending.Push(child);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NaraPainter.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No NaraPainter.sln found above {AppContext.BaseDirectory}.");
    }
}
