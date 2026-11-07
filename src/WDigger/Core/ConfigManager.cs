using System.Text.Json;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>
/// Loads the external JSON definitions (tweaks.json, software.json) so the
/// tool can be customized and extended without recompiling.
/// </summary>
public sealed class ConfigManager
{
    private readonly string _configDirectory;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public List<TweakDefinition> Tweaks { get; }
    public List<SoftwareDefinition> Software { get; }

    public ConfigManager(string configDirectory)
    {
        _configDirectory = configDirectory;
        Tweaks = LoadJson<List<TweakDefinition>>(Path.Combine(_configDirectory, "tweaks.json")) ?? new();
        Software = LoadJson<List<SoftwareDefinition>>(Path.Combine(_configDirectory, "software.json")) ?? new();
    }

    private static T? LoadJson<T>(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return default;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[config] Failed to load {path}: {ex.Message}");
            return default;
        }
    }
}
