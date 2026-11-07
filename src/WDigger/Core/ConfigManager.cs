using System.Reflection;
using System.Text.Json;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>
/// Loads editable JSON files beside the portable executable. Factory defaults
/// are embedded in the EXE, so configuration is never a separate publish dependency.
/// </summary>
public sealed class ConfigManager
{
    private readonly string _configDirectory;
    private readonly AppLogger _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public List<TweakDefinition> Tweaks { get; }
    public List<SoftwareDefinition> Software { get; }

    public ConfigManager(string configDirectory, AppLogger logger)
    {
        _configDirectory = configDirectory;
        _logger = logger;
        Tweaks = LoadJson<List<TweakDefinition>>("tweaks.json") ?? new();
        Software = LoadJson<List<SoftwareDefinition>>("software.json") ?? new();
    }

    private T? LoadJson<T>(string fileName)
    {
        var path = Path.Combine(_configDirectory, fileName);
        try
        {
            string json;
            if (File.Exists(path))
            {
                json = File.ReadAllText(path);
            }
            else
            {
                json = ReadEmbeddedDefault(fileName);
                TryWritePortableDefault(path, json);
            }

            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (Exception exception)
        {
            _logger.Error($"Failed to load configuration '{path}': {exception}");
            return default;
        }
    }

    private static string ReadEmbeddedDefault(string fileName)
    {
        var resourceName = $"WDigger.Configuration.{fileName}";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new FileNotFoundException($"Embedded configuration '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void TryWritePortableDefault(string path, string json)
    {
        try
        {
            Directory.CreateDirectory(_configDirectory);
            File.WriteAllText(path, json);
            _logger.Info($"Portable configuration created: {path}");
        }
        catch (Exception exception)
        {
            // Embedded defaults remain usable if the USB is read-only.
            _logger.Warn($"Could not create editable configuration '{path}': {exception.Message}");
        }
    }
}
