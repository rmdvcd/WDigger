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
            "Guarda los controladores de terceros mediante Export-WindowsDriver en una carpeta elegida.",
            BackupDriversAsync,
            changesSystem: true,
            confirmation: "Se creará una copia de los controladores en la carpeta que selecciones.", risk: OperationRisk.Low),
        new ModuleAction(
            "Listar controladores instalados",
            "Ejecuta pnputil /enum-drivers y muestra su salida.",
            (context, token) => context.RunCommandAsync(CommandSpec.Read(WindowsTool.PnPUtil, "/enum-drivers"), token)),
        new ModuleAction(
            "Instalar un paquete de controlador",
            "Selecciona un archivo INF y lo instala mediante pnputil.",
            AddDriverAsync,
            changesSystem: true,
            confirmation: "Instalar un controlador incorrecto puede dejar un dispositivo inutilizable.", risk: OperationRisk.High)
    };

    private static async Task BackupDriversAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var initial = Path.Combine(context.Host.BaseDirectory, "drivers-backup");
        var destination = await context.PickFolderAsync("Selecciona la carpeta de destino de la copia", initial)
            ?? await context.PromptAsync("Exportar controladores", "Carpeta de destino:", initial);
        if (destination is null) return;

        string path;
        try
        {
            path = Path.GetFullPath(destination);
        }
        catch (Exception exception)
        {
            context.WriteLine($"La ruta de destino no es válida: {exception.Message}", OutputKind.Error);
            context.SetOutcome(OperationOutcome.NotStarted("El destino de la copia de controladores no es válido."));
            return;
        }
        context.WriteLine($"Destino: {path}", OutputKind.Muted);
        context.StartStep("driver-backup-destination", "Validando y preparando el destino de la copia de controladores.");
        try
        {
            context.EnsurePersistentJournal();
            if (!context.IsSimulation) Directory.CreateDirectory(path);
            else context.WriteLine($"[SIMULACIÓN] Se crearía la carpeta {path}.", OutputKind.Warning);
            context.CompleteStep(context.IsSimulation ? OperationStatus.Simulated : OperationStatus.Succeeded,
                context.IsSimulation ? "Destino de copia simulado." : "Destino de copia preparado.");
        }
        catch (Exception exception)
        {
            context.CompleteStep(OperationStatus.Failed, "No se pudo preparar el destino de la copia.");
            context.WriteLine($"No se pudo crear la carpeta de destino: {exception.Message}", OutputKind.Error);
            context.SetOutcome(OperationOutcome.Failed("No se pudo preparar el destino de la copia de controladores."));
            return;
        }

        await context.RunPowerShellAsync(PowerShellScriptId.ExportWindowsDrivers, new[] { path }, token);
    }

    private static async Task AddDriverAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var inf = await context.PickFileAsync("Seleccionar controlador",
            "Controladores INF (*.inf)|*.inf|Todos los archivos (*.*)|*.*");
        if (inf is null) return;
        inf = Path.GetFullPath(inf);
        if (!inf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase) || !File.Exists(inf))
        {
            throw new ArgumentException("El archivo seleccionado no es un INF existente.");
        }
        if (!await context.ConfirmAsync("Instalar controlador", $"Se instalará el controlador de «{inf}». Un controlador incorrecto puede inutilizar un dispositivo. ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.PnPUtil, "/add-driver", inf, "/install"), token);
        context.RequireManualAction(
            "PnPUtil terminó sin error de proceso. Comprueba el proveedor, firma, compatibilidad y el estado del dispositivo.",
            "Reinicia solo si Windows o el fabricante lo solicitan; conserva el paquete INF y el journal para soporte.");
    }
}
