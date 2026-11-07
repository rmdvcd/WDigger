namespace WDigger.Models;

/// <summary>
/// A single sensor reading coming from LibreHardwareMonitor.
/// </summary>
public sealed class SensorReading
{
    public string Hardware { get; init; } = "";
    public string HardwareType { get; init; } = "";
    public string SensorName { get; init; } = "";
    public string SensorType { get; init; } = "";
    public string? Value { get; init; }
    public string Unit { get; init; } = "";

    public string DisplayValue => Value is null ? "n/a" : $"{Value} {Unit}".TrimEnd();
}

/// <summary>
/// A single inventory item coming from WMI.
/// </summary>
public sealed class ComponentInfo
{
    public string Category { get; init; } = "";
    public string Item { get; init; } = "";
    public string Value { get; init; } = "";
}
