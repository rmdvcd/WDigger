using Spectre.Console;
using WDigger.Core;
using WDigger.Modules;

namespace WDigger.UI;

/// <summary>
/// Main navigation shell: main menu, settings and module dispatch.
/// </summary>
public sealed class Shell
{
    private readonly AppHost _host;

    public Shell(AppHost host)
    {
        _host = host;
    }

    public void Run()
    {
        AnsiConsole.Write(new FigletText("WDigger") { Color = Color.Cyan });
        AnsiConsole.Write(new Markup("[grey]Offline Windows Diagnostics & Deep Repair Toolkit[/]\n"));
        AnsiConsole.Write(new Markup($"[grey]Execution mode: {(_host.DryRun ? "DRY-RUN (simulated actions only)" : "LIVE (real system changes)")}[/]\n"));
        AnsiConsole.Write(new Rule { BorderStyle = Color.Grey });

        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Main menu")
                .PageSize(10)
                .AddChoices(
                    "Hardware - inventory & real-time monitoring",
                    "System repair - SFC, DISM, CHKDSK",
                    "Software - Winget, Chocolatey, bloatware",
                    "Tweaks - privacy & performance",
                    "Drivers - backup & restore",
                    "Network - reset, DNS, connectivity",
                    "Disk - space & temp cleanup",
                    "Settings",
                    "Exit"));

            switch (choice)
            {
                case "Hardware - inventory & real-time monitoring":
                    new HardwareModule().Run(_host);
                    break;
                case "System repair - SFC, DISM, CHKDSK":
                    new RepairModule().Run(_host);
                    break;
                case "Software - Winget, Chocolatey, bloatware":
                    new SoftwareModule().Run(_host);
                    break;
                case "Tweaks - privacy & performance":
                    new TweaksModule().Run(_host);
                    break;
                case "Drivers - backup & restore":
                    new DriversModule().Run(_host);
                    break;
                case "Network - reset, DNS, connectivity":
                    new NetworkModule().Run(_host);
                    break;
                case "Disk - space & temp cleanup":
                    new DiskModule().Run(_host);
                    break;
                case "Settings":
                    SettingsMenu();
                    break;
                case "Exit":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return to the main menu...[/]");
            Console.ReadKey(true);
        }
    }

    private void SettingsMenu()
    {
        while (true)
        {
            var dryRunLabel = _host.DryRun ? "Disable dry-run mode" : "Enable dry-run mode";

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Settings")
                .AddChoices(dryRunLabel, "Show log file location", "Show config files location", "Back to main menu"));

            switch (choice)
            {
                case var c when c == dryRunLabel:
                    _host.DryRun = !_host.DryRun;
                    AnsiConsole.MarkupLine(_host.DryRun
                        ? "[yellow]Dry-run mode enabled. Modifying actions will be simulated.[/]"
                        : "[green]Dry-run mode disabled. Actions will affect the system.[/]");
                    break;
                case "Show log file location":
                    AnsiConsole.MarkupLine($"Log file: [underline]{_host.Logger.LogFilePath}[/]");
                    break;
                case "Show config files location":
                    AnsiConsole.MarkupLine($"Config folder: [underline]{_host.ConfigDirectory}[/]");
                    break;
                case "Back to main menu":
                    return;
            }
        }
    }
}
