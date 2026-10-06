using System.Runtime.InteropServices;
using System.Text;
using NaraDreamPainter.App.Services;

namespace NaraDreamPainter.App;

/// <summary>
/// Records a failure and tells the user it happened.
/// </summary>
/// <remarks>
/// The log goes to <c>%LOCALAPPDATA%\NaraDreamPainter\crash.log</c>, which is writable even when the
/// application folder is not - a portable build is often unzipped into a read-only place. The message
/// box is user32's rather than a XAML dialog on purpose: this runs when the UI thread is already in
/// trouble, and creating a XAML dialog there is as likely to fail as the code that broke.
/// </remarks>
internal static class CrashReport
{
    private const string FolderName = "NaraDreamPainter";
    private const string FileName = "crash.log";

    private const uint IconError = 0x00000010;
    private const uint TopMost = 0x00040000;

    private static int _showing;

    /// <summary>Where the log is written, or null when no writable location could be found.</summary>
    public static string? Path { get; } = ResolvePath();

    public static void Write(string stage, Exception error)
    {
        string text = Describe(stage, error);
        Console.Error.WriteLine(text);
        StartupLog.Record(stage, text);

        string? path = Path;
        if (path is null) return;

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            File.AppendAllText(path, text + Environment.NewLine);
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Writes the failure and blocks on a dialog so the program does not simply vanish.</summary>
    public static void Report(string stage, Exception error)
    {
        Write(stage, error);

        // A second failure while the first dialog is up would stack boxes the user cannot dismiss.
        if (Interlocked.Exchange(ref _showing, 1) == 1) return;

        string where = Path is null ? string.Empty : Path;
        Show(Localization.Interpolate(Strings.CrashMessage, Environment.NewLine) + where + Environment.NewLine + Environment.NewLine + error.Message);
    }

    /// <summary>For failures that are reported but do not end the program.</summary>
    public static void Warn(string message) => Show(message);

    private static void Show(string message) =>
        MessageBoxW(IntPtr.Zero, message, "Nara Dream Painter", IconError | TopMost);

    private static string Describe(string stage, Exception error)
    {
        var text = new StringBuilder();
        text.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{stage}]");

        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            text.AppendLine($"  {current.GetType().FullName}: {current.Message}");
            text.AppendLine(current.StackTrace);
        }

        return text.ToString();
    }

    private static string? ResolvePath()
    {
        try
        {
            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                FolderName,
                FileName);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
