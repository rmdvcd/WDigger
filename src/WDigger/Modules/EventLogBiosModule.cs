using System.Management;
using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for Windows Event Viewer and BIOS/UEFI information.
/// </summary>
public class EventLogBiosModule : IModule
{
    public string Name => "Events & BIOS";
    public string Description => "Windows Event Viewer and BIOS/UEFI information.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Events & BIOS module")
                .AddChoices(
                    "List event logs",
                    "Query event log (last 20 entries)",
                    "BIOS information",
                    "Motherboard information",
                    "UEFI / Firmware information",
                    "Back to main menu"));

            switch (choice)
            {
                case "List event logs":
                    host.Commands.Run("wevtutil", "el");
                    break;
                case "Query event log (last 20 entries)":
                    QueryEventLog(host);
                    break;
                case "BIOS information":
                    ShowWmi(host, "SELECT Name, Version, ReleaseDate, Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", "BIOS");
                    break;
                case "Motherboard information":
                    ShowWmi(host, "SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard", "Motherboard");
                    break;
                case "UEFI / Firmware information":
                    ShowWmi(host, "SELECT Name, Manufacturer, Version, ReleaseDate FROM Win32_BIOS", "Firmware");
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void QueryEventLog(AppHost host)
    {
        var log = AnsiConsole.Ask<string>("Event log name (e.g. System, Application, Security)");
        if (string.IsNullOrWhiteSpace(log)) return;
        host.Commands.Run("wevtutil", $"qe \"{log}\" /c:20 /f:text /rd:true");
    }

    private static void ShowWmi(AppHost host, string wql, string title)
    {
        var table = new Table("Property", "Value")
        {
            Border = TableBorder.Rounded,
            Title = new TableTitle(title)
        };

        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject obj in searcher.Get())
            {
                foreach (PropertyData prop in obj.Properties)
                {
                    table.AddRow(prop.Name, prop.Value?.ToString() ?? "n/a");
                }
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]WMI query failed: {ex.Message}[/]");
            return;
        }

        if (table.Rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No data returned.[/]");
        }
        else
        {
            AnsiConsole.Write(table);
        }
    }
}
