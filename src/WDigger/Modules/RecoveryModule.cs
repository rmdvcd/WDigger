using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for deleted file recovery using Windows File Recovery (winfr).
/// </summary>
public class RecoveryModule : IModule
{
    public string Name => "File Recovery";
    public string Description => "Deleted file recovery using Windows File Recovery (winfr).";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("File Recovery module")
                .AddChoices(
                    "Check winfr availability",
                    "List drives",
                    "Recover deleted files (winfr)",
                    "Back to main menu"));

            switch (choice)
            {
                case "Check winfr availability":
                    host.Commands.Run("winfr", "/?");
                    break;
                case "List drives":
                    host.Commands.Run("powershell", "-Command \"Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID, DriveType, Size, FreeSpace | Format-Table -AutoSize\"");
                    break;
                case "Recover deleted files (winfr)":
                    RecoverFiles(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void RecoverFiles(AppHost host)
    {
        AnsiConsole.MarkupLine("[red]Warning:[/] file recovery requires Windows File Recovery (winfr) from Microsoft Store. Run as Administrator.");
        var source = AnsiConsole.Ask<string>("Source drive (e.g. C:)");
        if (string.IsNullOrWhiteSpace(source)) return;
        var destination = AnsiConsole.Ask<string>("Destination drive (must differ from source, e.g. D:)");
        if (string.IsNullOrWhiteSpace(destination)) return;
        var mode = AnsiConsole.Prompt(new SelectionPrompt<string>()
            .Title("Recovery mode")
            .AddChoices("Default", "Segment", "Signature"));
        var filter = AnsiConsole.Ask<string>("File filter (e.g. *.pdf, *.jpg, or leave empty for all)");
        if (!Confirm(host)) return;

        var args = $"\"{source}\" \"{destination}\" /{mode.ToLower()}";
        if (!string.IsNullOrWhiteSpace(filter))
        {
            args += $" /{filter}";
        }

        host.Commands.Run("winfr", args);
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
