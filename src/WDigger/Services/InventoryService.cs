using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.Services;

/// <summary>
/// Reads the static component inventory through WMI: CPU, GPU, RAM, disks,
/// motherboard, battery and network adapters.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class InventoryService
{
    private readonly AppLogger _logger;

    public InventoryService(AppLogger logger) => _logger = logger;

    /// <summary>
    /// Full component inventory as a flat list of category / item / value rows.
    /// </summary>
    public List<ComponentInfo> GetInventory()
    {
        var inventory = new List<ComponentInfo>();

        inventory.AddRange(OperatingSystem());
        inventory.AddRange(ComputerSystem());
        inventory.AddRange(Processor());
        inventory.AddRange(VideoController());
        inventory.AddRange(PhysicalMemory());
        inventory.AddRange(DiskDrives());
        inventory.AddRange(BaseBoard());
        inventory.AddRange(Battery());
        inventory.AddRange(NetworkAdapters());

        return inventory;
    }

    /// <summary>
    /// Disk status (via WMI) plus battery state, shown in the "disk status &
    /// battery health" view. SMART temperatures/health readings are surfaced in
    /// the real-time sensor monitor via LibreHardwareMonitor.
    /// </summary>
    public List<ComponentInfo> GetSmartAndBattery()
    {
        var list = new List<ComponentInfo>();

        foreach (var row in Query("SELECT Model, Status, MediaType, InterfaceType FROM Win32_DiskDrive"))
        {
            list.Add(new ComponentInfo
            {
                Category = "Disk",
                Item = Get(row, "Model") ?? "disk",
                Value = $"Status: {Get(row, "Status") ?? "n/a"}, Media: {Get(row, "MediaType") ?? "n/a"}, Interface: {Get(row, "InterfaceType") ?? "n/a"}"
            });
        }

        foreach (var row in Query("SELECT EstimatedChargeRemaining, BatteryStatus FROM Win32_Battery"))
        {
            list.Add(new ComponentInfo
            {
                Category = "Battery",
                Item = "Charge remaining",
                Value = $"{Get(row, "EstimatedChargeRemaining") ?? "n/a"}%"
            });
            list.Add(new ComponentInfo
            {
                Category = "Battery",
                Item = "Status",
                Value = BatteryStatusName(Get(row, "BatteryStatus"))
            });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Info", Item = "SMART / battery", Value = "No data available via WMI." });
        }

        return list;
    }

    private static IEnumerable<Dictionary<string, object?>> Query(string wql)
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
        catch
        {
            // WMI may be unavailable or access denied (not running as admin).
        }
        return results;
    }

    private static string? Get(Dictionary<string, object?> row, params string[] names)
    {
        foreach (var name in names)
        {
            if (row.TryGetValue(name, out var value) && value is not null)
            {
                return value.ToString();
            }
        }
        return null;
    }

    private static string ToGigabytes(object? value)
    {
        if (value is null || !long.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var bytes))
        {
            return "n/a";
        }

        return $"{(bytes / 1024.0 / 1024.0 / 1024.0):F2} GB";
    }

    private static string ToMegabits(object? value)
    {
        if (value is null || !long.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var bitsPerSecond))
        {
            return "n/a";
        }

        return $"{(bitsPerSecond / 1_000_000.0):F0} Mbps";
    }

    private static string BatteryStatusName(string? status) => status switch
    {
        "1" => "Discharging",
        "2" => "Charging",
        _ => status ?? "n/a"
    };

    private List<ComponentInfo> OperatingSystem()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Caption, Version, BuildNumber, OSArchitecture FROM Win32_OperatingSystem"))
        {
            list.Add(new ComponentInfo { Category = "Operating system", Item = "Name", Value = Get(row, "Caption") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Operating system", Item = "Version", Value = Get(row, "Version") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Operating system", Item = "Build", Value = Get(row, "BuildNumber") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Operating system", Item = "Architecture", Value = Get(row, "OSArchitecture") ?? "n/a" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Operating system", Item = "Name", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> ComputerSystem()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Manufacturer, Model, TotalPhysicalMemory, NumberOfProcessors, SystemType FROM Win32_ComputerSystem"))
        {
            list.Add(new ComponentInfo { Category = "Computer system", Item = "Manufacturer", Value = Get(row, "Manufacturer") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Computer system", Item = "Model", Value = Get(row, "Model") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Computer system", Item = "Total memory", Value = ToGigabytes(Get(row, "TotalPhysicalMemory")) });
            list.Add(new ComponentInfo { Category = "Computer system", Item = "Physical processors", Value = Get(row, "NumberOfProcessors") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Computer system", Item = "System type", Value = Get(row, "SystemType") ?? "n/a" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Computer system", Item = "Model", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> Processor()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor"))
        {
            list.Add(new ComponentInfo { Category = "CPU", Item = "Name", Value = Get(row, "Name") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "CPU", Item = "Cores", Value = Get(row, "NumberOfCores") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "CPU", Item = "Logical processors", Value = Get(row, "NumberOfLogicalProcessors") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "CPU", Item = "Max clock", Value = Get(row, "MaxClockSpeed") is { } clock ? $"{clock} MHz" : "n/a" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "CPU", Item = "Name", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> VideoController()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Name, VideoProcessor, AdapterRAM, DriverVersion FROM Win32_VideoController"))
        {
            list.Add(new ComponentInfo { Category = "GPU", Item = "Name", Value = Get(row, "Name") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "GPU", Item = "Video processor", Value = Get(row, "VideoProcessor") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "GPU", Item = "Adapter RAM", Value = ToGigabytes(Get(row, "AdapterRAM")) });
            list.Add(new ComponentInfo { Category = "GPU", Item = "Driver version", Value = Get(row, "DriverVersion") ?? "n/a" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "GPU", Item = "Name", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> PhysicalMemory()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT BankLabel, Capacity, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory"))
        {
            var bank = Get(row, "BankLabel") ?? "module";
            list.Add(new ComponentInfo { Category = "RAM", Item = bank, Value = $"{ToGigabytes(Get(row, "Capacity"))}, {Get(row, "Speed") ?? "n/a"} MHz, {Get(row, "Manufacturer") ?? "n/a"}" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "RAM", Item = "Modules", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> DiskDrives()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Model, Size, InterfaceType, MediaType, Status FROM Win32_DiskDrive"))
        {
            list.Add(new ComponentInfo
            {
                Category = "Disk",
                Item = Get(row, "Model") ?? "disk",
                Value = $"Size: {ToGigabytes(Get(row, "Size"))}, Media: {Get(row, "MediaType") ?? "n/a"}, Interface: {Get(row, "InterfaceType") ?? "n/a"}, Status: {Get(row, "Status") ?? "n/a"}"
            });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Disk", Item = "Drives", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> BaseBoard()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard"))
        {
            list.Add(new ComponentInfo { Category = "Motherboard", Item = "Manufacturer", Value = Get(row, "Manufacturer") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Motherboard", Item = "Product", Value = Get(row, "Product") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Motherboard", Item = "Serial", Value = Get(row, "SerialNumber") ?? "n/a" });
            list.Add(new ComponentInfo { Category = "Motherboard", Item = "Version", Value = Get(row, "Version") ?? "n/a" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Motherboard", Item = "Product", Value = "n/a" });
        }
        return list;
    }

    private List<ComponentInfo> Battery()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Name, EstimatedChargeRemaining, BatteryStatus, Chemistry FROM Win32_Battery"))
        {
            list.Add(new ComponentInfo { Category = "Battery", Item = Get(row, "Name") ?? "Battery", Value = $"{Get(row, "EstimatedChargeRemaining") ?? "n/a"}% - {BatteryStatusName(Get(row, "BatteryStatus"))}" });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Battery", Item = "Present", Value = "No battery detected (desktop system)" });
        }
        return list;
    }

    private List<ComponentInfo> NetworkAdapters()
    {
        var list = new List<ComponentInfo>();
        foreach (var row in Query("SELECT Name, MACAddress, Speed, NetConnectionStatus, AdapterType FROM Win32_NetworkAdapter WHERE NetEnabled = TRUE"))
        {
            list.Add(new ComponentInfo
            {
                Category = "Network",
                Item = Get(row, "Name") ?? "adapter",
                Value = $"MAC: {Get(row, "MACAddress") ?? "n/a"}, Speed: {ToMegabits(Get(row, "Speed"))}, Status: {ConnectionStatusName(Get(row, "NetConnectionStatus"))}"
            });
        }

        if (list.Count == 0)
        {
            list.Add(new ComponentInfo { Category = "Network", Item = "Adapters", Value = "n/a" });
        }
        return list;
    }

    private static string ConnectionStatusName(string? status) => status switch
    {
        "0" => "Disconnected",
        "1" => "Connected",
        "2" => "Authenticating",
        "7" => "Disabled",
        _ => status ?? "n/a"
    };
}
