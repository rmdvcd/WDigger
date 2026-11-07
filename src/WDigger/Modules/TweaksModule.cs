using System.Linq;
using Spectre.Console;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Modules;

/// <summary>
/// Phase 4 module (core): applies and reverts tweaks defined in
/// config/tweaks.json. Creates a system restore point before applying
/// (when reversible), and fully supports dry-run mode.
/// </summary>
public class TweaksModule : IModule
{
    public string Name => "Tweaks";
    public string Description => "Privacy, performance and telemetry tweaks from config/tweaks.json.";

    public void Run(AppHost host)
    {
        var tweaks = host.Config.Tweaks;
        if (tweaks.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tweaks found in config/tweaks.json.[/]");
            return;
        }

        while (true)
        {
            var table = new Table("Id", "Name", "Category", "Reversible")
            {
                Border = TableBorder.Rounded
            };
            foreach (var t in tweaks)
            {
                table.AddRow(t.Id, t.Name, t.Category, t.Reversible ? "Yes" : "No");
            }
            AnsiConsole.Write(table);

            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Select a tweak")
                .AddChoices(tweaks.Select(t => t.Id).Append("Back to main menu").ToArray()));

            if (choice == "Back to main menu")
            {
                return;
            }

            var tweak = tweaks.First(t => t.Id == choice);
            ApplyTweak(host, tweak);

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ApplyTweak(AppHost host, TweakDefinition tweak)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]{tweak.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]{tweak.Description}[/]");

        var preview = new Table("Action", "Detail")
        {
            Border = TableBorder.Rounded
        };
        foreach (var action in tweak.Actions)
        {
            preview.AddRow(action.Type, FormatAction(action));
        }
        AnsiConsole.Write(preview);

        if (!Confirm(host, tweak))
        {
            return;
        }

        if (tweak.Reversible && !host.DryRun)
        {
            var created = host.RestorePoints.Create($"WDigger tweak: {tweak.Name}");
            AnsiConsole.MarkupLine(created
                ? "[green]System restore point created before applying the tweak.[/]"
                : "[yellow]Restore point could not be created; continuing anyway.[/]");
        }

        foreach (var action in tweak.Actions)
        {
            ExecuteAction(host, action);
        }

        AnsiConsole.MarkupLine($"[green]Tweak '{tweak.Name}' applied (simulated if dry-run mode is on).[/]");
    }

    private static string FormatAction(TweakAction action) => action.Type switch
    {
        "registry" => $"reg add \"{action.Hive}\\{action.Key}\" /v {action.Value} /t {MapKind(action.Kind)} /d {action.Data} /f",
        _ => action.Command
    };

    private static string MapKind(string kind) => kind switch
    {
        "DWord" => "REG_DWORD",
        "QWord" => "REG_QWORD",
        "String" => "REG_SZ",
        "ExpandString" => "REG_EXPAND_SZ",
        "Binary" => "REG_BINARY",
        _ => "REG_DWORD"
    };

    private static bool Confirm(AppHost host, TweakDefinition tweak)
    {
        AnsiConsole.MarkupLine(host.DryRun
            ? "[grey][DRY-RUN - commands will be simulated][/]"
            : "[red][LIVE - commands and registry changes will run][/]");
        return host.DryRun || AnsiConsole.Confirm($"Apply tweak '{tweak.Name}'?");
    }

    private static void ExecuteAction(AppHost host, TweakAction action)
    {
        var commandLine = action.Type.Equals("registry", StringComparison.OrdinalIgnoreCase)
            ? $"reg add \"{action.Hive}\\{action.Key}\" /v {action.Value} /t {MapKind(action.Kind)} /d {action.Data} /f"
            : action.Command;

        host.Commands.Run("cmd", $"/c \"{commandLine}\"");
    }
}
