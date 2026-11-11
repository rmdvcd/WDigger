using WDigger.Services;
using WDigger.Diagnostics;

namespace WDigger.Core;

/// <summary>
/// Shared services for the lifetime of the native application. Portable data is
/// preferred beside the executable; logging falls back to LocalAppData when the
/// launch directory is protected or read-only.
/// </summary>
public sealed class AppHost : IDisposable
{
    public string BaseDirectory { get; }
    public string ConfigDirectory { get; }
    public string LogDirectory { get; }

    public AppLogger Logger { get; }
    public CommandExecutor Commands { get; }
    public RestorePointManager RestorePoints { get; }
    public SensorService Sensors { get; }
    public InventoryService Inventory { get; }
    public OperationJournal Operations { get; }
    public DiagnosticService Diagnostics { get; }
    public DiagnosticReport? LatestDiagnosticReport { get; set; }

    /// <summary>When true, modifying commands are logged but never executed.</summary>
    public bool DryRun { get; set; }

    /// <summary>True only when the process token itself is elevated.</summary>
    public bool IsAdministrator => ElevationPolicy.IsProcessElevated();

    private AppHost(string baseDirectory)
    {
        BaseDirectory = Path.GetFullPath(baseDirectory);
        var portableLogDirectory = Path.Combine(BaseDirectory, "logs");
        Logger = new AppLogger(portableLogDirectory);
        LogDirectory = Logger.LogDirectory ?? portableLogDirectory;
        ConfigDirectory = ResolveWritableDataDirectory(BaseDirectory, "config");
        Commands = new CommandExecutor(Logger, () => DryRun);
        Diagnostics = new DiagnosticService(Commands);
        RestorePoints = new RestorePointManager(Logger);
        Sensors = new SensorService(Logger);
        Inventory = new InventoryService(Logger);
        Operations = new OperationJournal(Path.Combine(LogDirectory, "operations"), Logger);

        Logger.Info($"WDigger (WinUI) host created. Process ID: {Environment.ProcessId}; base directory: {BaseDirectory}");
        Logger.Info($"Config directory: {ConfigDirectory}");
        Logger.Info($"Log directory: {LogDirectory}");
        Logger.Info($"Operation journal: {(Operations.IsPersistent ? Operations.DirectoryPath : "unavailable")}");
        Logger.Info($"Execution identity: {(IsAdministrator ? "Administrator" : "Standard user")}");
        Logger.Info($"Dry-run mode: {DryRun}");
    }

    public static AppHost Create(string baseDirectory) => new(baseDirectory);

    private static string ResolveWritableDataDirectory(string baseDirectory, string relativePath)
    {
        var portable = Path.Combine(baseDirectory, relativePath);
        if (CanWriteDirectory(portable)) return portable;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            var fallback = Path.Combine(local, "WDigger", relativePath);
            if (CanWriteDirectory(fallback)) return fallback;
        }

        // Preserve the portable path as the final diagnostic value. Callers use
        // best-effort settings/configuration, while real changes are separately
        // blocked if audit log or operation journal persistence is unavailable.
        return portable;
    }

    private static bool CanWriteDirectory(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".wdigger-{Guid.NewGuid():N}.tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => Logger.Dispose();
}
