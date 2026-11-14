using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class RecoveryModule : IModule
{
    public string Name => "Recuperación de archivos";
    public string Description => "Unidades lógicas y su espacio disponible.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Listar unidades", "Muestra las unidades lógicas y su espacio disponible.", ListDrivesAsync)
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
