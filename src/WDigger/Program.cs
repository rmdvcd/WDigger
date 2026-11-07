using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using WDigger.Core;
using WDigger.UI;

namespace WDigger;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                WriteCrashLog(exception);
            }
        };

        // The manifest normally elevates before Main. This guard also covers
        // development hosts and prevents any unelevated code path.
        if (!IsAdministrator())
        {
            return RelaunchElevated(args);
        }

        try
        {
            return Core.App.Run(args);
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
            return 1;
        }
    }

    internal static bool IsAdministrator()
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

    private static int RelaunchElevated(IReadOnlyList<string> args)
    {
        try
        {
            var executable = Environment.ProcessPath ?? Application.ExecutablePath;
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true,
                Verb = "runas"
            };

            // ShellExecute does not consistently honor ArgumentList on all
            // supported .NET/Windows combinations; these are the supported flags.
            if (args.Any(argument => argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase)
                                     || argument.Equals("-dryrun", StringComparison.OrdinalIgnoreCase)))
            {
                startInfo.Arguments = "--dry-run";
            }

            Process.Start(startInfo);
            return 0;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 1223)
        {
            MessageBox.Show(
                "WDigger necesita privilegios de administrador. Acepta el aviso de Control de cuentas de usuario para continuar.",
                "Elevación requerida",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return 1;
        }
        catch (Exception exception)
        {
            ShowFatalError(exception);
            return 1;
        }
    }

    private static void ShowFatalError(Exception exception)
    {
        WriteCrashLog(exception);
        MessageBox.Show(
            $"WDigger no puede continuar.\n\n{exception.Message}\n\nConsulta WDigger.crash.log junto al ejecutable para obtener más detalles.",
            "Error de WDigger",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private static void WriteCrashLog(Exception exception)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(AppContext.BaseDirectory, "WDigger.crash.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash logging is best effort (for example, on read-only media).
        }
    }
}
