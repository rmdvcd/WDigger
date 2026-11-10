using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>Bounded JSON-lines audit log. Full command output is not persisted by default.</summary>
public sealed class AppLogger : IDisposable
{
    private const long MaxFileBytes = 2 * 1024 * 1024;
    private const int MaxFiles = 10;
    private const int MaxMessageChars = 8192;
    private readonly object _lock = new();
    private readonly Queue<string> _recent = new();
    private readonly string _session = $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}";
    private StreamWriter? _writer;
    private string? _directory;
    private string _path = "";
    private int _part;

    public AppLogger(string logDirectory)
    {
        if (!TryOpen(logDirectory))
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local)) TryOpen(Path.Combine(local, "WDigger", "logs"));
        }
        Write("INFO", "session", "Registro iniciado; las salidas completas no se guardan por defecto.");
    }

    public string LogFilePath => IsPersistent ? _path : "(solo memoria; sin registro persistente)";
    public string? LogDirectory => _directory;
    public bool IsPersistent => _writer is not null;
    public IReadOnlyList<string> RecentEvents { get { lock (_lock) return _recent.ToArray(); } }

    public void Info(string message) => Write("INFO", "application", message);
    public void Warn(string message) => Write("WARN", "application", message);
    public void Error(string message) => Write("ERROR", "application", message);
    public void Debug(string message) => Write("DEBUG", "application", message);

    public void LogCommand(CommandResult result)
    {
        Write(result.Success ? "INFO" : "WARN", "command",
            $"Comando: {result.Command}; código: {result.ExitCode}; duración: {result.Duration.TotalMilliseconds:F0} ms; salida completa no almacenada.");
    }

    private bool TryOpen(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            _directory = directory;
            OpenPart();
            return true;
        }
        catch
        {
            _writer?.Dispose();
            _writer = null;
            _directory = null;
            _path = "";
            return false;
        }
    }

    private void OpenPart()
    {
        _path = Path.Combine(_directory!, $"WDigger_{_session}_{_part++:D3}.jsonl");
        _writer = new StreamWriter(new FileStream(_path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        try
        {
            foreach (var old in new DirectoryInfo(_directory!).EnumerateFiles("WDigger_*.jsonl")
                .OrderByDescending(file => file.LastWriteTimeUtc).Skip(MaxFiles))
            {
                try { if (!string.Equals(old.FullName, _path, StringComparison.OrdinalIgnoreCase)) old.Delete(); } catch { }
            }
        }
        catch { /* Retention is best effort, never blocks a diagnostic. */ }
    }

    private void Write(string severity, string category, string message)
    {
        var safe = Redact(message);
        if (safe.Length > MaxMessageChars) safe = safe[..MaxMessageChars] + " [truncado]";
        var line = JsonSerializer.Serialize(new { timestampUtc = DateTimeOffset.UtcNow, severity, category, message = safe });
        lock (_lock)
        {
            _recent.Enqueue(line);
            while (_recent.Count > 200) _recent.Dequeue();
            try
            {
                if (_writer is null) return;
                if (_writer.BaseStream.Length + Encoding.UTF8.GetByteCount(line) + 2 > MaxFileBytes)
                {
                    _writer.Dispose();
                    _writer = null;
                    OpenPart();
                }
                _writer.WriteLine(line);
            }
            catch
            {
                try { _writer?.Dispose(); } catch { }
                _writer = null;
            }
        }
    }

    private static string Redact(string message)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile)) message = message.Replace(profile, "[PERFIL]", StringComparison.OrdinalIgnoreCase);
        // This reduces obvious exposure; arbitrary diagnostic strings may still contain sensitive data.
        message = Regex.Replace(message, @"(?i)\b[A-Z]:\\Users\\[^\\\r\n""']+", "[PERFIL]");
        message = Regex.Replace(message, @"\b(?:\d{1,3}\.){3}\d{1,3}\b", "[IP]");
        return message;
    }

    public void Dispose()
    {
        lock (_lock) { _writer?.Dispose(); _writer = null; }
    }
}
