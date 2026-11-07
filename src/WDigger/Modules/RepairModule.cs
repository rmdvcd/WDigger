using System.Text.RegularExpressions;
using Spectre.Console;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Modules;

/// <summary>
/// Phase 3 module: SFC, DISM, CHKDSK and Windows Update repair helpers, with
/// live progress bars for long-running operations and plain-language result
/// explanations.
/// </summary>
public class RepairModule : IModule
{
    public string Name => "System repair";
    public string Description => "SFC /scannow, DISM RestoreHealth, CHKDSK scan and Windows Update component repair.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("System repair")
                .AddChoices(
                    "SFC /scannow - repair system files",
                    "DISM /RestoreHealth - repair component store",
                    "CHKDSK /scan /perf - scan volume",
                    "Repair Windows Update components",
                    "Reset Winget source cache",
                    "Back to main menu"));

            switch (choice)
            {
                case "SFC /scannow - repair system files":
                    RunSfc(host);
                    break;
                case "DISM /RestoreHealth - repair component store":
                    RunDism(host);
                    break;
                case "CHKDSK /scan /perf - scan volume":
                    RunChkdsk(host);
                    break;
                case "Repair Windows Update components":
                    RepairWindowsUpdate(host);
                    break;
                case "Reset Winget source cache":
                    ResetWingetCache(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void RunSfc(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]SFC requires Administrator and may take several minutes.[/]");
        if (!Confirm(host)) return;

        var result = RunWithProgress(host, "SFC /scannow", "sfc", "/scannow",
            "Verifying system files...", "File verification complete");

        ShowCommandResult("SFC /scannow", result);
        ExplainSfcResult(result);
    }

    private static void RunDism(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]DISM requires Administrator and an internet connection (Windows Update).[/]");
        if (!Confirm(host)) return;

        var result = RunWithProgress(host, "DISM /RestoreHealth", "DISM",
            "/Online /Cleanup-Image /RestoreHealth",
            "Repairing component store...", "Component store repair complete");

        ShowCommandResult("DISM /RestoreHealth", result);
        ExplainDismResult(result);
    }

    private static void RunChkdsk(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]CHKDSK on the system volume may schedule itself for the next reboot.[/]");
        if (!Confirm(host)) return;

        var result = RunWithProgress(host, "CHKDSK /scan /perf C:", "chkdsk",
            "/scan /perf C:", "Scanning file system...", "Disk scan complete");

        ShowCommandResult("CHKDSK /scan /perf C:", result);
        ExplainChkdskResult(result);
    }

    private static void RepairWindowsUpdate(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]This stops Windows Update services, renames SoftwareDistribution and catroot2, then restarts the services.[/]");
        if (!Confirm(host)) return;

        AnsiConsole.Progress().Start(ctx =>
        {
            var services = new[] { "wuauserv", "cryptSvc", "bits", "msiserver" };
            var stopTask = ctx.AddTask("Stopping Windows Update services...", autoStart: true);
            stopTask.MaxValue = services.Length;
            foreach (var service in services)
            {
                host.Commands.Run("net", $"stop {service}", echoOutput: false);
                stopTask.Increment(1);
                stopTask.Description = $"Stopped {service}";
            }

            var renameTask = ctx.AddTask("Renaming distribution folders...", autoStart: true);
            renameTask.MaxValue = 2;
            host.Commands.Run("cmd", "/c ren %SystemRoot%\\SoftwareDistribution SoftwareDistribution.old", echoOutput: false);
            renameTask.Increment(1);
            renameTask.Description = "Renamed SoftwareDistribution";
            host.Commands.Run("cmd", "/c ren %SystemRoot%\\System32\\catroot2 catroot2.old", echoOutput: false);
            renameTask.Increment(1);
            renameTask.Description = "Renamed catroot2";

            var startTask = ctx.AddTask("Restarting Windows Update services...", autoStart: true);
            startTask.MaxValue = services.Length;
            foreach (var service in services.Reverse())
            {
                host.Commands.Run("net", $"start {service}", echoOutput: false);
                startTask.Increment(1);
                startTask.Description = $"Started {service}";
            }
        });

        AnsiConsole.MarkupLine("[green]Windows Update component reset finished. A reboot is recommended.[/]");
    }

    private static void ResetWingetCache(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]This resets the Winget source cache.[/]");
        if (!Confirm(host)) return;
        var result = RunWithProgress(host, "Reset Winget cache", "winget",
            "source reset --force", "Resetting Winget sources...", "Winget cache reset");
        ShowCommandResult("winget source reset --force", result);
    }

    /// <summary>
    /// Runs a long operation inside a Spectre.Console progress view so the
    /// technician sees live activity instead of a frozen terminal.
    /// </summary>
    private static CommandResult RunWithProgress(AppHost host, string title, string fileName, string arguments,
        string startingMessage, string completedMessage)
    {
        return AnsiConsole.Progress().Start(async ctx =>
        {
            var task = ctx.AddTask(startingMessage, autoStart: true);
            task.MaxValue = 100;

            var result = host.Commands.Run(fileName, arguments, echoOutput: false, onOutputLine: line =>
            {
                task.Description = CommandExecutor.Shorten(line, 64);
                task.Increment(1);
                // Loop the bar so it conveys activity for operations of unknown length.
                if (task.Value >= task.MaxValue)
                {
                    task.Value = 0;
                }
            });

            task.Description = completedMessage;
            task.Value = task.MaxValue;
            return result;
        });
    }

    private static void ShowCommandResult(string label, CommandResult result)
    {
        AnsiConsole.WriteLine();
        var status = result.Success
            ? new Markup($"[green]{label} completed successfully.[/] (exit code {result.ExitCode})")
            : new Markup($"[red]{label} finished with errors.[/] (exit code {result.ExitCode})");
        AnsiConsole.Write(new Panel(status)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = result.Success ? Color.Green : Color.Red
        });

        if (!string.IsNullOrEmpty(result.StandardError))
        {
            AnsiConsole.Write(new Panel(new Text(result.StandardError))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Color.Red,
                Header = new PanelHeader("Standard error")
            });
        }

        AnsiConsole.MarkupLine($"[grey]Finished in {result.Duration.TotalSeconds:F2} seconds. Full output in log file.[/]");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }

    // ------------------------------------------------------------------
    // Plain-language result explanations
    // ------------------------------------------------------------------

    private static void ExplainSfcResult(CommandResult result)
    {
        var output = result.StandardOutput ?? string.Empty;
        var explanation = "No clear status line found in SFC output.";
        var color = Color.Grey;

        if (Regex.IsMatch(output, @"did not find any integrity violations", RegexOptions.IgnoreCase))
        {
            explanation = "Windows Resource Protection did not find any integrity violations. System files are healthy.";
            color = Color.Green;
        }
        else if (Regex.IsMatch(output, @"found corrupt files and successfully repaired them", RegexOptions.IgnoreCase))
        {
            explanation = "Corrupt system files were found and successfully repaired. A reboot is recommended.";
            color = Color.Green;
        }
        else if (Regex.IsMatch(output, @"found corrupt files but was unable to fix some of them", RegexOptions.IgnoreCase))
        {
            explanation = "Corrupt files were found but some could not be repaired. Run DISM /RestoreHealth, then SFC again.";
            color = Color.Yellow;
        }
        else if (Regex.IsMatch(output, @"could not perform the requested operation", RegexOptions.IgnoreCase))
        {
            explanation = "SFC could not complete the operation. Try running it from an elevated prompt or offline.";
            color = Color.Red;
        }

        AnsiConsole.Write(new Panel(new Markup($"[{color}]{explanation}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = color,
            Header = new PanelHeader("SFC result explanation")
        });
    }

    private static void ExplainDismResult(CommandResult result)
    {
        var output = result.StandardOutput ?? string.Empty;
        var explanation = "No clear status line found in DISM output.";
        var color = Color.Grey;

        if (Regex.IsMatch(output, @"restore operation completed successfully", RegexOptions.IgnoreCase))
        {
            explanation = "The component store repair completed successfully. Component corruption has been fixed.";
            color = Color.Green;
        }
        else if (Regex.IsMatch(output, @"no component store corruption detected", RegexOptions.IgnoreCase))
        {
            explanation = "No component store corruption was detected. Nothing to repair.";
            color = Color.Green;
        }
        else if (Regex.IsMatch(output, @"The source files could not be found|failed|error", RegexOptions.IgnoreCase))
        {
            explanation = "DISM could not complete the repair (source files missing or access denied). Ensure an internet connection and Administrator rights.";
            color = Color.Red;
        }

        AnsiConsole.Write(new Panel(new Markup($"[{color}]{explanation}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = color,
            Header = new PanelHeader("DISM result explanation")
        });
    }

    private static void ExplainChkdskResult(CommandResult result)
    {
        var output = result.StandardOutput ?? string.Empty;
        var explanation = "Review the CHKDSK output and log file for details.";
        var color = Color.Grey;

        if (Regex.IsMatch(output, @"No problems found|no errors|has been scanned", RegexOptions.IgnoreCase))
        {
            explanation = "No file system errors were found.";
            color = Color.Green;
        }
        else if (Regex.IsMatch(output, @"Correcting errors|errors found|unrecoverable", RegexOptions.IgnoreCase))
        {
            explanation = "Errors were found and/or corrected. Review the full output; a reboot may be required to finish repairs.";
            color = Color.Yellow;
        }
        else if (Regex.IsMatch(output, @"cannot lock|in use|scheduled", RegexOptions.IgnoreCase))
        {
            explanation = "The volume is in use; CHKDSK may schedule itself for the next reboot.";
            color = Color.Yellow;
        }

        AnsiConsole.Write(new Panel(new Markup($"[{color}]{explanation}[/]"))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = color,
            Header = new PanelHeader("CHKDSK result explanation")
        });
    }
}
