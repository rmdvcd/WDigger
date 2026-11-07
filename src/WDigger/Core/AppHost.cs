using System.IO;
using WDigger.Services;

namespace WDigger.Core;

/// <summary>
/// Hosts the shared application services (logger, command executor, hardware
/// services, configuration, ...). One instance lives for the lifetime of the
/// process and is passed to every module.
/// </summary>
public sealed class AppHost
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

    /// <summary>
    /// When true, every action that modifies the system is only simulated.
    /// Read by CommandExecutor at execution time (lazy, so the flag can be
    /// toggled from the Settings menu at runtime).
    /// </summary>
    public bool DryRun { get; set; }

    private AppHost(string baseDirectory)
    {
        BaseDirectory = baseDirectory;
        ConfigDirectory = ResolveDirectory(baseDirectory, "config");
        LogDirectory = Path.Combine(baseDirectory, "logs");

        Logger = new AppLogger(LogDirectory);
        Commands = new CommandExecutor(Logger, () => DryRun);
        RestorePoints = new RestorePointManager(Logger);
        Config = new ConfigManager(ConfigDirectory);
        Sensors = new SensorService(Logger);
        Inventory = new InventoryService(Logger);
        TweakHistory = new TweakHistoryService(LogDirectory);

        Logger.Info($"WDigger started. Base directory: {BaseDirectory}");
        Logger.Info($"Config directory: {ConfigDirectory}");
        Logger.Info($"Log directory: {LogDirectory}");
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

        // When launched from "dotnet run" or Visual Studio the executable lives
        // under bin/<configuration>/<tfm>. Walk up the tree to find the
        // solution-root config folder so development works out of the box.
        var directory = new DirectoryInfo(baseDirectory);
        while (directory is not null && directory.Parent is not null)
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
}
