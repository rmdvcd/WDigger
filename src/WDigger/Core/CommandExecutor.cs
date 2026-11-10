using System.Diagnostics;
using System.Text;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>Runs a command, drains both pipes concurrently and records its outcome.</summary>
public sealed class CommandExecutor
{
    private readonly AppLogger _logger;
    private readonly Func<bool> _isDryRun;
    private readonly TimeSpan _timeout;

    public CommandExecutor(AppLogger logger, Func<bool> isDryRun, TimeSpan? timeout = null)
    {
        _logger = logger;
        _isDryRun = isDryRun;
        _timeout = timeout ?? TimeSpan.FromMinutes(30);
        if (_timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool DryRun => _isDryRun();

    public CommandResult Run(string fileName, string arguments = "", bool echoOutput = true,
        Action<string>? onOutputLine = null, CancellationToken cancellationToken = default,
        bool? simulate = null, bool enforceTimeout = true)
        => RunAsync(fileName, arguments, echoOutput, onOutputLine, cancellationToken, simulate, enforceTimeout)
            .GetAwaiter().GetResult();

    public Task<CommandResult> RunAsync(string fileName, string arguments = "", bool echoOutput = true,
        Action<string>? onOutputLine = null, CancellationToken cancellationToken = default,
        bool? simulate = null, bool enforceTimeout = true)
        => RunCoreAsync(fileName, arguments, null, echoOutput, onOutputLine, cancellationToken, simulate, enforceTimeout);

    public Task<CommandResult> RunArgumentsAsync(string fileName, IReadOnlyList<string> arguments,
        Action<string>? onOutputLine = null, CancellationToken cancellationToken = default,
        bool? simulate = null, bool enforceTimeout = true)
        => RunCoreAsync(fileName, "", arguments, true, onOutputLine, cancellationToken, simulate, enforceTimeout);

    private async Task<CommandResult> RunCoreAsync(string fileName, string arguments, IReadOnlyList<string>? argumentList,
        bool echoOutput, Action<string>? onOutputLine, CancellationToken cancellationToken,
        bool? simulate, bool enforceTimeout)
    {
        var executable = ResolveSystemExecutable(fileName);
        var displayArgs = argumentList is null ? arguments : string.Join(" ", argumentList.Select(arg => $"[{arg}]"));
        var commandLine = string.IsNullOrWhiteSpace(displayArgs) ? executable : $"{executable} {displayArgs}";
        cancellationToken.ThrowIfCancellationRequested();
        if (simulate ?? _isDryRun())
        {
            _logger.Warn($"[DRY-RUN] Would execute: {commandLine}");
            onOutputLine?.Invoke($"[SIMULACIÓN] {commandLine}");
            return new CommandResult { Command = commandLine, IsSimulated = true,
                StandardOutput = "[Dry-run] Command was simulated and not executed." };
        }

        _logger.Info($"Executing: {commandLine}");
        var stopwatch = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Process? process = null;
        Task? readers = null;
        using var timeout = new CancellationTokenSource();
        if (enforceTimeout) timeout.CancelAfter(_timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (argumentList is null) startInfo.Arguments = arguments;
            else foreach (var argument in argumentList) startInfo.ArgumentList.Add(argument);
            process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"No se pudo iniciar '{fileName}'.");

            // Start the deadline before waiting for *either* EOF or process exit.
            // A silent process, or a descendant holding a pipe open, must not bypass it.
            var outputTask = DrainAsync(process.StandardOutput, stdout, echoOutput ? onOutputLine : null, linked.Token);
            var errorTask = DrainAsync(process.StandardError, stderr, null, linked.Token);
            readers = Task.WhenAll(outputTask, errorTask);
            await Task.WhenAll(readers, process.WaitForExitAsync(linked.Token));
            stopwatch.Stop();
            var result = new CommandResult
            {
                Command = commandLine, ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(), StandardError = stderr.ToString(), Duration = stopwatch.Elapsed
            };
            _logger.LogCommand(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            // Do not wait indefinitely if a process cannot be killed or a descendant holds a pipe.
            if (process is not null)
            {
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            if (readers is not null)
            {
                try { await readers.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            }
            stopwatch.Stop();
            var timedOut = timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            _logger.Warn($"Command {(timedOut ? "timed out" : "cancelled")}: {commandLine}; durationMs={stopwatch.Elapsed.TotalMilliseconds:F0}; output not stored");
            if (timedOut)
                throw new TimeoutException($"El comando superó el tiempo de espera ({_timeout.TotalMinutes:F1} min). Puede haber cambios parciales; revisa el sistema.");
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            stopwatch.Stop();
            _logger.Error($"Failed to execute '{commandLine}': {exception}");
            var result = new CommandResult { Command = commandLine, ExitCode = -1,
                StandardOutput = stdout.ToString(), StandardError = exception.Message, Duration = stopwatch.Elapsed };
            _logger.LogCommand(result);
            return result;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string ResolveSystemExecutable(string fileName)
    {
        if (!OperatingSystem.IsWindows() || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)) return fileName;
        var name = Path.GetFileNameWithoutExtension(fileName);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrEmpty(system)) throw new InvalidOperationException("No se pudo resolver la carpeta del sistema.");
        if (name.Equals("powershell", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        string[] systemTools = { "sfc", "dism", "chkdsk", "netsh", "net", "cmd", "cscript", "pnputil", "rundll32", "wevtutil", "ipconfig", "tracert", "netstat" };
        return systemTools.Contains(name, StringComparer.OrdinalIgnoreCase) ? Path.Combine(system, name + ".exe") : fileName;
    }

    private static async Task DrainAsync(StreamReader reader, StringBuilder output, Action<string>? onLine, CancellationToken token)
    {
        // Continue draining after the limit so the child cannot block on a full pipe.
        const int limit = 256 * 1024;
        var buffer = new char[2048];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            var remaining = Math.Max(0, limit - output.Length);
            var accepted = Math.Min(count, remaining);
            if (accepted > 0)
            {
                var text = new string(buffer, 0, accepted);
                output.Append(text);
                onLine?.Invoke(text);
            }
            if (accepted < count && !truncated)
            {
                truncated = true;
                const string warning = "\n[Salida truncada por el límite de seguridad.]";
                output.Append(warning);
                onLine?.Invoke(warning);
            }
        }
    }

    private static void TryKill(Process? process)
    {
        try { if (process is not null && !process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* Best effort; caller must report potential partial changes. */ }
    }

    public static string Shorten(string? text, int maxLength = 70)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
