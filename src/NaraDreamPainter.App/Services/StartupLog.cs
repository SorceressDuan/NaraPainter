using System.Text;

namespace NaraDreamPainter.App.Services;

/// <summary>
/// A windowed app leaves no trace when a startup argument is misread or a self test dies before it
/// writes anything, and the process is gone before anyone can look at it. One line per launch, next to
/// the executable and in the temp folder, tells "never ran" apart from "ran and failed".
/// </summary>
public static class StartupLog
{
    public static void Record(string stage, params string[] details)
    {
        var body = new StringBuilder();
        body.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" [").Append(stage).Append(']');
        foreach (string detail in details)
        {
            body.Append(Environment.NewLine).Append("    ").Append(detail);
        }

        body.AppendLine();
        string text = body.ToString();

        Write(Path.Combine(AppContext.BaseDirectory, "startup.log"), text);
        Write(Path.Combine(Path.GetTempPath(), "naradreampainter-startup.log"), text);
    }

    private static void Write(string path, string text)
    {
        try
        {
            File.AppendAllText(path, text);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Diagnostics must never be the reason a launch fails.
        }
    }
}
