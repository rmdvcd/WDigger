using System.Text.Json;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class SoftwareModule : IModule
{
    public string Name => "Software";
    public string Description => "Inventario y retirada limitada de aplicaciones Appx. Las integraciones de paquetes siguen suspendidas.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Instalar paquetes configurados",
            "La instalación de paquetes externos está suspendida.",
            (_, _) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Winget y Chocolatey siguen suspendidos hasta verificar origen, ruta, firma, versión e identidad de cada paquete.",
            risk: OperationRisk.High),
        new ModuleAction(
            "Instalar un paquete configurado",
            "La instalación de paquetes externos está suspendida.",
            (_, _) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Winget y Chocolatey siguen suspendidos hasta verificar origen, ruta, firma, versión e identidad de cada paquete.",
            risk: OperationRisk.High),
        new ModuleAction(
            "Buscar un paquete",
            "Buscaría en los orígenes configurados de Winget.",
            (_, _) => Task.CompletedTask,
            unavailableReason: "La ejecución de Winget desde una aplicación elevada sigue suspendida hasta verificar ruta, firma e identidad del paquete en cada equipo."),
        new ModuleAction(
            "Listar paquetes instalados",
            "Mostraría el inventario conocido por Winget.",
            (_, _) => Task.CompletedTask,
            unavailableReason: "La ejecución de Winget desde una aplicación elevada sigue suspendida hasta verificar ruta, firma e identidad del paquete en cada equipo."),
        new ModuleAction(
            "Quitar un paquete Appx",
            "Selecciona un único paquete removible del usuario actual; no elimina paquetes provisionados ni afecta a otros usuarios.",
            RemoveAppxAsync, changesSystem: true,
            confirmation: "Desinstalar una aplicación puede afectar a datos y funciones; no existe rollback automático.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Destructive,
            commandTimeout: TimeSpan.FromMinutes(15))
    };

    public sealed class AppxEntry
    {
        public string Name { get; set; } = "";
        public string PackageFullName { get; set; } = "";
    }

    private static async Task RemoveAppxAsync(ModuleExecutionContext context, CancellationToken token)
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
        var entries = (parsed ?? new List<AppxEntry>())
            .Where(package => AppxPackagePolicy.IsValidFullName(package.PackageFullName))
            .OrderBy(package => package.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
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
