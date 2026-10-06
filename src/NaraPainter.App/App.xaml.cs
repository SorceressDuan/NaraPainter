using NaraPainter.App.Services;
using NaraPainter.App.Views;
using Microsoft.UI.Xaml;

namespace NaraPainter.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        // Both places a failure can surface from: the UI thread and a task nobody awaited. Neither is
        // allowed to take the window down without leaving a log and a message behind.
        UnhandledException += (_, args) => CrashReport.Report("unhandled", args.Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashReport.Write("unobserved task", args.Exception);
            args.SetObserved();
        };

        try
        {
            InitializeComponent();
        }
        catch (Exception error)
        {
            CrashReport.Report("app resources", error);
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
            CrashReport.Report("main window", error);
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
}
