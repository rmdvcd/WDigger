using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class DiskModule : IModule
{
    public string Name => "Discos y limpieza";
    public string Description => "Espacio en disco, análisis CHKDSK y limpieza de archivos temporales.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Mostrar espacio en discos", "Muestra capacidad, espacio libre y sistema de archivos.",
            (context, token) => ShowDiskSpaceAsync(host, context, token)),
        new ModuleAction("Limpiar archivos temporales", "Elimina el contenido de %TEMP%; omite archivos en uso.",
            (context, token) => CleanTempFilesAsync(host, context, token), changesSystem: true,
            confirmation: "Se eliminarán archivos temporales. Cierra las aplicaciones abiertas antes de continuar."),
        new ModuleAction("Analizar una unidad", "Ejecuta CHKDSK /scan en la letra de unidad indicada.",
            (context, token) => ScanDriveAsync(host, context, token), changesSystem: true,
            confirmation: "El análisis puede corregir errores o programar operaciones para el próximo reinicio.", canCancel: false, allowedInElevatedSession: true)
    };

    private static Task<CommandResult> ShowDiskSpaceAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        return context.RunCommandAsync("powershell",
            "-NoProfile -Command \"Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID, Description, Size, FreeSpace, FileSystem | Format-Table -AutoSize -Wrap\"",
            token);
    }

    private static Task<CommandResult> CleanTempFilesAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        return context.RunCommandAsync("cmd", "/c del /s /f /q \"%TEMP%\\*\" 2>nul", token);
    }

    private static async Task ScanDriveAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var input = await context.PromptAsync("Analizar unidad", "Letra de unidad:", "C");
        if (input is null) return;
        var drive = input.TrimEnd(':').Trim();
        if (drive.Length != 1 || !((drive[0] >= 'A' && drive[0] <= 'Z') || (drive[0] >= 'a' && drive[0] <= 'z')))
        {
            context.WriteLine("Escribe una única letra de unidad válida, por ejemplo C o D.", OutputKind.Warning);
            return;
        }
        var target = $"{drive.ToUpperInvariant()}:";
        if (!await context.ConfirmAsync("Confirmar volumen", $"CHKDSK analizará {target} y podría realizar correcciones. No apagues el equipo. ¿Continuar?")) return;
        await context.RunArgumentsAsync("chkdsk", new[] { target, "/scan" }, token);
    }
}
