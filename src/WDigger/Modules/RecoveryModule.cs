using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class RecoveryModule : IModule
{
    public string Name => "Recuperación de archivos";
    public string Description => "Recuperación de archivos eliminados con Windows File Recovery (winfr).";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Comprobar Windows File Recovery", "Muestra la ayuda de winfr para confirmar que está disponible.",
            (context, token) => Run(host, context, token, "winfr", "/?"),
            unavailableReason: "La invocación de WinFR está suspendida hasta verificar de forma independiente su ruta y firma en una sesión elevada."),
        new ModuleAction("Listar unidades", "Muestra las unidades lógicas y su espacio disponible.",
            (context, token) => Run(host, context, token, "powershell",
                "-NoProfile -Command \"Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID, DriveType, Size, FreeSpace | Format-Table -AutoSize\"")),
        new ModuleAction("Recuperar archivos eliminados", "Recupera datos a otra unidad mediante winfr y un filtro opcional.",
            (context, token) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Suspendida hasta verificar los volúmenes físicos, el destino y la versión de winfr.")
    };

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
