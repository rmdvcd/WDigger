using System.Globalization;
using System.Linq;
using System.Threading;
using Spectre.Console;
using WDigger.Core;
using WDigger.Models;
using WDigger.Services;

namespace WDigger.Modules;

/// <summary>
/// Phase 2 module: component inventory (WMI) and real-time sensor monitoring
/// (LibreHardwareMonitorLib).
/// </summary>
public class HardwareModule : IModule
{
    public string Name => "Hardware";
    public string Description => "Component inventory (WMI) and real-time sensor monitoring (LibreHardwareMonitor).";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Hardware module")
                .AddChoices(
                    "View component inventory (WMI)",
                    "Real-time sensor monitor",
                    "Disk status & battery health",
                    "Back to main menu"));

            switch (choice)
            {
                case "View component inventory (WMI)":
                    ShowInventory(host);
                    break;
                case "Real-time sensor monitor":
                    ShowSensorsLive(host);
                    break;
                case "Disk status & battery health":
                    ShowSmartAndBattery(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void ShowInventory(AppHost host)
    {
        var inventory = host.Inventory.GetInventory();
        var table = new Table("Component", "Detail", "Value")
        {
            Border = TableBorder.Rounded
        };
        foreach (var item in inventory)
        {
            table.AddRow(item.Category, item.Item, string.IsNullOrEmpty(item.Value) ? "n/a" : item.Value);
        }

        AnsiConsole.Write(table);
    }

    private void ShowSensorsLive(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]Note:[/] sensor access may require Administrator privileges. " +
            "Values may appear as n/a without elevation.");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[grey]Press any key to stop monitoring.[/]\n");

        using var live = AnsiConsole.Live(new Panel("Initializing LibreHardwareMonitor..."));
        while (!Console.KeyAvailable)
        {
            var readings = host.Sensors.ReadAll();
            live.Refresh(BuildSensorPanel(readings));
            Thread.Sleep(1000);
        }

        if (Console.KeyAvailable)
        {
            _ = Console.ReadKey(true);
        }
    }

    private static Panel BuildSensorPanel(IReadOnlyList<SensorReading> readings)
    {
        var table = new Table { Border = TableBorder.Rounded };
        table.AddColumn("Hardware");
        table.AddColumn("Sensor");
        table.AddColumn("Type");
        table.AddColumn("Value");
        table.AddColumn("Unit");

        var shown = readings
            .Where(r => r.Value is not null)
            .OrderBy(r => r.HardwareType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Hardware, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.SensorName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var r in shown)
        {
            table.AddRow(
                r.Hardware,
                r.SensorName,
                r.SensorType,
                new Text(r.Value ?? "n/a", StyleFor(r)),
                r.Unit);
        }

        return new Panel(table)
        {
            Border = BoxBorder.Rounded,
            Header = new PanelHeader($"Real-time sensors ({shown.Count} readings)")
        };
    }

    private static Style StyleFor(SensorReading r)
    {
        if (!float.TryParse(r.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return Style.Plain;
        }

        return r.SensorType switch
        {
            "Temperature" => value > 85 ? Style.Parse("red") : value > 70 ? Style.Parse("yellow") : Style.Parse("green"),
            "Load" => value > 90 ? Style.Parse("red") : value > 70 ? Style.Parse("yellow") : Style.Parse("green"),
            "Fan" => value == 0 ? Style.Parse("red") : Style.Parse("cyan"),
            "Clock" => Style.Parse("grey"),
            "Voltage" => Math.Abs(value - 1.0f) > 0.1f ? Style.Parse("yellow") : Style.Parse("green"),
            _ => Style.Plain
        };
    }

    private void ShowSmartAndBattery(AppHost host)
    {
        var table = new Table("Component", "Detail", "Value")
        {
            Border = TableBorder.Rounded
        };
        foreach (var item in host.Inventory.GetSmartAndBattery())
        {
            table.AddRow(item.Category, item.Item, string.IsNullOrEmpty(item.Value) ? "n/a" : item.Value);
        }

        AnsiConsole.Write(table);
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[grey]SMART temperatures, health and per-disk sensor values are shown in the real-time sensor monitor above.[/]\n");
    }
}
