using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 3 module: SFC, DISM, CHKDSK and Windows Update repair helpers.
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
        var result = host.Commands.Run("sfc", "/scannow");
        if (!result.Success)
        {
            AnsiConsole.MarkupLine("[red]SFC reported errors. See the log file for full output.[/]");
        }
    }

    private static void RunDism(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]DISM requires Administrator and an internet connection (Windows Update).[/]");
        if (!Confirm(host)) return;
        var result = host.Commands.Run("DISM", "/Online /Cleanup-Image /RestoreHealth");
        if (!result.Success)
        {
            AnsiConsole.MarkupLine("[red]DISM reported errors. See the log file for full output.[/]");
        }
    }

    private static void RunChkdsk(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]CHKDSK on the system volume may schedule itself for the next reboot.[/]");
        if (!Confirm(host)) return;
        var result = host.Commands.Run("chkdsk", "/scan /perf C:");
        if (!result.Success)
        {
            AnsiConsole.MarkupLine("[red]CHKDSK reported errors. See the log file for full output.[/]");
        }
    }

    private static void RepairWindowsUpdate(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]This stops Windows Update services, renames SoftwareDistribution and catroot2, then restarts the services.[/]");
        if (!Confirm(host)) return;

        foreach (var service in new[] { "wuauserv", "cryptSvc", "bits", "msiserver" })
        {
            host.Commands.Run("net", $"stop {service}");
        }

        host.Commands.Run("cmd", "/c ren %SystemRoot%\\SoftwareDistribution SoftwareDistribution.old");
        host.Commands.Run("cmd", "/c ren %SystemRoot%\\System32\\catroot2 catroot2.old");

        foreach (var service in new[] { "bits", "msiserver", "cryptSvc", "wuauserv" })
        {
            host.Commands.Run("net", $"start {service}");
        }

        AnsiConsole.MarkupLine("[green]Windows Update component reset finished. A reboot is recommended.[/]");
    }

    private static void ResetWingetCache(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]This resets the Winget source cache.[/]");
        if (!Confirm(host)) return;
        host.Commands.Run("winget", "source reset --force");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
