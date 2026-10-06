using Compositor.App.Services;
using Compositor.App.Views;
using Microsoft.UI.Xaml;

namespace Compositor.App;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();

        string[] commandLine = Environment.GetCommandLineArgs();
        if (SmokeTest.IsRequested(commandLine))
        {
            MainWindow window = _window;
            _window.DispatcherQueue.TryEnqueue(async () => Environment.Exit(await SmokeTest.RunAsync(window, commandLine)));
        }
    }
}
