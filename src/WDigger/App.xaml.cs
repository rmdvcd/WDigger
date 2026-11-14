using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WDigger.Core;
using WDigger.Diagnostics;
using WDigger.UI;

namespace WDigger;

/// <summary>
/// Application entry point. WDigger can run in two modes:
/// 1. Interactive (default): shows the WinUI 3 GUI with all tools.
/// 2. Headless (--headless): runs diagnostics and reports results without requiring a display/GUI.
/// Both modes require elevated administrator privileges.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class App : Application
{
    private const string HeadlessArgument = "--headless";
    private const string HeadlessShortArgument = "-headless";

    /// <summary>Shared services for the lifetime of the native application.</summary>
    internal static AppHost? Host { get; private set; }

    /// <summary>Whether the app is running in headless mode (no UI required).</summary>
    internal static bool IsHeadless { get; private set; }

    /// <summary>Result of a headless diagnostic run.</summary>
    public sealed class HeadlessRunResult
    {
        public DiagnosticReport? Report { get; set; }
        public string? ErrorMessage { get; set; }
        public int ExitCode { get; set; }
    }

    // Derive version from the assembly informational version or fallback
    private static string GetAppVersion()
    {
        try
        {
            var attr = Assembly.GetExecutingAssembly()!.GetCustomAttribute<AssemblyVersionAttribute>();
            return attr?.Version ?? "3.0.1";
        }
        catch
        {
            return "3.0.1";
        }
    }

    static App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                // Always attempt to write crash log, even in headless mode
                try
                {
                    CrashLog.Write(exception);
                }
                catch
                {
                    // Best effort: if we can't even write a crash log, exit.
                }
            }
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try
            {
                CrashLog.Write(e.Exception);
                e.SetObserved();
            }
            catch
            {
                // Best effort.
            }
        };
    }

    public App()
    {
        InitializeComponent();

        // Detect headless mode from command line
        var commandLine = Environment.GetCommandLineArgs();
        IsHeadless = commandLine.Any(argument =>
            argument.Equals(HeadlessArgument, StringComparison.OrdinalIgnoreCase) ||
            argument.Equals(HeadlessShortArgument, StringComparison.OrdinalIgnoreCase));

        // Debug: write command line args and headless status to file
        try
        {
            var argsStr = string.Join(" | ", commandLine);
            var diagDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly()!.Location) ?? ".";
            var debugPath = System.IO.Path.Combine(diagDir, "headless_args.txt");
            File.WriteAllText(debugPath, $"CommandLine: {argsStr}\nIsHeadless: {IsHeadless}");
        }
        catch { }

        try
        {
            // Always require elevated process - both modes need admin
            ElevationPolicy.RequireElevatedProcess();

            // Initialize the AppHost (core services) regardless of mode
            // Base directory is the directory of the running executable
            var baseDir = System.IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly()!.Location) ?? ".";
            Host = AppHost.Create(baseDir);

            // In headless mode, run diagnostics and exit
            if (IsHeadless)
            {
                RunHeadlessDiagnostics();
            }
            else
            {
                // Interactive mode: continue to OnLaunched which shows the UI
                // (no-op here since OnLaunched is overridden)
            }
        }
        catch (Exception exception)
        {
            CrashLog.Write(exception);
            try
            {
                if (IsHeadless)
                {
                    // In headless mode, write error and exit
                    Console.Error.WriteLine($"\nWDigger error: {exception.Message}\n");
                    Environment.Exit(1);
                }
                else
                {
                    // Interactive mode: show error via UI (will be caught in OnLaunched)
                    throw;
                }
            }
            catch
            {
                Environment.Exit(1);
            }
        }
    }

    /// <summary>
    /// Runs diagnostics in headless mode, writes results to log, and exits.
    /// </summary>
    private async void RunHeadlessDiagnostics()
    {
        var appVersion = GetAppVersion();
        try
        {
            AppendOutput($"\nWDigger {appVersion} · modo headless · diagnóstico sin GUI\n", OutputKind.Success);
            AppendOutput("Sesión iniciada con permisos de administrador.\n", OutputKind.Info);

            // Initialize diagnostics
            var diagnosticService = new DiagnosticService(Host!.Commands);

            // Run analysis
            var report = await diagnosticService.AnalyzeAsync();

            // Write report to log via AppLogger
            var logPath = Host!.Logger!.LogFilePath;
            AppendOutput($"Informe de diagnóstico guardado en: {logPath}\n", OutputKind.Info);

            // Output summary to console
            Console.WriteLine("\n=== WDigger Headless Diagnostic Report ===");
            Console.WriteLine(report.ToPlainText());
            Console.WriteLine("========================================\n");

            // Write to diagnostic file for later retrieval
            try
            {
                var diagDir = Path.GetDirectoryName(logPath) ?? "logs";
                Directory.CreateDirectory(diagDir);
                var reportPath = Path.Combine(diagDir, $"wdigger-headless-{DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
                File.WriteAllText(reportPath, report.ToPlainText());
                AppendOutput($"Informe guardado en: {reportPath}\n", OutputKind.Info);
            }
            catch (Exception writeEx)
            {
                AppendOutput($"No se pudo escribir el informe: {writeEx.Message}\n", OutputKind.Warning);
            }

            Environment.ExitCode = 0;
        }
        catch (Exception exception)
        {
            AppendOutput($"\nError durante el diagnóstico headless: {exception.Message}\n", OutputKind.Error);
            Console.Error.WriteLine($"\nError durante el diagnóstico headless: {exception.Message}\n");
            try { CrashLog.Write(exception); } catch { }
            Environment.ExitCode = 1;
        }
        finally
        {
            // Ensure host is disposed
            try { Host?.Dispose(); } catch { }
            Environment.Exit(Environment.ExitCode);
        }
    }

    // The following members are kept for interactive mode backward compatibility
    // but are no-ops when running headless.

    internal static void AppendOutput(string message, OutputKind kind = OutputKind.Info)
    {
        // In headless mode, output to console; in interactive mode this would go to the UI
        if (IsHeadless)
        {
            Console.WriteLine(message);
        }
        // Interactive mode: would use the UI Dispatcher, but we skip it here
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Interactive mode: show the UI window
        if (!IsHeadless)
        {
            base.OnLaunched(args);
            // The shell window is the only window of the application: create and
            // activate it explicitly so startup never depends on framework
            // heuristics (an app that starts without a window looks hung).
            if (!WDigger.UI.ShellWindow.InstanceCreated)
            {
                var window = new WDigger.UI.ShellWindow();
                window.Activate();
            }
        }
        // Headless mode: already handled in constructor; just exit
        else
        {
            try
            {
                // Ensure host is created and diagnostics ran
                Host?.Dispose();
            }
            catch { }
            Environment.Exit(0);
        }
    }
}