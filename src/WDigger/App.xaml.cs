using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using WDigger.Core;
using WDigger.UI;

namespace WDigger;

/// <summary>Application entry point; WDigger only runs with an elevated administrator token.</summary>
public partial class App : Application
{
    private ShellWindow? _window;

    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                CrashLog.Write(exception);
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.SetObserved();
        };
    }

    public App()
    {
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            CrashLog.Write(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // The manifest requests requireAdministrator. Keep a second guard so
            // a malformed package or an unusual launcher cannot expose the UI
            // with a filtered token.
            ElevationPolicy.RequireElevatedProcess();
            _window = new ShellWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            try
            {
                NativeMessageBox.Show(
                    $"WDigger no puede continuar.\n\n{exception.Message}\n\nConsulta WDigger.crash.log junto al ejecutable o en %LOCALAPPDATA%\\WDigger\\logs para obtener más detalles.",
                    "Error de WDigger",
                    NativeMessageBox.Icon.Error);
            }
            catch
            {
                // Si el cuadro de diálogo falla, al menos el log queda escrito.
            }
            Environment.Exit(1);
        }
    }

}
