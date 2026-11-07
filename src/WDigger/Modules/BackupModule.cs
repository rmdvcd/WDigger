using System.IO;
using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for Acronis True Image (.tibx) backup listing and restore.
/// </summary>
public class BackupModule : IModule
{
    public string Name => "Backup (.tibx)";
    public string Description => "Acronis True Image (.tibx) backup listing and restore.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Backup module")
                .AddChoices(
                    "List .tibx backups in a folder",
                    "Show backup details",
                    "Restore from .tibx (Acronis)",
                    "Back to main menu"));

            switch (choice)
            {
                case "List .tibx backups in a folder":
                    ListTibx(host);
                    break;
                case "Show backup details":
                    ShowDetails(host);
                    break;
                case "Restore from .tibx (Acronis)":
                    RestoreTibx(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ListTibx(AppHost host)
    {
        var folder = AnsiConsole.Ask<string>("Folder to scan for .tibx files", @"C:\Backups");
        if (string.IsNullOrWhiteSpace(folder)) return;
        if (!Directory.Exists(folder))
        {
            AnsiConsole.MarkupLine("[yellow]Folder does not exist.[/]");
            return;
        }

        var files = Directory.GetFiles(folder, "*.tibx", SearchOption.AllDirectories);
        if (files.Length == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No .tibx files found.[/]");
            return;
        }

        var table = new Table("File", "Size", "Modified")
        {
            Border = TableBorder.Rounded
        };
        foreach (var file in files.OrderByDescending(f => File.GetLastWriteTime(f)))
        {
            var info = new FileInfo(file);
            table.AddRow(
                info.Name,
                $"{info.Length / 1024.0 / 1024.0 / 1024.0:F2} GB",
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            );
        }
        AnsiConsole.Write(table);
    }

    private static void ShowDetails(AppHost host)
    {
        var file = AnsiConsole.Ask<string>("Path to .tibx file");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        var info = new FileInfo(file);
        AnsiConsole.MarkupLine($"[bold]{info.Name}[/]");
        AnsiConsole.MarkupLine($"Size: {info.Length / 1024.0 / 1024.0 / 1024.0:F2} GB");
        AnsiConsole.MarkupLine($"Modified: {info.LastWriteTime}");
        AnsiConsole.MarkupLine("[grey]Full backup contents require Acronis True Image installed.[/]");
    }

    private static void RestoreTibx(AppHost host)
    {
        AnsiConsole.MarkupLine("[red]Warning:[/] restoring a backup overwrites data on the target disk. Proceed with extreme care.");
        var file = AnsiConsole.Ask<string>("Path to .tibx backup file");
        if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return;
        var target = AnsiConsole.Ask<string>("Target drive letter (e.g. C)");
        if (string.IsNullOrWhiteSpace(target)) return;
        if (!Confirm(host)) return;

        var acronis = @"C:\Program Files (x86)\Acronis\BackupAndRecovery\deploycmd.exe";
        if (!File.Exists(acronis))
        {
            acronis = @"C:\Program Files\Acronis\BackupAndRecovery\deploycmd.exe";
        }

        if (File.Exists(acronis))
        {
            host.Commands.Run(acronis, $"restore --backup \"{file}\" --target {target}:");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Acronis command-line tool not found. Install Acronis True Image to enable restore.[/]");
            host.Commands.Run("cmd", $"/c echo .tibx restore would target {target}: from {file}");
        }
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
