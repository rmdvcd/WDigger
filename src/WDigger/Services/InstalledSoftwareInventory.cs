using Microsoft.Win32;
using System.Runtime.Versioning;

namespace WDigger.Services;

public sealed record InstalledSoftware(string Name, string Version, string Publisher, string Scope);
public sealed record SoftwareInventoryResult(IReadOnlyList<InstalledSoftware> Entries, IReadOnlyList<string> Warnings);

/// <summary>Reads uninstall metadata only. Never runs uninstall commands or Win32_Product.</summary>
[SupportedOSPlatform("windows")]
public static class InstalledSoftwareInventory
{
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static SoftwareInventoryResult Read(CancellationToken token)
    {
        var entries = new List<InstalledSoftware>();
        var warnings = new List<string>();
        ReadView(RegistryHive.LocalMachine, RegistryView.Registry64, UninstallKey, "Equipo (64 bits)", entries, warnings, token);
        ReadView(RegistryHive.LocalMachine, RegistryView.Registry32, UninstallKey, "Equipo (32 bits)", entries, warnings, token);
        ReadView(RegistryHive.CurrentUser, RegistryView.Registry64, UninstallKey, "Usuario actual", entries, warnings, token);
        ReadView(RegistryHive.CurrentUser, RegistryView.Registry32, UninstallKey, "Usuario actual (32 bits)", entries, warnings, token);
        return new SoftwareInventoryResult(entries.Distinct().OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), warnings);
    }

    internal static void ReadView(RegistryHive hive, RegistryView view, string path, string scope,
        List<InstalledSoftware> entries, List<string> warnings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = root.OpenSubKey(path);
            if (uninstall is null)
            {
                warnings.Add($"No se encontró el inventario de {scope}.");
                return;
            }
            foreach (var name in uninstall.GetSubKeyNames())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var key = uninstall.OpenSubKey(name);
                    if (key is null) { warnings.Add($"No se pudo leer una entrada de {scope}."); continue; }
                    var displayName = key.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    entries.Add(new InstalledSoftware(displayName.Trim(), key.GetValue("DisplayVersion") as string ?? "—",
                        key.GetValue("Publisher") as string ?? "—", scope));
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
                {
                    warnings.Add($"No se pudo leer una entrada de {scope}.");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            warnings.Add($"No se pudo leer el inventario de {scope}.");
        }
    }
}
