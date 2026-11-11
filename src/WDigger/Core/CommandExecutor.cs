using System.Diagnostics;
using System.Text;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>Runs reviewed Windows commands, drains both pipes concurrently and records their outcome.</summary>
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

    /// <summary>Runs a closed-set tool with separately supplied arguments.</summary>
    public Task<CommandResult> RunAsync(CommandSpec specification, Action<string>? onOutputLine = null,
        CancellationToken cancellationToken = default, bool? simulate = null, bool enforceTimeout = true,
        TimeSpan? timeoutOverride = null)
    {
        ArgumentNullException.ThrowIfNull(specification);
        return RunCoreAsync(ResolveTrustedExecutable(specification.Tool), specification.Arguments, onOutputLine,
            cancellationToken, simulate, enforceTimeout, timeoutOverride ?? specification.Timeout);
    }

    // Kept internal so the safety project can exercise pipe, cancellation and
    // argument-boundary behavior against its own child process. Production
    // modules only receive the public CommandSpec entry point above.
    internal Task<CommandResult> RunForTestAsync(string verifiedExecutable, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default, bool? simulate = null, TimeSpan? timeoutOverride = null)
    {
        if (string.IsNullOrWhiteSpace(verifiedExecutable) || !Path.IsPathFullyQualified(verifiedExecutable))
        {
            throw new ArgumentException("Las pruebas deben proporcionar una ruta absoluta verificada.", nameof(verifiedExecutable));
        }
        return RunCoreAsync(Path.GetFullPath(verifiedExecutable), arguments, null, cancellationToken, simulate,
            enforceTimeout: true, timeoutOverride);
    }

    private async Task<CommandResult> RunCoreAsync(string executable, IReadOnlyList<string> arguments,
        Action<string>? onOutputLine, CancellationToken cancellationToken, bool? simulate,
        bool enforceTimeout, TimeSpan? timeoutOverride)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        // Arguments can contain profile paths, package names, proxy endpoints, or
        // encoded script bodies. Keep the audit identity useful without storing
        // their complete values by default.
        var commandLine = $"{Path.GetFileName(executable)} ({arguments.Count} argumento(s))";
        cancellationToken.ThrowIfCancellationRequested();
        if (simulate ?? _isDryRun())
        {
            _logger.Warn($"[DRY-RUN] Would execute: {commandLine}");
            onOutputLine?.Invoke($"[SIMULACIÓN] {commandLine}");
            return new CommandResult
            {
                Command = commandLine,
                IsSimulated = true,
                StandardOutput = "[Dry-run] Command was simulated and not executed."
            };
        }

        var effectiveTimeout = timeoutOverride ?? _timeout;
        if (effectiveTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutOverride));
        }

        var stopwatch = Stopwatch.StartNew();
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Process? process = null;
        Task? readers = null;
        using var timeout = new CancellationTokenSource();
        if (enforceTimeout) timeout.CancelAfter(effectiveTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        try
        {
            _logger.Info($"Executing: {commandLine}");
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
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            process = new Process { StartInfo = startInfo };
            if (!process.Start()) throw new InvalidOperationException($"No se pudo iniciar '{Path.GetFileName(executable)}'.");

            // The deadline begins before waiting for either EOF or process exit.
            // A silent process, or a descendant holding a pipe open, cannot bypass it.
            var outputTask = DrainAsync(process.StandardOutput, stdout, onOutputLine, linked.Token);
            var errorTask = DrainAsync(process.StandardError, stderr, null, linked.Token);
            readers = Task.WhenAll(outputTask, errorTask);
            await Task.WhenAll(readers, process.WaitForExitAsync(linked.Token));
            stopwatch.Stop();
            var result = new CommandResult
            {
                Command = commandLine,
                ExitCode = process.ExitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                Duration = stopwatch.Elapsed
            };
            _logger.LogCommand(result);
            return result;
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
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
            {
                throw new TimeoutException($"El comando superó el tiempo de espera ({effectiveTimeout.TotalMinutes:F1} min). Puede haber cambios parciales; revisa el sistema.");
            }
            throw;
        }
        catch (Exception exception)
        {
            TryKill(process);
            stopwatch.Stop();
            _logger.Error($"Failed to execute '{commandLine}': {exception}");
            var result = new CommandResult
            {
                Command = commandLine,
                ExitCode = -1,
                StandardOutput = stdout.ToString(),
                StandardError = exception.Message,
                Duration = stopwatch.Elapsed
            };
            _logger.LogCommand(result);
            return result;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string ResolveTrustedExecutable(WindowsTool tool)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("WDigger solo puede ejecutar herramientas de Windows.");
        }

        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrEmpty(system))
        {
            throw new InvalidOperationException("No se pudo resolver la carpeta del sistema.");
        }

        return tool switch
        {
            WindowsTool.PowerShell => Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
            WindowsTool.BcdEdit => Path.Combine(system, "bcdedit.exe"),
            WindowsTool.Chkdsk => Path.Combine(system, "chkdsk.exe"),
            WindowsTool.Cscript => Path.Combine(system, "cscript.exe"),
            WindowsTool.Dism => Path.Combine(system, "dism.exe"),
            WindowsTool.Ipconfig => Path.Combine(system, "ipconfig.exe"),
            WindowsTool.Net => Path.Combine(system, "net.exe"),
            WindowsTool.Netsh => Path.Combine(system, "netsh.exe"),
            WindowsTool.Netstat => Path.Combine(system, "netstat.exe"),
            WindowsTool.PnPUtil => Path.Combine(system, "pnputil.exe"),
            WindowsTool.Rundll32 => Path.Combine(system, "rundll32.exe"),
            WindowsTool.Sfc => Path.Combine(system, "sfc.exe"),
            WindowsTool.Tracert => Path.Combine(system, "tracert.exe"),
            WindowsTool.Wevtutil => Path.Combine(system, "wevtutil.exe"),
            _ => throw new InvalidOperationException($"La herramienta '{tool}' no está autorizada.")
        };
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
}
