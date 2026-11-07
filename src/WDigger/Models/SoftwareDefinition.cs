namespace WDigger.Models;

/// <summary>
/// A software package definition loaded from config/software.json.
/// </summary>
public sealed class SoftwareDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "winget"; // winget | chocolatey
    public string? Arguments { get; set; }
    public string Category { get; set; } = "General";
    public bool Silent { get; set; } = true;
}
