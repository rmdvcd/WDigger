using System.Collections.Generic;
using System.Management;
using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for printers and PnP device management.
/// </summary>
public class DevicesModule : IModule
{
    public string Name => "Devices";
    public string Description => "Printers, PnP devices and device management.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Devices module")
                .AddChoices(
                    "List installed printers",
                    "Add network printer",
                    "Remove a printer",
                    "List PnP devices",
                    "Enable / Disable a device",
                    "Back to main menu"));

            switch (choice)
            {
                case "List installed printers":
                    ListPrinters(host);
                    break;
                case "Add network printer":
                    AddNetworkPrinter(host);
                    break;
                case "Remove a printer":
                    RemovePrinter(host);
                    break;
                case "List PnP devices":
                    ListPnpDevices(host);
                    break;
                case "Enable / Disable a device":
                    EnableDisableDevice(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ListPrinters(AppHost host)
    {
        var table = new Table("Name", "Status", "Port", "Shared", "Default")
        {
            Border = TableBorder.Rounded
        };

        foreach (var row in WmiQuery(host, "SELECT Name, Status, PortName, Shared, Default FROM Win32_Printer"))
        {
            table.AddRow(
                Get(row, "Name") ?? "n/a",
                Get(row, "Status") ?? "n/a",
                Get(row, "PortName") ?? "n/a",
                Get(row, "Shared") ?? "False",
                Get(row, "Default") ?? "False"
            );
        }

        if (table.Rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No printers found.[/]");
        }
        else
        {
            AnsiConsole.Write(table);
        }
    }

    private static void AddNetworkPrinter(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]Adding a network printer requires Administrator rights.[/]");
        var ip = AnsiConsole.Ask<string>("Printer IP address or hostname");
        if (string.IsNullOrWhiteSpace(ip)) return;
        var name = AnsiConsole.Ask<string>("Printer name (display name)", ip);
        if (!Confirm(host)) return;

        host.Commands.Run("rundll32.exe", $"printui.dll,PrintUIEntry /if /b \"{name}\" /r \"IP_{ip}\" /m \"Generic Printer\" /z");
    }

    private static void RemovePrinter(AppHost host)
    {
        var name = AnsiConsole.Ask<string>("Printer name to remove");
        if (string.IsNullOrWhiteSpace(name)) return;
        if (!Confirm(host)) return;
        host.Commands.Run("rundll32.exe", $"printui.dll,PrintUIEntry /dl /n \"{name}\"");
    }

    private static void ListPnpDevices(AppHost host)
    {
        var table = new Table("Name", "Status", "Class", "Device ID")
        {
            Border = TableBorder.Rounded
        };

        foreach (var row in WmiQuery(host, "SELECT Name, Status, ClassGuid, PNPDeviceID FROM Win32_PnPEntity WHERE Name IS NOT NULL"))
        {
            table.AddRow(
                Get(row, "Name") ?? "n/a",
                Get(row, "Status") ?? "n/a",
                Get(row, "ClassGuid") ?? "n/a",
                Get(row, "PNPDeviceID") ?? "n/a"
            );
        }

        if (table.Rows.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No PnP devices found.[/]");
        }
        else
        {
            AnsiConsole.Write(table);
        }
    }

    private static void EnableDisableDevice(AppHost host)
    {
        var instanceId = AnsiConsole.Ask<string>("Device Instance ID (PNPDeviceID)");
        if (string.IsNullOrWhiteSpace(instanceId)) return;
        var enable = AnsiConsole.Confirm("Enable device? (No = Disable)");
        if (!Confirm(host)) return;

        var action = enable ? "Enable-PnpDevice" : "Disable-PnpDevice";
        host.Commands.Run("powershell", $"-Command \"{action} -InstanceId '{instanceId}' -Confirm:$false\"");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }

    private static IEnumerable<Dictionary<string, object?>> WmiQuery(AppHost host, string wql)
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (ManagementObject obj in searcher.Get())
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (PropertyData prop in obj.Properties)
                {
                    row[prop.Name] = prop.Value;
                }
                results.Add(row);
            }
        }
        catch (Exception ex)
        {
            host.Logger.Warn($"WMI query failed: {ex.Message}");
        }
        return results;
    }

    private static string? Get(Dictionary<string, object?> row, string name)
    {
        return row.TryGetValue(name, out var value) && value is not null ? value.ToString() : null;
    }
}
