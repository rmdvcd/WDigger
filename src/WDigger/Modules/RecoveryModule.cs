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
            (context, token) => Run(host, context, token, "winfr", "/?")),
        new ModuleAction("Listar unidades", "Muestra las unidades lógicas y su espacio disponible.",
            (context, token) => Run(host, context, token, "powershell",
                "-NoProfile -Command \"Get-CimInstance Win32_LogicalDisk | Select-Object DeviceID, DriveType, Size, FreeSpace | Format-Table -AutoSize\"")),
        new ModuleAction("Recuperar archivos eliminados", "Recupera datos a otra unidad mediante winfr y un filtro opcional.",
            (context, token) => RecoverFilesAsync(host, context, token), changesSystem: true,
            confirmation: "La recuperación escribirá archivos en la unidad de destino. Debe ser distinta de la unidad de origen.")
    };

    private static async Task RecoverFilesAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var source = await context.PromptAsync("Recuperar archivos", "Unidad de origen (por ejemplo, C:):");
        if (source is null) return;
        var destination = await context.PromptAsync("Recuperar archivos", "Unidad de destino, distinta del origen (por ejemplo, D:):");
        if (destination is null) return;
        if (NormalizeDrive(source).Equals(NormalizeDrive(destination), StringComparison.OrdinalIgnoreCase))
        {
            context.WriteLine("El origen y el destino deben ser unidades diferentes.", OutputKind.Warning);
            return;
        }

        var mode = await context.ChooseAsync("Modo de recuperación", "Selecciona el modo:",
            new[] { "Default", "Segment", "Signature" }, item => item);
        if (mode is null) return;
        var filter = await context.PromptAsync("Recuperar archivos",
            "Filtro opcional (por ejemplo, *.pdf o *.jpg):", allowEmpty: true);
        if (filter is null) return;

        var arguments = $"\"{source}\" \"{destination}\" /{mode.ToLowerInvariant()}";
        if (!string.IsNullOrWhiteSpace(filter)) arguments += $" /n \"{filter}\"";
        await context.RunCommandAsync("winfr", arguments, token);
    }

    private static string NormalizeDrive(string value) => value.Trim().TrimEnd('\\', '/', ':');

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
