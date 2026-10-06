using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NaraPainter.Bootstrap;

/// <summary>
/// Starts the application that lives in the app folder next to this executable.
/// </summary>
/// <remarks>
/// A separate launcher exists only so the folder a user unzips holds one obvious thing to run while
/// several hundred runtime files stay in a subfolder. It passes the working directory explicitly,
/// because the application resolves its own runtime and content relative to it.
///
/// The one dialog it can show goes through user32 directly. The project does not reference
/// presentation frameworks, which keeps a build-time dependency and a banned namespace out of the tree.
/// </remarks>
internal static class Program
{
    private const string AppFolder = "..\\app";
    private const string AppExecutable = "NaraPainter.exe";

    private const uint IconError = 0x00000010;

    // The product name as the interface shows it. Escaped rather than typed out so this file stays
    // ASCII, and kept here rather than read from the application's resources because this is a
    // separate assembly that has to work even when the application folder is missing.
    private static readonly string Product = new([(char)0x90A3, (char)0x83C8, (char)0x7ED8, (char)0x68A6]);

    [STAThread]
    private static int Main(string[] arguments)
    {
        // Resolved against this program's own folder, not the working directory, so the shortcut, the
        // batch file and a double-click all land on the same place.
        string launcherFolder = AppContext.BaseDirectory;
        string target = Path.GetFullPath(Path.Combine(launcherFolder, AppFolder, AppExecutable));

        if (!File.Exists(target))
        {
            Report(
                $"Could not find {AppExecutable} in the app folder beside this one.{Environment.NewLine}{Environment.NewLine}" +
                $"Expected: {target}{Environment.NewLine}{Environment.NewLine}" +
                "Keep the app and launcher folders together, and unzip the whole archive before running it.");
            return 1;
        }

        var start = new ProcessStartInfo(target)
        {
            WorkingDirectory = Path.GetDirectoryName(target)!,
            UseShellExecute = false
        };

        foreach (string argument in arguments) start.ArgumentList.Add(argument);

        try
        {
            using Process? app = Process.Start(start);
            return app is null ? 1 : 0;
        }
        catch (Exception error)
        {
            Report($"The application could not be started.{Environment.NewLine}{Environment.NewLine}{error.Message}");
            return 1;
        }
    }

    private static void Report(string message) =>
        MessageBoxW(IntPtr.Zero, message, Product, IconError);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
