using System.Text.Json;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Services;

/// <summary>
/// Persists the list of tweaks applied by the technician so they can be
/// rolled back in a later session. Stored in logs/applied-tweaks.json.
/// </summary>
public sealed class TweakHistoryService
{
    private readonly string _path;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public List<AppliedTweakRecord> Records { get; private set; } = new();

    public TweakHistoryService(string logDirectory)
    {
        _path = Path.Combine(logDirectory, "applied-tweaks.json");
        Load();
    }

    public void Record(string tweakId, string tweakName)
    {
        Records.RemoveAll(r => r.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        Records.Add(new AppliedTweakRecord
        {
            TweakId = tweakId,
            TweakName = tweakName,
            AppliedAt = DateTime.Now
        });
        Save();
    }

    public void Remove(string tweakId)
    {
        var removed = Records.RemoveAll(r => r.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
        {
            Save();
        }
    }

    public bool Contains(string tweakId) => Records.Any(r => r.TweakId.Equals(tweakId, StringComparison.OrdinalIgnoreCase));

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            var json = File.ReadAllText(_path);
            Records = JsonSerializer.Deserialize<List<AppliedTweakRecord>>(json, _jsonOptions) ?? new();
        }
        catch
        {
            Records = new();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(Records, _jsonOptions));
        }
        catch
        {
            // Best effort; history persistence is not critical.
        }
    }
}

public sealed class AppliedTweakRecord
{
    public string TweakId { get; set; } = "";
    public string TweakName { get; set; } = "";
    public DateTime AppliedAt { get; set; } = DateTime.Now;
}
