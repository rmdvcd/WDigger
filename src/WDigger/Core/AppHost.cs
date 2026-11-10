using System.Security.Principal;
using WDigger.Services;
using WDigger.Diagnostics;

namespace WDigger.Core;

/// <summary>
/// Shared services for the lifetime of the native application. All mutable data
/// lives beside the executable so a published copy can run directly from USB.
/// </summary>
public sealed class AppHost : IDisposable
{
    public string BaseDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogDirectory { get; }

    public AppLogger Logger { get; }
    public CommandExecutor Commands { get; }
    public RestorePointManager RestorePoints { get; }
    public ConfigManager Config { get; }
    public SensorService Sensors { get; }
    public InventoryService Inventory { get; }
    public TweakHistoryService TweakHistory { get; }
    public DiagnosticService Diagnostics { get; } = new();
    public DiagnosticReport? LatestDiagnosticReport { get; set; }

    /// <summary>When true, modifying commands are logged but never executed.</summary>
    public bool DryRun { get; set; }

    public bool IsAdministrator
    {
        get
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
    }

    private AppHost(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        ConfigDirectory = ResolveDirectory(BaseDirectory, "config");
        var portableLogDirectory = Path.Combine(BaseDirectory, "logs");
        Logger = new AppLogger(portableLogDirectory);
        LogDirectory = Logger.LogDirectory ?? portableLogDirectory;
        Commands = new CommandExecutor(Logger, () => DryRun);
        RestorePoints = new RestorePointManager(Logger);
        Config = new ConfigManager(ConfigDirectory, Logger);
        Sensors = new SensorService(Logger);
        Inventory = new InventoryService(Logger);
        TweakHistory = new TweakHistoryService(portableLogDirectory);

        Logger.Info($"WDigger (WinUI) host created. Process ID: {Environment.ProcessId}; base directory: {BaseDirectory}");
        Logger.Info($"Config directory: {ConfigDirectory}");
        Logger.Info($"Log directory: {LogDirectory}");
        Logger.Info($"Execution identity: {(IsAdministrator ? "Administrator" : "Standard user")}");
        Logger.Info($"Dry-run mode: {DryRun}");
    }

    public static AppHost Create(string baseDirectory) => new(baseDirectory);

    private static string ResolveDirectory(string baseDirectory, string relativePath)
    {
        var direct = Path.Combine(baseDirectory, relativePath);
        if (Directory.Exists(direct))
        {
            return direct;
        }

        // Development fallback: find the repository-level config directory.
        // Published single-file applications use the direct portable path.
        var directory = new DirectoryInfo(baseDirectory);
        while (directory.Parent is not null)
        {
            directory = directory.Parent;
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return direct;
    }

    public void Dispose() => Logger.Dispose();
}
