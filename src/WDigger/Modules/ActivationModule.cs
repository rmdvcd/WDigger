using System.IO;
using System.Linq;
using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for Windows and Office activation status and HEU-KMS helper.
/// WARNING: HEU-KMS is a third-party tool. Use only for personal evaluation
/// in compliance with applicable laws and Microsoft licensing terms.
/// </summary>
public class ActivationModule : IModule
{
    public string Name => "Activation";
    public string Description => "Windows and Office activation status and HEU-KMS helper.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Activation module")
                .AddChoices(
                    "Check Windows activation status",
                    "Activate Windows (slmgr /ato)",
                    "Check Office activation status",
                    "Activate Office (cscript ospp.vbs)",
                    "Detect HEU-KMS installation",
                    "Back to main menu"));

            switch (choice)
            {
                case "Check Windows activation status":
                    host.Commands.Run("cscript", "//Nologo %SystemRoot%\\System32\\slmgr.vbs /xpr");
                    break;
                case "Activate Windows (slmgr /ato)":
                    AnsiConsole.MarkupLine("[yellow]Requires Administrator and a valid product key / digital license.[/]");
                    if (Confirm(host)) host.Commands.Run("cscript", "//Nologo %SystemRoot%\\System32\\slmgr.vbs /ato");
                    break;
                case "Check Office activation status":
                    CheckOfficeStatus(host);
                    break;
                case "Activate Office (cscript ospp.vbs)":
                    AnsiConsole.MarkupLine("[yellow]Requires Administrator and a valid Office license.[/]");
                    if (Confirm(host)) ActivateOffice(host);
                    break;
                case "Detect HEU-KMS installation":
                    DetectHeuKms(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void CheckOfficeStatus(AppHost host)
    {
        var officePaths = new[]
        {
            @"C:\Program Files\Microsoft Office\Office16\ospp.vbs",
            @"C:\Program Files (x86)\Microsoft Office\Office16\ospp.vbs",
            @"C:\Program Files\Microsoft Office\Office15\ospp.vbs",
            @"C:\Program Files (x86)\Microsoft Office\Office15\ospp.vbs"
        };

        var vbs = officePaths.FirstOrDefault(File.Exists);
        if (vbs is null)
        {
            AnsiConsole.MarkupLine("[yellow]Office ospp.vbs not found in common locations.[/]");
            return;
        }

        host.Commands.Run("cscript", $"//Nologo \"{vbs}\" /dstatus");
    }

    private static void ActivateOffice(AppHost host)
    {
        var vbs = new[]
        {
            @"C:\Program Files\Microsoft Office\Office16\ospp.vbs",
            @"C:\Program Files (x86)\Microsoft Office\Office16\ospp.vbs",
            @"C:\Program Files\Microsoft Office\Office15\ospp.vbs",
            @"C:\Program Files (x86)\Microsoft Office\Office15\ospp.vbs"
        }.FirstOrDefault(File.Exists);

        if (vbs is null)
        {
            AnsiConsole.MarkupLine("[yellow]Office ospp.vbs not found.[/]");
            return;
        }

        host.Commands.Run("cscript", $"//Nologo \"{vbs}\" /act");
    }

    private static void DetectHeuKms(AppHost host)
    {
        var paths = new[]
        {
            @"C:\Program Files\HEU_KMS_Activator",
            @"C:\Program Files (x86)\HEU_KMS_Activator",
            @"C:\Tools\HEU_KMS_Activator",
            @"C:\HEU_KMS_Activator"
        };

        var found = paths.FirstOrDefault(Directory.Exists);
        if (found is null)
        {
            AnsiConsole.MarkupLine("[yellow]HEU-KMS Activator not detected in common locations.[/]");
            AnsiConsole.MarkupLine("[grey]Note: HEU-KMS is a third-party activation tool. Use only for personal evaluation in compliance with applicable laws and Microsoft licensing terms.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[green]HEU-KMS detected at: {found}[/]");
            var exe = Directory.GetFiles(found, "*.exe", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (exe is not null)
            {
                AnsiConsole.MarkupLine($"Executable: {Path.GetFileName(exe)}");
            }
        }
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
