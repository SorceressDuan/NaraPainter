using System.Text;
using Compositor.App.Services;
using Compositor.App.Views;
using Microsoft.UI.Xaml;

namespace Compositor.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        UnhandledException += (_, args) => Report("unhandled", args.Exception);

        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            Report("app resources", error);
            throw;
        }
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        bool selfTest = SmokeTest.IsRequested(commandLine);
        StartupLog.Record(
            "OnLaunched",
            $"commandLine={Environment.CommandLine}",
            $"args=[{string.Join(" | ", commandLine)}]",
            $"selfTest={selfTest}",
            $"cwd={Environment.CurrentDirectory}",
            $"baseDir={AppContext.BaseDirectory}");

        try
        {
            _window = new MainWindow();
            _window.Activate();
        }
        catch (Exception error)
        {
            Report("main window", error);
            Environment.Exit(3);
            return;
        }

        if (!selfTest) return;

        StartupLog.Record("selfTest", "queued", $"title={_window.Title}");
        MainWindow window = _window;
        _window.DispatcherQueue.TryEnqueue(async () =>
        {
            StartupLog.Record("selfTest", "running");
            int exitCode = await SmokeTest.RunAsync(window, commandLine);
            StartupLog.Record("selfTest", $"finished exit={exitCode}");
            Environment.Exit(exitCode);
        });
    }

    /// <summary>
    /// A desktop app has no console to fall back on, so failures during startup are hard to see.
    /// This writes them next to the executable and to stderr when the caller redirected it.
    /// </summary>
    private static void Report(string stage, Exception error)
    {
        var text = new StringBuilder();
        for (Exception? current = error; current is not null; current = current.InnerException)
        {
            text.AppendLine($"[{stage}] {current.GetType().FullName}: {current.Message}");
            text.AppendLine(current.StackTrace);
        }

        string message = text.ToString();
        Console.Error.WriteLine(message);
        StartupLog.Record(stage, message);

        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"), message + Environment.NewLine);
        }
        catch (Exception writeError) when (writeError is IOException or UnauthorizedAccessException)
        {
        }
    }
}
