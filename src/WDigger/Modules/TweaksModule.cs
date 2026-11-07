using System.Linq;
using Spectre.Console;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Modules;

/// <summary>
/// Phase 4 module: applies and reverts tweaks defined in config/tweaks.json.
/// Creates a system restore point before applying (when reversible), keeps a
/// history of applied tweaks so they can be rolled back later, and fully
/// supports dry-run mode.
/// </summary>
public class TweaksModule : IModule
{
    public string Name => "Tweaks";
    public string Description => "Privacy, performance and telemetry tweaks from config/tweaks.json with rollback support.";

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
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Tweaks module")
                .AddChoices(
                    "Apply a tweak",
                    "Roll back a previously applied tweak",
                    "Show applied tweaks history",
                    "Back to main menu"));

            switch (choice)
            {
                case "Apply a tweak":
                    ApplyTweakMenu(host, tweaks);
                    break;
                case "Roll back a previously applied tweak":
                    RollbackTweakMenu(host, tweaks);
                    break;
                case "Show applied tweaks history":
                    ShowHistory(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ApplyTweakMenu(AppHost host, List<TweakDefinition> tweaks)
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
            .Title("Select a tweak to apply")
            .AddChoices(tweaks.Select(t => t.Id).Append("Back").ToArray()));

        if (choice == "Back") return;

        var tweak = tweaks.First(t => t.Id == choice);
        ApplyTweak(host, tweak);
    }

    private static void RollbackTweakMenu(AppHost host, List<TweakDefinition> tweaks)
    {
        var history = host.TweakHistory.Records;
        if (history.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tweaks have been applied yet.[/]");
            return;
        }

        var appliedIds = new HashSet<string>(history.Select(r => r.TweakId), StringComparer.OrdinalIgnoreCase);
        var applicable = tweaks.Where(t => appliedIds.Contains(t.Id) && t.Reversible && t.Rollback.Count > 0).ToList();
        if (applicable.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No applied tweaks are eligible for rollback.[/]");
            return;
        }

        var choice = AnsiConsole.Prompt(new SelectionPrompt<TweakDefinition>()
            .Title("Select a tweak to roll back")
            .UseConverter(t => $"{t.Name} ({t.Id})")
            .AddChoices(applicable));

        if (choice == null) return;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[bold]Roll back: {choice.Name}[/]");
        AnsiConsole.MarkupLine($"[grey]{choice.Description}[/]");

        var preview = new Table("Rollback Action", "Detail")
        {
            Border = TableBorder.Rounded
        };
        foreach (var action in choice.Rollback)
        {
            preview.AddRow(action.Type, FormatRollbackAction(action));
        }
        AnsiConsole.Write(preview);

        if (!Confirm(host, choice)) return;

        if (!host.DryRun)
        {
            var created = host.RestorePoints.Create($"WDigger rollback: {choice.Name}");
            AnsiConsole.MarkupLine(created
                ? "[green]System restore point created before rolling back.[/]"
                : "[yellow]Restore point could not be created; continuing anyway.[/]");
        }

        foreach (var action in choice.Rollback)
        {
            ExecuteRollbackAction(host, action);
        }

        host.TweakHistory.Remove(choice.Id);
        AnsiConsole.MarkupLine($"[green]Tweak '{choice.Name}' rolled back (simulated if dry-run mode is on).[/]");
    }

    private static void ShowHistory(AppHost host)
    {
        var history = host.TweakHistory.Records;
        if (history.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No tweaks have been applied yet.[/]");
            return;
        }

        var table = new Table("Tweak Id", "Name", "Applied At")
        {
            Border = TableBorder.Rounded
        };
        foreach (var record in history.OrderByDescending(r => r.AppliedAt))
        {
            table.AddRow(record.TweakId, record.TweakName, record.AppliedAt.ToString("yyyy-MM-dd HH:mm"));
        }
        AnsiConsole.Write(table);
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

        if (!Confirm(host, tweak)) return;

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

        host.TweakHistory.Record(tweak.Id, tweak.Name);
        AnsiConsole.MarkupLine($"[green]Tweak '{tweak.Name}' applied (simulated if dry-run mode is on).[/]");
    }

    private static string FormatAction(TweakAction action) => action.Type switch
    {
        "registry" => $"reg add \"{action.Hive}\\{action.Key}\" /v {action.Value} /t {MapKind(action.Kind)} /d {action.Data} /f",
        _ => action.Command
    };

    private static string FormatRollbackAction(TweakAction action) => action.Type switch
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

    private static void ExecuteRollbackAction(AppHost host, TweakAction action)
    {
        var commandLine = action.Type.Equals("registry", StringComparison.OrdinalIgnoreCase)
            ? $"reg add \"{action.Hive}\\{action.Key}\" /v {action.Value} /t {MapKind(action.Kind)} /d {action.Data} /f"
            : action.Command;

        host.Commands.Run("cmd", $"/c \"{commandLine}\"");
    }
}
