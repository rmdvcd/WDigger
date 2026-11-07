using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 5 module (skeleton): disk space analysis, CHKDSK and temp file
/// cleanup.
/// </summary>
public class DiskModule : IModule
{
    public string Name => "Disk";
    public string Description => "Disk space analysis, CHKDSK scan and temporary file cleanup.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Disk module")
                .AddChoices(
                    "Show logical disk space",
                    "Clean temporary files (%TEMP%)",
                    "Scan a drive (CHKDSK /scan /perf)",
                    "Back to main menu"));

            switch (choice)
            {
                case "Show logical disk space":
                    host.Commands.Run("powershell",
                        "-Command \"Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID, Description, Size, FreeSpace, FileSystem | Format-Table -AutoSize -Wrap\"");
                    break;
                case "Clean temporary files (%TEMP%)":
                    CleanTempFiles(host);
                    break;
                case "Scan a drive (CHKDSK /scan /perf)":
                    ScanDrive(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void CleanTempFiles(AppHost host)
    {
        AnsiConsole.MarkupLine("[red]Warning:[/] this deletes files in %TEMP%. Files in use by running applications will be skipped.");
        if (!Confirm(host)) return;
        host.Commands.Run("cmd", "/c del /s /f /q %TEMP%\\* 2>nul");
        AnsiConsole.MarkupLine("[green]Temporary file cleanup finished.[/]");
    }

    private static void ScanDrive(AppHost host)
    {
        var drive = AnsiConsole.Ask<string>("Drive letter to scan", "C");
        if (string.IsNullOrWhiteSpace(drive)) return;
        drive = drive.TrimEnd(':');
        AnsiConsole.MarkupLine("[yellow]CHKDSK on the system volume may schedule itself for the next reboot.[/]");
        if (!Confirm(host)) return;
        host.Commands.Run("chkdsk", $"/scan /perf {drive}:");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
