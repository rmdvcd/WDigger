using System.Text.RegularExpressions;
using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class RepairModule : IModule
{
    public string Name => "Reparación del sistema";
    public string Description => "SFC, DISM, CHKDSK y reparación de componentes de Windows Update.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "SFC /scannow",
            "Comprueba y repara los archivos protegidos de Windows. Puede tardar varios minutos.",
            (context, token) => RunSfcAsync(host, context, token),
            changesSystem: true,
            confirmation: "SFC analizará y reparará archivos del sistema. Guarda tu trabajo antes de continuar."),
        new ModuleAction(
            "DISM /RestoreHealth",
            "Repara el almacén de componentes de Windows; puede necesitar Windows Update.",
            (context, token) => RunDismAsync(host, context, token),
            changesSystem: true,
            confirmation: "DISM reparará el almacén de componentes y puede descargar archivos desde Windows Update."),
        new ModuleAction(
            "CHKDSK /scan /perf en C:",
            "Analiza el volumen del sistema. Si está en uso, la reparación puede quedar para el próximo reinicio.",
            (context, token) => RunChkdskAsync(host, context, token),
            changesSystem: true,
            confirmation: "Se analizará C:. Algunas correcciones pueden requerir un reinicio."),
        new ModuleAction(
            "Reparar Windows Update",
            "Detiene servicios, renombra SoftwareDistribution y catroot2 y reinicia los servicios.",
            (context, token) => RepairWindowsUpdateAsync(host, context, token),
            changesSystem: true,
            confirmation: "Se restablecerán los componentes y la caché de Windows Update. Se recomienda reiniciar al terminar."),
        new ModuleAction(
            "Restablecer caché de Winget",
            "Fuerza el restablecimiento de los orígenes de paquetes de Winget.",
            (context, token) => RunCommand(host, context, token, "winget", "source reset --force"),
            changesSystem: true)
    };

    private static async Task RunSfcAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var result = await RunCommand(host, context, token, "sfc", "/scannow");
        ExplainResult(context, result, "SFC");
    }

    private static async Task RunDismAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var result = await RunCommand(host, context, token, "DISM", "/Online /Cleanup-Image /RestoreHealth");
        ExplainResult(context, result, "DISM");
    }

    private static async Task RunChkdskAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var result = await RunCommand(host, context, token, "chkdsk", "/scan /perf C:");
        ExplainResult(context, result, "CHKDSK");
    }

    private static async Task RepairWindowsUpdateAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var services = new[] { "wuauserv", "cryptSvc", "bits", "msiserver" };
        var totalSteps = services.Length * 2 + 2;
        var step = 0;

        foreach (var service in services)
        {
            context.SetStatus($"Deteniendo {service}...");
            await RunCommand(host, context, token, "net", $"stop {service}");
            context.ReportProgress(++step * 100 / totalSteps);
        }

        await RunCommand(host, context, token, "cmd",
            "/c ren %SystemRoot%\\SoftwareDistribution SoftwareDistribution.old");
        context.ReportProgress(++step * 100 / totalSteps);
        await RunCommand(host, context, token, "cmd",
            "/c ren %SystemRoot%\\System32\\catroot2 catroot2.old");
        context.ReportProgress(++step * 100 / totalSteps);

        foreach (var service in Enumerable.Reverse(services))
        {
            context.SetStatus($"Iniciando {service}...");
            await RunCommand(host, context, token, "net", $"start {service}");
            context.ReportProgress(++step * 100 / totalSteps);
        }

        context.WriteLine("Restablecimiento finalizado. Se recomienda reiniciar Windows.", OutputKind.Success);
    }

    private static Task<CommandResult> RunCommand(
        AppHost host,
        ModuleExecutionContext context,
        CancellationToken token,
        string fileName,
        string arguments)
    {
        // Keep the host parameter in this helper to make every call site explicit
        // about the shared service container used by the module.
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }

    private static void ExplainResult(ModuleExecutionContext context, CommandResult result, string tool)
    {
        var output = result.StandardOutput ?? string.Empty;
        var explanation = tool switch
        {
            "SFC" when Regex.IsMatch(output, "did not find any integrity violations", RegexOptions.IgnoreCase)
                => "No se encontraron infracciones de integridad. Los archivos del sistema están correctos.",
            "SFC" when Regex.IsMatch(output, "found corrupt files and successfully repaired them", RegexOptions.IgnoreCase)
                => "Se encontraron y repararon archivos dañados. Se recomienda reiniciar.",
            "SFC" when Regex.IsMatch(output, "found corrupt files but was unable to fix some of them", RegexOptions.IgnoreCase)
                => "Algunos archivos no pudieron repararse. Ejecuta DISM /RestoreHealth y después repite SFC.",
            "DISM" when Regex.IsMatch(output, "restore operation completed successfully", RegexOptions.IgnoreCase)
                => "La reparación del almacén de componentes finalizó correctamente.",
            "DISM" when Regex.IsMatch(output, "no component store corruption detected", RegexOptions.IgnoreCase)
                => "No se detectaron daños en el almacén de componentes.",
            "CHKDSK" when Regex.IsMatch(output, "No problems found|no errors|has been scanned", RegexOptions.IgnoreCase)
                => "No se encontraron errores en el sistema de archivos.",
            "CHKDSK" when Regex.IsMatch(output, "Correcting errors|errors found|unrecoverable", RegexOptions.IgnoreCase)
                => "Se detectaron o corrigieron errores. Revisa la salida; puede ser necesario reiniciar.",
            _ => result.Success
                ? "La operación terminó correctamente. Revisa la salida y el registro completo."
                : "La operación terminó con errores. Revisa la salida y el registro completo."
        };
        context.WriteLine($"Interpretación de {tool}: {explanation}",
            result.Success ? OutputKind.Success : OutputKind.Warning);
    }
}
