using System.Text.Json;
using WDigger.Core;
using WDigger.UI;
using WDigger.Services;

namespace WDigger.Modules;

public sealed class SoftwareModule : IModule
{
    public string Name => "Software";
    public string Description => "Inventario de aplicaciones instaladas y retirada limitada de Appx del usuario actual.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Comprobar gestores de paquetes",
            "Busca indicios de WinGet y Chocolatey en rutas habituales, sin ejecutarlos ni instalarlos.",
            ShowPackageManagerCandidatesAsync),
        new ModuleAction(
            "Inventario de software instalado",
            "Lista programas registrados para el equipo y el usuario elevado actual, sin ejecutar instaladores ni desinstaladores.",
            ShowInstalledSoftwareAsync),
        new ModuleAction(
            "Exportar inventario de software",
            "Guarda un informe de las entradas registradas; revísalo antes de compartirlo.",
            ExportInstalledSoftwareAsync, writesFile: true),
        new ModuleAction(
            "Ver paquetes Appx removibles",
            "Lista los paquetes removibles del usuario elevado actual; no incluye aplicaciones de otros usuarios ni paquetes provisionados.",
            ShowAppxAsync),
        new ModuleAction(
            "Quitar un paquete Appx",
            "Selecciona un único paquete removible del usuario actual; no elimina paquetes provisionados ni afecta a otros usuarios.",
            RemoveAppxAsync, changesSystem: true,
            confirmation: "Desinstalar una aplicación puede afectar a datos y funciones; no existe rollback automático.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Destructive,
            commandTimeout: TimeSpan.FromMinutes(15))
    };

    private static Task ShowPackageManagerCandidatesAsync(ModuleExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var candidates = new[]
        {
            (Name: "WinGet", Base: local, Relative: Path.Combine("Microsoft", "WindowsApps", "winget.exe")),
            (Name: "Chocolatey", Base: common, Relative: Path.Combine("chocolatey", "bin", "choco.exe"))
        };
        var rows = candidates.Select(candidate =>
        {
            var path = string.IsNullOrWhiteSpace(candidate.Base) ? null : Path.Combine(candidate.Base, candidate.Relative);
            var found = path is not null && File.Exists(path);
            return new[] { candidate.Name, found ? "Candidato en ruta habitual; sin verificar" : "No detectado en ruta habitual", path ?? "Ruta no disponible" };
        }).ToList();
        context.ShowTable("Gestores de paquetes (solo detección)", new[] { "Gestor", "Resultado", "Ruta examinada" }, rows);
        context.WriteLine("No se ha ejecutado ningún gestor ni verificado firma, versión o identidad. Un alias puede existir sin funcionar; también puede haber instalaciones en otras rutas. La instalación y actualización siguen deshabilitadas.", OutputKind.Warning);
        return Task.CompletedTask;
    }

    private static async Task ShowInstalledSoftwareAsync(ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Consultando software instalado...");
        var result = await Task.Run(() => InstalledSoftwareInventory.Read(token), token);
        token.ThrowIfCancellationRequested();
        context.ShowTable("Software registrado", new[] { "Nombre", "Versión", "Editor", "Ámbito" },
            result.Entries.Select(item => new[] { item.Name, item.Version, item.Publisher, item.Scope }));
        context.WriteLine($"{result.Entries.Count} entradas registradas. La lista puede no incluir aplicaciones portables o Appx.", OutputKind.Info);
        foreach (var warning in result.Warnings.Distinct()) context.WriteLine(warning, OutputKind.Warning);
        if (result.Warnings.Count > 0)
            context.SetOutcome(OperationOutcome.Partial("El inventario está incompleto; revisa las advertencias."));
    }

    private static async Task ExportInstalledSoftwareAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var path = await context.PickSaveFileAsync("WDigger-software", ".txt");
        if (path is null) return;
        context.SetStatus("Consultando software instalado...");
        var result = await Task.Run(() => InstalledSoftwareInventory.Read(token), token);
        token.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(path, SoftwareInventoryReport.Create(result), token);
        context.WriteLine("Informe guardado. Puede contener información sensible sobre el equipo; revísalo antes de compartirlo.", OutputKind.Warning);
        if (result.Warnings.Count > 0)
            context.SetOutcome(OperationOutcome.Partial("El informe se guardó con advertencias; el inventario puede estar incompleto."));
    }

    public sealed class AppxEntry
    {
        public string Name { get; set; } = "";
        public string PackageFullName { get; set; } = "";
    }

    private static async Task<List<AppxEntry>> GetAppxAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var result = await context.RunReadOnlyPowerShellAsync(PowerShellScriptId.AppxInventory, Array.Empty<string>(), token);
        List<AppxEntry>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<List<AppxEntry>>(result.StandardOutput.Trim(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("No se pudo interpretar el inventario de Appx; no se retiró ningún paquete.", exception);
        }
        return (parsed ?? new List<AppxEntry>())
            .Where(package => AppxPackagePolicy.IsValidFullName(package.PackageFullName))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task ShowAppxAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var entries = await GetAppxAsync(context, token);
        context.ShowTable("Appx removibles del usuario actual", new[] { "Nombre", "Nombre completo del paquete" },
            entries.Select(entry => new[] { entry.Name, entry.PackageFullName }));
        context.WriteLine($"{entries.Count} paquetes removibles del usuario elevado actual. No incluye otros usuarios ni paquetes provisionados.", OutputKind.Info);
    }

    private static async Task RemoveAppxAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var entries = await GetAppxAsync(context, token);
        if (entries.Count == 0)
        {
            context.WriteLine("No hay paquetes aptos para el usuario actual.", OutputKind.Warning);
            context.SetOutcome(OperationOutcome.NotStarted("No se encontró ningún paquete Appx removible para el usuario actual."));
            return;
        }

        var selected = await context.ChooseAsync("Quitar Appx", "Elige un paquete exacto del usuario de la sesión elevada:", entries,
            package => $"{package.Name} — {package.PackageFullName}");
        if (selected is null) return;
        if (!AppxPackagePolicy.IsValidFullName(selected.PackageFullName))
        {
            throw new InvalidOperationException("El paquete seleccionado no cumple el formato permitido.");
        }
        if (!await context.ConfirmAsync("Confirmar desinstalación", $"Se desinstalará solo «{selected.PackageFullName}» para el usuario de esta sesión. Puede perderse información de la aplicación; no se desinstala para todos los usuarios ni se garantiza la reinstalación. ¿Continuar?")) return;

        await context.RunPowerShellAsync(PowerShellScriptId.RemoveCurrentUserAppx,
            new[] { selected.PackageFullName }, token);
        context.RequireManualAction(
            "El paquete Appx se retiró para el usuario actual sin error de proceso.",
            "Comprueba que la aplicación y sus datos ya no sean necesarios; la reinstalación o recuperación de datos no está garantizada.");
    }
}
