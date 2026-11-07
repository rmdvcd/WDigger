using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 5 module (skeleton): driver backup with Export-WindowsDriver and
/// driver enumeration/installation with pnputil.
/// </summary>
public class DriversModule : IModule
{
    public string Name => "Drivers";
    public string Description => "Driver backup, enumeration and installation.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Driver module")
                .AddChoices(
                    "Backup all drivers (Export-WindowsDriver)",
                    "List installed drivers (pnputil /enum-drivers)",
                    "Add a driver package (pnputil /add-driver)",
                    "Back to main menu"));

            switch (choice)
            {
                case "Backup all drivers (Export-WindowsDriver)":
                    BackupDrivers(host);
                    break;
                case "List installed drivers (pnputil /enum-drivers)":
                    host.Commands.Run("pnputil", "/enum-drivers");
                    break;
                case "Add a driver package (pnputil /add-driver)":
                    AddDriver(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void BackupDrivers(AppHost host)
    {
        var destination = AnsiConsole.Ask<string>("Backup destination folder", "drivers-backup");
        if (string.IsNullOrWhiteSpace(destination)) return;
        if (!Confirm(host)) return;

        var path = System.IO.Path.GetFullPath(destination);
        AnsiConsole.MarkupLine($"[grey]Exporting drivers to {path} ...[/]");
        host.Commands.Run("powershell", $"-Command \"Export-WindowsDriver -Online -Destination '{path}' -Verbose\"");
    }

    private static void AddDriver(AppHost host)
    {
        var inf = AnsiConsole.Ask<string>("Path to the driver .inf file");
        if (string.IsNullOrWhiteSpace(inf)) return;
        if (!Confirm(host)) return;
        host.Commands.Run("pnputil", $"/add-driver \"{inf}\" /install");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
