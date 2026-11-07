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
                IsTpmEnabled = true,
                IsBatteryEnabled = true,
                IsFanControllerEnabled = true,
                IsHeatControllerEnabled = true
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
        // Update() pulls fresh values from the underlying hardware. Some items
        // (e.g. certain SM controllers) throw on update; skip them silently.
        try
        {
            hardware.Update();
        }
        catch
        {
            // Ignore update failures for individual hardware nodes.
        }

        foreach (var sensor in hardware.Sensors)
        {
            results.Add(new SensorReading
            {
                Hardware = hardware.Name,
                HardwareType = hardware.HardwareType.ToString(),
                SensorName = sensor.Name,
                SensorType = sensor.SensorType.ToString(),
                Value = sensor.Value.HasValue
                    ? sensor.Value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                    : null,
                Unit = UnitFor(sensor.SensorType)
            });
        }

        foreach (var sub in hardware.SubHardware)
        {
            Collect(sub, results);
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
        SensorType.Capacity => "bytes",
        SensorType.Data => "bytes",
        SensorType.Energy => "Wh",
        SensorType.SmallData => "",
        SensorType.Factor => "",
        SensorType.Throughput => "",
        _ => ""
    };
}
