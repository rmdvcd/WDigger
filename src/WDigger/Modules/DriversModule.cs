using System.IO;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class DriversModule : IModule
{
    public string Name => "Controladores";
    public string Description => "Copia de seguridad, inventario e instalación de controladores INF.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Exportar todos los controladores",
            "Guarda los controladores de terceros mediante Export-WindowsDriver.",
            (context, token) => BackupDriversAsync(host, context, token),
            changesSystem: true,
            confirmation: "Se creará una copia de los controladores en la carpeta que selecciones."),
        new ModuleAction(
            "Listar controladores instalados",
            "Ejecuta pnputil /enum-drivers y muestra su salida.",
            (context, token) => context.RunCommandAsync("pnputil", "/enum-drivers", token)),
        new ModuleAction(
            "Instalar un paquete de controlador",
            "Selecciona un archivo INF y lo instala mediante pnputil.",
            (context, token) => AddDriverAsync(host, context, token),
            changesSystem: true,
            confirmation: "Instalar un controlador incorrecto puede dejar un dispositivo inutilizable.")
    };

    private static async Task BackupDriversAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var initial = Path.Combine(host.BaseDirectory, "drivers-backup");
        var destination = await context.PickFolderAsync("Selecciona la carpeta de destino de la copia", initial)
            ?? await context.PromptAsync("Exportar controladores", "Carpeta de destino:", initial);
        if (destination is null)
        {
            return;
        }

        string path;
        try
        {
            path = Path.GetFullPath(destination);
        }
        catch (Exception exception)
        {
            context.WriteLine($"La ruta de destino no es válida: {exception.Message}", OutputKind.Error);
            return;
        }

        context.WriteLine($"Destino: {path}", OutputKind.Muted);
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception)
        {
            context.WriteLine($"No se pudo crear la carpeta de destino: {exception.Message}", OutputKind.Error);
            return;
        }

        var escaped = path.Replace("'", "''");
        await context.RunCommandAsync("powershell",
            $"-NoProfile -Command \"Export-WindowsDriver -Online -Destination '{escaped}' -Verbose\"", token);
    }

    private static async Task AddDriverAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var inf = await context.PickFileAsync("Seleccionar controlador",
            "Controladores INF (*.inf)|*.inf|Todos los archivos (*.*)|*.*");
        if (inf is null)
        {
            return;
        }
        await context.RunCommandAsync("pnputil", $"/add-driver \"{inf}\" /install", token);
    }
}
