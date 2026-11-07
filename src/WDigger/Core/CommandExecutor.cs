using System.Diagnostics;
using System.Text;
using Spectre.Console;
using WDigger.Models;

namespace WDigger.Core;

/// <summary>
/// Executes native Windows commands and PowerShell scripts, capturing stdout
/// and stderr, logging everything to the audit log, and honoring dry-run mode.
/// </summary>
public sealed class CommandExecutor
{
    private readonly AppLogger _logger;
    private readonly Func<bool> _isDryRun;

    public CommandExecutor(AppLogger logger, Func<bool> isDryRun)
    {
        _logger = logger;
        _isDryRun = isDryRun;
    }

    public bool DryRun => _isDryRun();

    /// <summary>
    /// Runs a native command (e.g. sfc, dism, netsh, winget, pnputil, reg).
    /// When <paramref name="onOutputLine"/> is provided, each line of stdout is
    /// reported as it is produced (useful for live progress rendering); stdout
    /// is still buffered and returned in <see cref="CommandResult.StandardOutput"/>.
    /// </summary>
    public CommandResult Run(string fileName, string arguments = "", bool echoOutput = true,
        Action<string>? onOutputLine = null, CancellationToken cancellationToken = default)
    {
        return RunAsync(fileName, arguments, echoOutput, onOutputLine, cancellationToken)
            .GetAwaiter().GetResult();
    }

    private async Task<CommandResult> RunAsync(string fileName, string arguments, bool echoOutput,
        Action<string>? onOutputLine, CancellationToken cancellationToken)
    {
        var commandLine = string.IsNullOrWhiteSpace(arguments)
            ? fileName
            : $"{fileName} {arguments}";

        if (_isDryRun())
        {
            _logger.Warn($"[DRY-RUN] Would execute: {commandLine}");
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
        var sw = Stopwatch.StartNew();
        string standardOutput = string.Empty;
        string standardError = string.Empty;
        int exitCode = -1;

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

            using var process = Process.Start(startInfo)!;

            // Drain stderr concurrently so a large error stream cannot deadlock
            // the process while we read stdout line by line below.
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);

            var stdoutLines = new List<string>();
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync(cancellationToken)) is not null)
            {
                stdoutLines.Add(line);
                onOutputLine?.Invoke(line);
            }

            await process.WaitForExitAsync(cancellationToken);
            standardError = await readError;
            standardOutput = string.Join(Environment.NewLine, stdoutLines);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn($"Command cancelled by user: {commandLine}");
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.Error($"Failed to execute '{commandLine}': {ex.Message}");
            return new CommandResult
            {
                Command = commandLine,
                ExitCode = -1,
                StandardError = ex.Message,
                Duration = sw.Elapsed
            };
        }

        sw.Stop();
        var result = new CommandResult
        {
            Command = commandLine,
            ExitCode = exitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            Duration = sw.Elapsed
        };

        _logger.LogCommand(result);

        // Only render the full output panel when we are not streaming lines to a
        // progress UI (otherwise we'd print twice and break the progress view).
        if (echoOutput && onOutputLine == null)
        {
            RenderResult(result);
        }

        return result;
    }

    private static void RenderResult(CommandResult result)
    {
        var output = Truncate(result.StandardOutput, 250);
        if (!string.IsNullOrEmpty(output))
        {
            var borderStyle = result.Success ? Color.Green : Color.Red;
            AnsiConsole.Write(new Panel(new Text(output))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = borderStyle,
                Header = new PanelHeader($"Standard output (exit code {result.ExitCode})")
            });
        }

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            AnsiConsole.Write(new Panel(new Text(result.StandardError))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Color.Red,
                Header = new PanelHeader("Standard error")
            });
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]Finished in {result.Duration.TotalSeconds:F2} seconds.[/]");
    }

    /// <summary>
    /// Keeps very long command output (DISM/SFC logs) from flooding the terminal.
    /// </summary>
    private static string Truncate(string? output, int maxLines = 250)
    {
        if (string.IsNullOrEmpty(output))
        {
            return output ?? string.Empty;
        }

        var lines = output.Split('\n');
        if (lines.Length <= maxLines)
        {
            return output;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"... output truncated: {lines.Length - maxLines} lines omitted, showing the last {maxLines} lines ...");
        for (var i = lines.Length - maxLines; i < lines.Length; i++)
        {
            sb.AppendLine(lines[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Shortens a single line for display in a live progress description.
    /// </summary>
    public static string Shorten(string? text, int maxLength = 70)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        return text.Length <= maxLength
            ? text
            : text.Substring(0, maxLength - 3) + "...";
    }
}
