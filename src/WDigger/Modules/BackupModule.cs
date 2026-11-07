using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class BackupModule : IModule
{
    public string Name => "Copias de seguridad";
    public string Description => "Localización, detalles y restauración de copias Acronis True Image (.tibx).";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Buscar copias .tibx", "Busca copias de seguridad de forma recursiva en una carpeta.",
            (context, token) => ListTibxAsync(host, context, token)),
        new ModuleAction("Ver detalles de una copia", "Muestra tamaño y fecha de modificación de un archivo .tibx.",
            (context, token) => ShowDetailsAsync(host, context, token)),
        new ModuleAction("Restaurar una copia .tibx", "Usa deploycmd de Acronis para restaurar en una unidad de destino.",
            (context, token) => RestoreTibxAsync(host, context, token), changesSystem: true,
            confirmation: "ADVERTENCIA: restaurar una copia puede sobrescribir todos los datos del disco de destino.")
    };

    private static async Task ListTibxAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var folder = context.PickFolder("Selecciona la carpeta donde buscar copias .tibx", @"C:\Backups");
        if (folder is null) return;

        context.SetStatus("Buscando archivos .tibx...");
        var files = await Task.Run(() => Directory
            .EnumerateFiles(folder, "*.tibx", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTime)
            .ToList(), token);
        token.ThrowIfCancellationRequested();

        if (files.Count == 0)
        {
            context.WriteLine("No se encontraron archivos .tibx.", OutputKind.Warning);
            return;
        }
        context.ShowTable("Copias de seguridad .tibx", new[] { "Archivo", "Ubicación", "Tamaño", "Modificado" },
            files.Select(info => new[]
            {
                info.Name,
                info.DirectoryName ?? string.Empty,
                $"{info.Length / 1024.0 / 1024.0 / 1024.0:F2} GB",
                info.LastWriteTime.ToString("yyyy-MM-dd HH:mm")
            }));
    }

    private static Task ShowDetailsAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        _ = token;
        var file = context.PickFile("Seleccionar copia de seguridad", "Acronis True Image (*.tibx)|*.tibx");
        if (file is null) return Task.CompletedTask;
        var info = new FileInfo(file);
        context.ShowTable("Detalles de la copia", new[] { "Propiedad", "Valor" }, new[]
        {
            new[] { "Archivo", info.FullName },
            new[] { "Tamaño", $"{info.Length / 1024.0 / 1024.0 / 1024.0:F2} GB" },
            new[] { "Modificado", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") },
            new[] { "Contenido", "La exploración del contenido requiere Acronis True Image." }
        });
        return Task.CompletedTask;
    }

    private static async Task RestoreTibxAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var file = context.PickFile("Seleccionar copia para restaurar", "Acronis True Image (*.tibx)|*.tibx");
        if (file is null) return;
        var target = context.Prompt("Restaurar copia", "Letra de la unidad de destino (por ejemplo, C):");
        if (target is null) return;
        target = target.TrimEnd(':').Trim();
        if (target.Length != 1 || !char.IsLetter(target[0]))
        {
            context.WriteLine("La unidad de destino no es válida.", OutputKind.Warning);
            return;
        }

        var acronis = new[]
        {
            @"C:\Program Files (x86)\Acronis\BackupAndRecovery\deploycmd.exe",
            @"C:\Program Files\Acronis\BackupAndRecovery\deploycmd.exe"
        }.FirstOrDefault(File.Exists);
        if (acronis is null)
        {
            context.WriteLine("No se encontró deploycmd.exe. Instala Acronis Backup and Recovery para habilitar la restauración.",
                OutputKind.Error);
            return;
        }
        await context.RunCommandAsync(acronis, $"restore --backup \"{file}\" --target {target}:", token);
    }
}
