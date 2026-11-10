using System.Diagnostics;
using System.Text;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>
/// Executes native commands without a console window, streams their output to
/// the native shell, writes the audit log and honors dry-run/cancellation.
/// </summary>
public sealed class CommandExecutor
{
    private readonly AppLogger _logger;
    private readonly Func<bool> _isDryRun;

    /// <summary>Maximum time a command is allowed to run before being killed.</summary>
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromMinutes(30);

    public CommandExecutor(AppLogger logger, Func<bool> isDryRun)
    {
        _logger = logger;
        _isDryRun = isDryRun;
    }

    public bool DryRun => _isDryRun();

    public CommandResult Run(
        string fileName,
        string arguments = "",
        bool echoOutput = true,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        return RunAsync(fileName, arguments, echoOutput, onOutputLine, cancellationToken)
            .GetAwaiter().GetResult();
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        string arguments = "",
        bool echoOutput = true,
        Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default)
    {
        var commandLine = string.IsNullOrWhiteSpace(arguments) ? fileName : $"{fileName} {arguments}";

        if (_isDryRun())
        {
            _logger.Warn($"[DRY-RUN] Would execute: {commandLine}");
            onOutputLine?.Invoke($"[SIMULACIÓN] {commandLine}");
            return new CommandResult
            {
                Command = commandLine,
                ExitCode = 0,
                StandardOutput = "[Dry-run] Command was simulated and not executed.",
                StandardError = string.Empty,
                Duration = TimeSpan.Zero
            };
        }

        _logger.Info($"Executing: {commandLine}");
        var stopwatch = Stopwatch.StartNew();
        var standardOutput = string.Empty;
        var standardError = string.Empty;
        var exitCode = -1;
        Process? process = null;
        Task<string>? readError = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                throw new InvalidOperationException($"No se pudo iniciar '{fileName}'.");
            }

            // Drain stderr concurrently to avoid blocking on a full pipe.
            readError = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var stdoutLines = new List<string>();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
            {
                stdoutLines.Add(line);
                onOutputLine?.Invoke(line);
            }

            // Wait for exit with a hard timeout so a hung process cannot block the app forever.
            var exitTask = process.WaitForExitAsync(CancellationToken.None);
            var timeoutTask = Task.Delay(DefaultCommandTimeout, CancellationToken.None);
            var completed = await Task.WhenAny(exitTask, timeoutTask);
            if (completed != exitTask)
            {
                // Process exceeded the timeout; kill it and its tree.
                _logger.Warn($"Command timed out after {DefaultCommandTimeout.TotalMinutes:F0} minutes: {commandLine}");
                TryKill(process);
                await exitTask; // Let it transition to exited.
                throw new TimeoutException($"El comando superó el tiempo de espera ({DefaultCommandTimeout.TotalMinutes:F0} min).");
            }

            standardError = await readError;
            standardOutput = string.Join(Environment.NewLine, stdoutLines);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (process is not null)
            {
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { /* Best effort. */ }
            }
            if (readError is not null)
            {
                try { await readError; } catch { /* Best effort. */ }
            }
            _logger.Warn($"Command cancelled by user: {commandLine}");
            throw;
        }
        catch (TimeoutException)
        {
            TryKill(process);
            if (process is not null)
            {
                try { await process.WaitForExitAsync(CancellationToken.None); } catch { /* Best effort. */ }
            }
            if (readError is not null)
            {
                try { await readError; } catch { /* Best effort. */ }
            }
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            stopwatch.Stop();
            _logger.Error($"Failed to execute '{commandLine}': {exception}");
            return new CommandResult
            {
                Command = commandLine,
                ExitCode = -1,
                StandardError = exception.Message,
                Duration = stopwatch.Elapsed
            };
        }
        finally
        {
            process?.Dispose();
        }

        stopwatch.Stop();
        var result = new CommandResult
        {
            Command = commandLine,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Duration = stopwatch.Elapsed
        };

        _logger.LogCommand(result);
        return result;
    }

    private static void TryKill(Process? process)
    {
        try
        {
            if (process is not null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // The process may have exited between the check and Kill.
        }
    }

    public static string Shorten(string? text, int maxLength = 70)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return text.Length <= maxLength ? text : text[..(maxLength - 3)] + "...";
    }
}
