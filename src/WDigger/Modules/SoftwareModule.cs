using System.Linq;
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
            (context, token) => InstallAllAsync(host, context, token), changesSystem: true),
        new ModuleAction(
            "Instalar un paquete configurado",
            "Permite elegir un único paquete de config/software.json.",
            (context, token) => InstallSingleAsync(host, context, token), changesSystem: true),
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
            (context, token) => Task.CompletedTask,
            changesSystem: true,
            unavailableReason: "Suspendida hasta poder seleccionar y confirmar los paquetes exactos.")
    };

    private static async Task InstallAllAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var packages = host.Config.Software;
        if (packages.Count == 0)
        {
            context.WriteLine("No hay paquetes definidos en config/software.json.", OutputKind.Warning);
            return;
        }

        foreach (var package in packages) SoftwarePackagePolicy.Validate(package);
        ShowPackages(context, packages);
        if (!await context.ConfirmAsync("Instalar paquetes configurados",
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
            await context.RunArgumentsAsync(executable, arguments, token);
            context.ReportProgress((index + 1) * 100 / packages.Count);
        }
    }

    private static async Task InstallSingleAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var package = await context.ChooseAsync("Instalar paquete", "Selecciona el paquete:",
            host.Config.Software, item => $"{item.Name} ({item.Id}) [{item.Source}]");
        if (package is null)
        {
            if (host.Config.Software.Count == 0)
            {
                context.WriteLine("No hay paquetes definidos en config/software.json.", OutputKind.Warning);
            }
            return;
        }

        SoftwarePackagePolicy.Validate(package);
        if (!await context.ConfirmAsync("Instalar paquete", $"Se instalará {package.Name} ({package.Id}). ¿Continuar?"))
        {
            return;
        }
        await context.RunArgumentsAsync(Executable(package), InstallArguments(package), token);
    }

    private static async Task SearchAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var query = await context.PromptAsync("Buscar con Winget", "Nombre o identificador del paquete:");
        if (query is null)
        {
            return;
        }
        await context.RunArgumentsAsync("winget", new[] { "search", "--query", query, "--accept-source-agreements" }, token);
    }

    private static void ShowPackages(ModuleExecutionContext context, IEnumerable<SoftwareDefinition> packages)
    {
        context.ShowTable("Paquetes configurados", new[] { "Id", "Nombre", "Origen", "Categoría" },
            packages.Select(package => new[] { package.Id, package.Name, package.Source, package.Category }));
    }

    private static string Executable(SoftwareDefinition package)
        => package.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase) ? "choco" : "winget";

    private static IReadOnlyList<string> InstallArguments(SoftwareDefinition package)
        => package.Source.Equals("chocolatey", StringComparison.OrdinalIgnoreCase)
            ? new[] { "install", package.Id, "-y" }
            : new[] { "install", "--id", package.Id, "--exact", "--silent", "--accept-package-agreements", "--accept-source-agreements" };
}
