using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class DiskModule : IModule
{
    public string Name => "Discos y limpieza";
    public string Description => "Espacio en disco, análisis CHKDSK y limpieza previsualizada de archivos temporales.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Mostrar espacio en discos", "Muestra capacidad, espacio libre y sistema de archivos.", ShowDiskSpaceAsync),
        new ModuleAction("Limpiar archivos temporales", "Previsualiza y elimina solo contenido de la carpeta temporal actual; omite puntos de redirección y archivos en uso.",
            CleanTempFilesAsync, changesSystem: true,
            confirmation: "Se previsualizarán y después se eliminarán archivos temporales. Cierra las aplicaciones abiertas antes de continuar.",
            risk: OperationRisk.Moderate),
        new ModuleAction("Analizar una unidad", "Ejecuta CHKDSK /scan en la letra de unidad indicada.",
            ScanDriveAsync, changesSystem: true,
            confirmation: "El análisis puede corregir errores o programar operaciones para el próximo reinicio.", canCancel: false,
            allowedInElevatedSession: true, risk: OperationRisk.High, commandTimeout: TimeSpan.FromHours(2))
    };

    private static Task ShowDiskSpaceAsync(ModuleExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var rows = DriveInfo.GetDrives().Select(drive =>
        {
            try
            {
                return new[]
                {
                    drive.Name,
                    drive.DriveType.ToString(),
                    drive.IsReady ? drive.DriveFormat : "No disponible",
                    drive.IsReady ? $"{drive.TotalSize / 1073741824d:F1} GiB" : "n/a",
                    drive.IsReady ? $"{drive.AvailableFreeSpace / 1073741824d:F1} GiB" : "n/a"
                };
            }
            catch (Exception exception)
            {
                return new[] { drive.Name, drive.DriveType.ToString(), "No disponible", "n/a", exception.Message };
            }
        }).ToList();
        context.ShowTable("Espacio en discos", new[] { "Unidad", "Tipo", "Sistema de archivos", "Capacidad", "Libre" }, rows);
        return Task.CompletedTask;
    }

    private static async Task CleanTempFilesAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var cleaner = new TemporaryFileCleaner();
        var root = Path.GetTempPath();
        context.StartStep("temp-cleanup-preview", "Previsualizando contenido temporal sin eliminarlo.");
        context.SetStatus("Previsualizando archivos temporales...");
        var preview = await Task.Run(() => cleaner.Inspect(root, token), token);
        context.CompleteStep(OperationStatus.Succeeded, "Previsualización de contenido temporal completada.");
        context.WriteLine($"Previsualización: {preview.Files} archivo(s), {preview.Directories} carpeta(s), " +
            $"{preview.Bytes / 1048576d:F1} MiB. Se omitirán {preview.ReparsePoints} punto(s) de redirección y " +
            $"se detectaron {preview.Errors} error(es) de lectura.", OutputKind.Info);
        if (preview.Files == 0 && preview.Directories == 0)
        {
            context.WriteLine("No hay contenido temporal eliminable.", OutputKind.Info);
            context.SetOutcome(OperationOutcome.NotStarted("No había contenido temporal eliminable."));
            return;
        }
        if (!await context.ConfirmAsync("Confirmar limpieza", "Solo se eliminará el contenido previsualizado de la carpeta temporal actual. Los puntos de redirección y archivos en uso se omitirán. ¿Continuar?")) return;
        if (context.IsSimulation)
        {
            context.WriteLine("[SIMULACIÓN] No se eliminó contenido temporal.", OutputKind.Warning);
            return;
        }

        context.StartStep("temp-cleanup-delete", "Eliminando el contenido temporal previsualizado.");
        context.EnsurePersistentJournal();
        context.SetStatus("Eliminando archivos temporales...");
        var result = await Task.Run(() => cleaner.Delete(preview.Root, token), token);
        context.CompleteStep(result.Errors.Count == 0 ? OperationStatus.Succeeded : OperationStatus.Partial,
            result.Errors.Count == 0 ? "Limpieza temporal completada." : "Limpieza temporal completada con elementos omitidos.");
        context.WriteLine($"Limpieza: {result.DeletedFiles} archivo(s), {result.DeletedDirectories} carpeta(s), " +
            $"{result.DeletedBytes / 1048576d:F1} MiB eliminados; {result.SkippedReparsePoints} punto(s) de redirección omitidos.",
            result.Errors.Count == 0 ? OutputKind.Success : OutputKind.Warning);
        if (result.Errors.Count > 0)
        {
            context.SetOutcome(OperationOutcome.Partial(
                "La limpieza temporal terminó con elementos omitidos o errores.",
                "Cierra las aplicaciones que usen los archivos temporales y revisa la salida antes de reintentar."));
            foreach (var error in result.Errors.Take(10)) context.WriteLine(error, OutputKind.Warning);
        }
    }

    private static async Task ScanDriveAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var input = await context.PromptAsync("Analizar unidad", "Letra de unidad:", "C");
        if (input is null) return;
        var drive = input.TrimEnd(':').Trim();
        if (drive.Length != 1 || !((drive[0] >= 'A' && drive[0] <= 'Z') || (drive[0] >= 'a' && drive[0] <= 'z')))
        {
            context.WriteLine("Escribe una única letra de unidad válida, por ejemplo C o D.", OutputKind.Warning);
            context.SetOutcome(OperationOutcome.NotStarted("No se indicó una letra de unidad válida."));
            return;
        }
        var target = $"{drive.ToUpperInvariant()}:";
        if (!DriveInfo.GetDrives().Any(item => string.Equals(item.Name, target + "\\", StringComparison.OrdinalIgnoreCase) && item.IsReady))
        {
            throw new ArgumentException("La unidad indicada no existe o no está lista.");
        }
        if (!await context.ConfirmAsync("Confirmar volumen", $"CHKDSK analizará {target} y podría realizar correcciones. No apagues el equipo. ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Chkdsk, target, "/scan"), token);
        context.RequireManualAction(
            "CHKDSK terminó sin error de proceso; revisa su salida antes de considerar el volumen reparado.",
            "Si CHKDSK indicó una reparación sin conexión, prográmala y reinicia cuando sea seguro hacerlo.");
    }
}
