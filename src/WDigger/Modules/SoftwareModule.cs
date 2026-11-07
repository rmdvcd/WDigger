using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 4 module (skeleton): Winget package management and Appx bloatware
/// removal. The configuration lives in config/software.json.
/// </summary>
public class SoftwareModule : IModule
{
    public string Name => "Software";
    public string Description => "Winget package management and Appx bloatware removal.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Software module")
                .AddChoices(
                    "List installed packages (winget list)",
                    "Search for a package (winget search)",
                    "Install a package silently (winget install)",
                    "Remove an Appx package (bloatware)",
                    "Back to main menu"));

            switch (choice)
            {
                case "List installed packages (winget list)":
                    RunWinget(host, "list");
                    break;
                case "Search for a package (winget search)":
                    {
                        var query = AnsiConsole.Ask<string>("Package name to search for");
                        if (!string.IsNullOrWhiteSpace(query))
                        {
                            RunWinget(host, $"search {query}");
                        }
                    }
                    break;
                case "Install a package silently (winget install)":
                    {
                        var id = AnsiConsole.Ask<string>("Package ID (e.g. Git.Git)");
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            AnsiConsole.MarkupLine("[yellow]Requires Administrator.[/]");
                            if (Confirm(host))
                            {
                                RunWinget(host, $"install \"{id}\" --silent");
                            }
                        }
                    }
                    break;
                case "Remove an Appx package (bloatware)":
                    RemoveAppx(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void RunWinget(AppHost host, string arguments)
    {
        if (!Confirm(host)) return;
        host.Commands.Run("winget", arguments);
    }

    private static void RemoveAppx(AppHost host)
    {
        AnsiConsole.MarkupLine("[red]Warning:[/] removing Appx packages can break Windows features. Proceed with care.");
        var pattern = AnsiConsole.Ask<string>("Package name pattern (e.g. *Weather*)");
        if (string.IsNullOrWhiteSpace(pattern)) return;
        if (!Confirm(host)) return;
        var command = $"Get-AppxPackage {pattern} | Remove-AppxPackage";
        host.Commands.Run("powershell", $"-Command \"{command}\"");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
