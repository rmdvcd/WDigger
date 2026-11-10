using System.Globalization;
using System.Runtime.Versioning;
using LibreHardwareMonitor.Hardware;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Services;

/// <summary>
/// Reads real-time hardware sensors through LibreHardwareMonitorLib
/// (temperatures, loads, clocks, voltages, fan speeds, ...).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SensorService
{
    private readonly AppLogger _logger;

    public SensorService(AppLogger logger) => _logger = logger;

    public IReadOnlyList<SensorReading> ReadAll()
    {
        var results = new List<SensorReading>();
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsNetworkEnabled = true,
                IsStorageEnabled = true,
                IsPsuEnabled = true,
                IsBatteryEnabled = true
            };

            computer.Open();
            try
            {
                foreach (var hardware in computer.Hardware)
                {
                    try
                    {
                        Collect(hardware, results);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"Failed to read hardware '{hardware.Name}': {ex.Message}");
                    }
                }
            }
            finally
            {
                computer.Close();
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"LibreHardwareMonitor initialization failed: {ex.Message}");
            results.Add(new SensorReading
            {
                Hardware = "LibreHardwareMonitor",
                HardwareType = "Error",
                SensorName = "Initialization",
                SensorType = "Error",
                Value = null,
                Unit = ex.Message
            });
        }

        return results;
    }

    private static void Collect(IHardware hardware, List<SensorReading> results)
    {
        // Use an explicit worklist and identity tracking so a cyclic or unusually
        // deep hardware tree cannot overflow the process stack.
        var pending = new Stack<IHardware>();
        var visited = new HashSet<IHardware>(ReferenceEqualityComparer.Instance);
        pending.Push(hardware);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            // Some controllers throw on update; still try their sensors.
            try
            {
                current.Update();
            }
            catch
            {
                // Ignore update failures for individual hardware nodes.
            }

            foreach (var sensor in current.Sensors)
            {
                results.Add(new SensorReading
                {
                    Hardware = current.Name,
                    HardwareType = current.HardwareType.ToString(),
                    SensorName = sensor.Name,
                    SensorType = sensor.SensorType.ToString(),
                    Value = sensor.Value.HasValue
                        ? sensor.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                        : null,
                    Unit = UnitFor(sensor.SensorType)
                });
            }

            // Reverse the push order to keep the previous depth-first ordering.
            foreach (var sub in current.SubHardware.Reverse())
            {
                pending.Push(sub);
            }
        }
    }

    private static string UnitFor(SensorType type) => type switch
    {
        SensorType.Temperature => "°C",
        SensorType.Clock => "MHz",
        SensorType.Load => "%",
        SensorType.Fan => "RPM",
        SensorType.Voltage => "V",
        SensorType.Power => "W",
        SensorType.Data => "B",
        SensorType.Energy => "mWh",
        SensorType.Factor => "",
        SensorType.Throughput => "B/s",
        _ => ""
    };
}
