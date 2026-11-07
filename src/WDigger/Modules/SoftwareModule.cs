using System.Linq;
using Spectre.Console;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Modules;

/// <summary>
/// Phase 4 module: Winget package management and Appx bloatware removal.
/// Package definitions live in config/software.json and can be customized
/// without recompiling.
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
                    "Install packages from config (silent, unattended)",
                    "Install a single package from config",
                    "Search for a package (winget search)",
                    "List installed packages (winget list)",
                    "Remove an Appx package (bloatware)",
                    "Back to main menu"));

            switch (choice)
            {
                case "Install packages from config (silent, unattended)":
                    InstallAllConfigured(host);
                    break;
                case "Install a single package from config":
                    InstallSingleConfigured(host);
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
                case "List installed packages (winget list)":
                    RunWinget(host, "list");
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

    private static void InstallAllConfigured(AppHost host)
    {
        var packages = host.Config.Software;
        if (packages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No packages defined in config/software.json.[/]");
            return;
        }

        var table = new Table("Id", "Name", "Source", "Category")
        {
            Border = TableBorder.Rounded
        };
        foreach (var p in packages)
        {
            table.AddRow(p.Id, p.Name, p.Source, p.Category);
        }
        AnsiConsole.Write(table);

        if (!Confirm(host)) return;

        AnsiConsole.Progress().Start(ctx =>
        {
            var task = ctx.AddTask("Installing packages...", autoStart: true);
            task.MaxValue = packages.Count;

            foreach (var pkg in packages)
            {
                task.Description = $"Installing {pkg.Name} ({pkg.Id})";
                var arguments = pkg.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase)
                    ? $"install {pkg.Id} {pkg.Arguments} -y"
                    : $"install \"{pkg.Id}\" {pkg.Arguments} --silent --accept-package-agreements --accept-source-agreements";
                host.Commands.Run(pkg.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ? "choco" : "winget",
                    arguments, echoOutput: false, onOutputLine: line => task.Description = CommandExecutor.Shorten(line, 60));
                task.Increment(1);
            }
        });

        AnsiConsole.MarkupLine("[green]All configured packages processed (simulated if dry-run mode is on).[/]");
    }

    private static void InstallSingleConfigured(AppHost host)
    {
        var packages = host.Config.Software;
        if (packages.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No packages defined in config/software.json.[/]");
            return;
        }

        var pkg = AnsiConsole.Prompt(new SelectionPrompt<SoftwareDefinition>()
            .Title("Select a package to install")
            .UseConverter(p => $"{p.Name} ({p.Id}) [{p.Source}]")
            .AddChoices(packages));

        if (pkg == null) return;
        if (!Confirm(host)) return;

        var arguments = pkg.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase)
            ? $"install {pkg.Id} {pkg.Arguments} -y"
            : $"install \"{pkg.Id}\" {pkg.Arguments} --silent --accept-package-agreements --accept-source-agreements";
        RunWinget(host, pkg.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ? "choco" : "winget", arguments,
            $"Installing {pkg.Name}...");
    }

    private static void RunWinget(AppHost host, string fileName, string arguments, string? statusMessage = null)
    {
        if (!Confirm(host)) return;
        if (statusMessage is not null)
        {
            AnsiConsole.MarkupLine($"[grey]{statusMessage}[/]");
        }
        host.Commands.Run(fileName, arguments);
    }

    private static void RunWinget(AppHost host, string arguments) => RunWinget(host, "winget", arguments);

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
