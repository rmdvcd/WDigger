using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Registry of navigation tag → module + glyph. Keeps the shell decoupled
/// from the concrete module list.
/// </summary>
public static class ModuleCatalog
{
    public sealed record Entry(string Tag, IModule Module, string Glyph);

    public static IReadOnlyList<Entry> Create() => new[]
    {
        new Entry("hardware", new HardwareModule(), "\uE950"),
        new Entry("repair", new RepairModule(), "\uE90F"),
        new Entry("software", new SoftwareModule(), "\uE71D"),
        new Entry("tweaks", new TweaksModule(), "\uE72E"),
        new Entry("drivers", new DriversModule(), "\uE896"),
        new Entry("network", new NetworkModule(), "\uE701"),
        new Entry("advnetwork", new AdvancedNetworkModule(), "\uE774"),
        new Entry("devices", new DevicesModule(), "\uE771"),
        new Entry("backup", new BackupModule(), "\uE74E"),
        new Entry("activation", new ActivationModule(), "\uE8D7"),
        new Entry("recovery", new RecoveryModule(), "\uE81C"),
        new Entry("eventlog", new EventLogBiosModule(), "\uE8A5"),
        new Entry("disk", new DiskModule(), "\uE76C")
    };
}
