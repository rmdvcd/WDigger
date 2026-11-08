using Microsoft.UI.Xaml;
using WDigger.UI;

namespace WDigger;

/// <summary>
/// Application entry point: creates the single shell window and turns fatal
/// UI-thread exceptions into crash logs instead of silent process death.
/// </summary>
public partial class App : Application
{
    private ShellWindow? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            Program.WriteCrashLog(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new ShellWindow();
        _window.Activate();
    }
}
