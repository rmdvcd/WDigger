using System.Text;

namespace WDigger.Services;

/// <summary>Plain-text inventory; registry strings cannot inject new report lines.</summary>
public static class SoftwareInventoryReport
{
    public static string Create(SoftwareInventoryResult inventory)
    {
        var text = new StringBuilder();
        text.AppendLine("WDigger — Inventario de software registrado");
        text.AppendLine("Datos potencialmente sensibles: revisa antes de compartir.");
        text.AppendLine("Solo entradas del registro; puede omitir Appx y aplicaciones portables. El usuario es el de la sesión elevada.");
        text.AppendLine($"Entradas: {inventory.Entries.Count}; advertencias: {inventory.Warnings.Count}.");
        foreach (var warning in inventory.Warnings.Distinct()) text.AppendLine($"Advertencia: {Clean(warning)}");
        text.AppendLine();
        foreach (var entry in inventory.Entries)
        {
            text.AppendLine($"Nombre: {Clean(entry.Name)}");
            text.AppendLine($"  Versión: {Clean(entry.Version)}");
            text.AppendLine($"  Editor: {Clean(entry.Publisher)}");
            text.AppendLine($"  Ámbito: {Clean(entry.Scope)}");
            text.AppendLine();
        }
        return text.ToString();
    }

    private static string Clean(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
