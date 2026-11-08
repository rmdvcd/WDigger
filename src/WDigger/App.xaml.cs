using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using WDigger.Core;
using WDigger.UI;

namespace WDigger;

/// <summary>
/// Application entry point. The Windows App SDK build generates the host entry
/// point (InitializeComWrappers + Application.Start), which instantiates App
/// directly. Elevation and crash logging therefore live in the static
/// constructor — before any window exists — and in OnLaunched.
///
/// The manifest normally elevates before any of this runs; the guard also
/// covers hosts that ignore the manifest and prevents any unelevated path.
/// </summary>
public partial class App : Application
{
    private ShellWindow? _window;

    static App()
    {
        GuardElevation();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                CrashLog.Write(exception);
            }
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
            _window = new ShellWindow();
            _window.Activate();
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            NativeMessageBox.Show(
                $"WDigger no puede continuar.\n\n{exception.Message}\n\nConsulta WDigger.crash.log junto al ejecutable para obtener más detalles.",
                "Error de WDigger",
                NativeMessageBox.Icon.Error);
            Environment.Exit(1);
        }
    }

    private static void GuardElevation()
    {
        if (IsAdministrator())
        {
            return;
        }

        try
        {
            var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable))
            {
                CrashLog.Write(new InvalidOperationException(
                    "No se pudo resolver la ruta del ejecutable para relanzar elevado."));
                Environment.Exit(1);
                return;
            }

            var commandLine = Environment.GetCommandLineArgs();
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            // ShellExecute does not consistently honor ArgumentList on all
            // supported .NET/Windows combinations; these are the supported flags.
            if (commandLine.Any(argument => argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)
                                            || argument.Equals("-dryrun", StringComparison.OrdinalIgnoreCase)))
            {
                startInfo.Arguments = "--dry-run";
            }

            Process.Start(startInfo);
            Environment.Exit(0);
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED: the user declined the UAC prompt.
            NativeMessageBox.Show(
                "WDigger necesita privilegios de administrador. Acepta el aviso de Control de cuentas de usuario para continuar.",
                "Elevación requerida",
                NativeMessageBox.Icon.Warning);
            Environment.Exit(1);
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            NativeMessageBox.Show(
                $"WDigger no puede continuar.\n\n{exception.Message}",
                "Error de WDigger",
                NativeMessageBox.Icon.Error);
            Environment.Exit(1);
        }
    }

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }
}
