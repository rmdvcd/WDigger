using System.IO;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>
/// Detailed timestamped audit logger. Every action, command and its full
/// stdout/stderr is written to a log file named with the session timestamp.
/// Nothing is written to the console directly, so Spectre.Console keeps full
/// control of the terminal rendering.
/// </summary>
public sealed class AppLogger : IDisposable
{
    private readonly object _lock = new();
    private readonly StreamWriter? _writer;
    private readonly string _path;

    public AppLogger(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _path = Path.Combine(logDirectory, $"WDigger_{DateTime.Now:yyyyMMdd_HHmmss}.log");
        _writer = new StreamWriter(_path, append: false, Encoding.UTF8) { AutoFlush = true };

        Write("INFO", "WDigger logger initialized.");
        Write("INFO", $"Log file: {_path}");
    }

    public string LogFilePath => _path;

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);
    public void Debug(string message) => Write("DEBUG", message);

    public void LogCommand(CommandResult result)
    {
        Write("CMD", $"'{result.Command}' exitCode={result.ExitCode} durationMs={result.Duration.TotalMilliseconds:F0}");
        if (!string.IsNullOrEmpty(result.StandardOutput))
        {
            Write("CMD-STDOUT", result.StandardOutput);
        }
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Write("CMD-STDERR", result.StandardError);
        }
    }

    private void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            _writer?.WriteLine(line);
        }
    }

    public void Dispose() => _writer?.Dispose();
}
