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
    /// </summary>
    public CommandResult Run(string fileName, string arguments = "", bool echoOutput = true, CancellationToken cancellationToken = default)
    {
        return RunAsync(fileName, arguments, echoOutput, cancellationToken).GetAwaiter().GetResult();
    }

    private async Task<CommandResult> RunAsync(string fileName, string arguments, bool echoOutput, CancellationToken cancellationToken)
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

            // Read both streams concurrently so a large output cannot deadlock
            // the process, then wait for it to exit.
            var readOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var readError = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);
            standardOutput = await readOutput;
            standardError = await readError;
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

        if (echoOutput)
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
}
