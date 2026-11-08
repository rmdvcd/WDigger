using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace WDigger;

/// <summary>
/// WinUI 3 entry point. The manifest normally elevates before Main; this guard
/// also covers development hosts and prevents any unelevated code path.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // The manifest normally elevates before Main. This guard also covers
        // development hosts and prevents any unelevated code path.
        if (!IsAdministrator())
        {
            return RelaunchElevated(args);
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                WriteCrashLog(exception);
            }
        };

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(_ => new App());
        return 0;
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
            var executable = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(executable))
            {
                return 1;
            }

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
            // ERROR_CANCELLED: the user declined the UAC prompt.
            WriteCrashLog(new InvalidOperationException(
                "WDigger necesita privilegios de administrador. Acepta el aviso de UAC para continuar.", exception));
            return 1;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == 740)
        {
            // ERROR_ELEVATION_REQUIRED should not happen (manifest requires admin).
            WriteCrashLog(exception);
            return 1;
        }
        catch (Exception exception)
        {
            WriteCrashLog(exception);
            return 1;
        }
    }

    internal static void WriteCrashLog(Exception exception)
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
