using WDigger.Core;
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
            "Comprueba y puede reparar archivos protegidos de Windows. Puede tardar varios minutos.",
            RunSfcAsync,
            changesSystem: true,
            confirmation: "SFC analizará y puede reparar archivos protegidos del sistema. Guarda tu trabajo antes de continuar.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Moderate,
            commandTimeout: TimeSpan.FromHours(2), diagnosticCheckId: "integrity"),
        new ModuleAction(
            "DISM /RestoreHealth",
            "Repara el almacén de componentes de Windows; puede necesitar Windows Update.",
            RunDismAsync,
            changesSystem: true,
            confirmation: "DISM reparará el almacén de componentes y puede descargar archivos desde Windows Update.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Moderate,
            commandTimeout: TimeSpan.FromHours(2), diagnosticCheckId: "image"),
        new ModuleAction(
            "CHKDSK /scan en el volumen del sistema",
            "Analiza en línea el volumen del sistema. Windows puede realizar correcciones y solicitar un reinicio.",
            RunChkdskAsync,
            changesSystem: true,
            confirmation: "Se analizará el volumen del sistema; algunas correcciones pueden requerir un reinicio.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.High,
            commandTimeout: TimeSpan.FromHours(2), diagnosticCheckId: "filesystem"),
        new ModuleAction(
            "Reparar Windows Update",
            "Detiene servicios, renombra SoftwareDistribution y catroot2 y reinicia los servicios originalmente activos.",
            ResetWindowsUpdateAsync,
            changesSystem: true,
            confirmation: "Se detendrán temporalmente servicios de Windows Update y se renombrarán las cachés. Conserva una copia de seguridad y no apagues el equipo.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.High,
            commandTimeout: TimeSpan.FromMinutes(20), recovery: OperationRecovery.CompensatesInProcess)
    };

    private static async Task ResetWindowsUpdateAsync(ModuleExecutionContext context, CancellationToken token)
    {
        try
        {
            await context.RunPowerShellAsync(PowerShellScriptId.ResetWindowsUpdate, Array.Empty<string>(), token);
            context.WriteLine("Las cachés anteriores se conservaron con un sufijo WDigger. Verifica Windows Update antes de borrarlas.", OutputKind.Warning);
        }
        catch (Exception)
        {
            context.RequireManualAction(
                "El restablecimiento de Windows Update no terminó de forma verificable.",
                "Revisa los servicios wuauserv, bits y cryptsvc, además de las carpetas SoftwareDistribution y catroot2 con sufijo WDigger, antes de reintentar.");
            throw;
        }
    }

    private static async Task RunSfcAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(new CommandSpec(WindowsTool.Sfc, new[] { "/scannow" }, CommandEffect.ModifiesSystem), token);
        context.WriteLine("SFC terminó sin error de proceso. Revisa la salida; WDigger no interpreta la salud del sistema a partir de texto localizado.", OutputKind.Info);
    }

    private static async Task RunDismAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(new CommandSpec(WindowsTool.Dism,
            new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, CommandEffect.ModifiesSystem), token);
        context.WriteLine("DISM terminó sin error de proceso. Revisa la salida y reinicia si Windows lo solicita.", OutputKind.Info);
    }

    private static async Task RunChkdskAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root) || root.Length != 3 || root[1] != ':')
        {
            throw new InvalidOperationException("No se pudo determinar la unidad del sistema.");
        }
        await context.RunCommandAsync(new CommandSpec(WindowsTool.Chkdsk, new[] { root[..2], "/scan" }, CommandEffect.ModifiesSystem), token);
        context.RequireManualAction(
            "CHKDSK finalizó sin error de proceso; revisa la salida para confirmar si Windows requiere un reinicio o una reparación adicional.",
            "Si CHKDSK indicó una reparación sin conexión, programa esa reparación y reinicia solo cuando puedas mantener la alimentación estable.");
    }
}
