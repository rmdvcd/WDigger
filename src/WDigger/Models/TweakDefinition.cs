namespace WDigger.Models;

/// <summary>
/// A tweak definition loaded from config/tweaks.json.
/// </summary>
public sealed class TweakDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "General";
    public bool Reversible { get; set; } = true;
    public List<TweakAction> Actions { get; set; } = new();
    public List<TweakAction> Rollback { get; set; } = new();
}

/// <summary>
/// A single action of a tweak: either a native command line or a registry
/// change (reg add compatible).
/// </summary>
public sealed class TweakAction
{
    public string Type { get; set; } = "command"; // command | registry
    public string Command { get; set; } = "";     // full command line for type=command
    public string Description { get; set; } = "";

    // registry fields
    public string Hive { get; set; } = "";
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
    public string Data { get; set; } = "";
    public string Kind { get; set; } = "DWord"; // DWord, QWord, String, ExpandString, Binary
}
