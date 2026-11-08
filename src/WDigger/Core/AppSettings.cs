using System.Text.Json;

namespace WDigger.Core;

/// <summary>
/// Persists small UI preferences (theme and dry-run) beside the executable in
/// config/ui-settings.json. Best effort: failures are silent because the app
/// must remain portable, including on read-only media.
/// </summary>
public sealed class AppSettings
{
    public const string FileName = "ui-settings.json";

    /// <summary>System | Light | Dark.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>When true, modifying commands are logged but never executed.</summary>
    public bool DryRun { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static AppSettings Load(string configDirectory)
    {
        try
        {
            var path = Path.Combine(configDirectory, FileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            }
        }
        catch
        {
            // Corrupt or unreadable settings must never block startup.
        }

        return new AppSettings();
    }

    public void Save(string configDirectory)
    {
        try
        {
            Directory.CreateDirectory(configDirectory);
            var path = Path.Combine(configDirectory, FileName);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Best effort; a missing settings file is not fatal.
        }
    }
}
