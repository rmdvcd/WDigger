using System.IO;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class BackupModule : IModule
{
    public string Name => "Copias de seguridad";
    public string Description => "Localización y detalles de copias Acronis True Image (.tibx).";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Buscar copias .tibx", "Busca copias de seguridad de forma recursiva en una carpeta.",
            (context, token) => ListTibxAsync(host, context, token)),
        new ModuleAction("Ver detalles de una copia", "Muestra tamaño y fecha de modificación de un archivo .tibx.",
            (context, token) => ShowDetailsAsync(host, context, token))
    };

    private static async Task ListTibxAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var folder = await context.PickFolderAsync("Selecciona la carpeta donde buscar copias .tibx", @"C:\Backups");
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
            context.SetOutcome(OperationOutcome.NotStarted("No se encontraron copias .tibx en la carpeta seleccionada."));
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

    private static async Task ShowDetailsAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        _ = token;
        var file = await context.PickFileAsync("Seleccionar copia de seguridad", "Acronis True Image (*.tibx)|*.tibx");
        if (file is null) return;
        var info = new FileInfo(file);
        context.ShowTable("Detalles de la copia", new[] { "Propiedad", "Valor" }, new[]
        {
            new[] { "Archivo", info.FullName },
            new[] { "Tamaño", $"{info.Length / 1024.0 / 1024.0 / 1024.0:F2} GB" },
            new[] { "Modificado", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss") },
            new[] { "Contenido", "La exploración del contenido requiere Acronis True Image." }
        });
    }

}
