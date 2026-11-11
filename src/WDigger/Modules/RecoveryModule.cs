using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class RecoveryModule : IModule
{
    public string Name => "Recuperación de archivos";
    public string Description => "Recuperación de archivos eliminados con Windows File Recovery (winfr).";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Comprobar Windows File Recovery", "Comprobaría la disponibilidad de winfr.",
            (_, _) => Task.CompletedTask,
            unavailableReason: "La invocación de WinFR está suspendida hasta verificar de forma independiente ruta, firma, versión, volúmenes físicos y destino."),
        new ModuleAction("Listar unidades", "Muestra las unidades lógicas y su espacio disponible.", ListDrivesAsync),
        new ModuleAction("Recuperar archivos eliminados", "Recuperaría datos a otra unidad mediante winfr y un filtro opcional.",
            (_, _) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Suspendida hasta verificar los volúmenes físicos, el destino y la versión de winfr.", risk: OperationRisk.Destructive)
    };

    private static Task ListDrivesAsync(ModuleExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rows = DriveInfo.GetDrives().Select(drive =>
        {
            try
            {
                return new[]
                {
                    drive.Name, drive.DriveType.ToString(), drive.IsReady ? $"{drive.TotalSize / 1073741824d:F1} GiB" : "n/a",
                    drive.IsReady ? $"{drive.AvailableFreeSpace / 1073741824d:F1} GiB" : "n/a"
                };
            }
            catch { return new[] { drive.Name, drive.DriveType.ToString(), "n/a", "n/a" }; }
        });
        context.ShowTable("Unidades lógicas", new[] { "Unidad", "Tipo", "Capacidad", "Libre" }, rows);
        return Task.CompletedTask;
    }
}
