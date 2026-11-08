using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class SoftwareModule : IModule
{
    public string Name => "Software";
    public string Description => "Instalación con Winget o Chocolatey y retirada de aplicaciones Appx.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Instalar paquetes configurados",
            "Instala de forma desatendida todos los paquetes de config/software.json.",
            (context, token) => InstallAllAsync(host, context, token)),
        new ModuleAction(
            "Instalar un paquete configurado",
            "Permite elegir un único paquete de config/software.json.",
            (context, token) => InstallSingleAsync(host, context, token)),
        new ModuleAction(
            "Buscar un paquete",
            "Busca en los orígenes configurados de Winget.",
            (context, token) => SearchAsync(host, context, token)),
        new ModuleAction(
            "Listar paquetes instalados",
            "Muestra el inventario de aplicaciones conocido por Winget.",
            (context, token) => context.RunCommandAsync("winget", "list --accept-source-agreements", token)),
        new ModuleAction(
            "Quitar un paquete Appx",
            "Quita aplicaciones que coincidan con un patrón; puede afectar a funciones de Windows.",
            (context, token) => RemoveAppxAsync(host, context, token),
            changesSystem: true,
            confirmation: "Quitar paquetes Appx puede romper funciones o aplicaciones de Windows.")
    };

    private static async Task InstallAllAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var packages = host.Config.Software;
        if (packages.Count == 0)
        {
            context.WriteLine("No hay paquetes definidos en config/software.json.", OutputKind.Warning);
            return;
        }

        ShowPackages(context, packages);
        if (!context.Confirm("Instalar paquetes configurados",
                $"Se procesarán {packages.Count} paquetes. ¿Quieres continuar?"))
        {
            return;
        }

        for (var index = 0; index < packages.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var package = packages[index];
            context.SetStatus($"Instalando {package.Name} ({index + 1}/{packages.Count})...");
            var executable = Executable(package);
            var arguments = InstallArguments(package);
            await context.RunCommandAsync(executable, arguments, token);
            context.ReportProgress((index + 1) * 100 / packages.Count);
        }
    }

    private static async Task InstallSingleAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var package = context.Choose("Instalar paquete", "Selecciona el paquete:",
            host.Config.Software, item => $"{item.Name} ({item.Id}) [{item.Source}]");
        if (package is null)
        {
            if (host.Config.Software.Count == 0)
            {
                context.WriteLine("No hay paquetes definidos en config/software.json.", OutputKind.Warning);
            }
            return;
        }

        if (!context.Confirm("Instalar paquete", $"Se instalará {package.Name} ({package.Id}). ¿Continuar?"))
        {
            return;
        }
        await context.RunCommandAsync(Executable(package), InstallArguments(package), token);
    }

    private static async Task SearchAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var query = context.Prompt("Buscar con Winget", "Nombre o identificador del paquete:");
        if (query is null)
        {
            return;
        }
        await context.RunCommandAsync("winget", $"search \"{Escape(query)}\" --accept-source-agreements", token);
    }

    private static async Task RemoveAppxAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var pattern = context.Prompt("Quitar paquete Appx", "Patrón del nombre (por ejemplo, *Weather*):");
        if (pattern is null)
        {
            return;
        }

        var escaped = pattern.Replace("'", "''");
        await context.RunCommandAsync("powershell",
            $"-NoProfile -Command \"Get-AppxPackage '{escaped}' | Remove-AppxPackage\"", token);
    }

    private static void ShowPackages(ModuleExecutionContext context, IEnumerable<SoftwareDefinition> packages)
    {
        context.ShowTable("Paquetes configurados", new[] { "Id", "Nombre", "Origen", "Categoría" },
            packages.Select(package => new[] { package.Id, package.Name, package.Source, package.Category }));
    }

    private static string Executable(SoftwareDefinition package)
        => package.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ? "choco" : "winget";

    private static string InstallArguments(SoftwareDefinition package)
        => package.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase)
            ? $"install {package.Id} {package.Arguments} -y"
            : $"install \"{package.Id}\" {package.Arguments} --silent --accept-package-agreements --accept-source-agreements";

    private static string Escape(string value) => value.Replace("\"", "\\\"");
}
