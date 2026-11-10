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
            confirmation: "SFC analizará y puede reparar archivos protegidos del sistema. Guarda tu trabajo antes de continuar.",
            canCancel: false, allowedInElevatedSession: true),
        new ModuleAction(
            "DISM /RestoreHealth",
            "Repara el almacén de componentes de Windows; puede necesitar Windows Update.",
            (context, token) => RunDismAsync(host, context, token),
            changesSystem: true,
            confirmation: "DISM reparará el almacén de componentes y puede descargar archivos desde Windows Update.",
            canCancel: false, allowedInElevatedSession: true),
        new ModuleAction(
            "CHKDSK /scan en el volumen del sistema",
            "Analiza en línea el volumen del sistema. Windows puede realizar correcciones y solicitar un reinicio.",
            (context, token) => RunChkdskAsync(host, context, token),
            changesSystem: true,
            confirmation: "Se analizará el volumen del sistema; algunas correcciones pueden requerir un reinicio.",
            canCancel: false, allowedInElevatedSession: true),
        new ModuleAction(
            "Reparar Windows Update",
            "Detiene servicios, renombra SoftwareDistribution y catroot2 y reinicia los servicios.",
            (context, token) => ResetWindowsUpdateAsync(context, token),
            changesSystem: true,
            confirmation: "Se detendrán temporalmente servicios de Windows Update y se renombrarán las cachés. Conserva una copia de seguridad y no apagues el equipo.",
            canCancel: false, allowedInElevatedSession: true),
        new ModuleAction(
            "Restablecer caché de Winget",
            "Fuerza el restablecimiento de los orígenes de paquetes de Winget.",
            (context, token) => RunCommand(host, context, token, "winget", "source reset --force"),
            changesSystem: true)
    };

    private static Task<CommandResult> ResetWindowsUpdateAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windows)) throw new InvalidOperationException("No se pudo identificar la carpeta de Windows.");
        var script = """
            $services = @('wuauserv', 'bits', 'cryptsvc')
            $running = @()
            foreach ($name in $services) {
                $s = Get-Service -Name $name -ErrorAction Stop
                if ($s.Status -notin @('Running','Stopped')) { throw "Servicio $name en estado transitorio; no se modifica nada." }
                if ($s.Status -eq 'Running') { $running += $name }
            }
            $directories = @((Join-Path '__WINDOWS__' 'SoftwareDistribution'), (Join-Path '__WINDOWS__' 'System32\catroot2'))
            foreach ($path in $directories) {
                if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Falta la carpeta $path; no se modifica nada." }
                if ((Get-Item -LiteralPath $path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Carpeta redirigida: $path; no se modifica nada." }
            }
            $renamed = @()
            $failure = $null
            try {
                foreach ($name in $running) {
                    Stop-Service -Name $name -ErrorAction Stop
                    (Get-Service -Name $name).WaitForStatus('Stopped', [TimeSpan]::FromMinutes(2))
                }
                foreach ($path in $directories) {
                    $backup = $path + '.WDigger-' + [guid]::NewGuid().ToString('N')
                    Move-Item -LiteralPath $path -Destination $backup -ErrorAction Stop
                    $renamed += @{ Original = $path; Backup = $backup }
                    Write-Output "Copia conservada: $backup"
                }
            } catch {
                $failure = $_.Exception.Message
                [array]::Reverse($renamed)
                foreach ($entry in $renamed) {
                    try {
                        if (Test-Path -LiteralPath $entry.Original) { throw "El destino ya existe: $($entry.Original)" }
                        Move-Item -LiteralPath $entry.Backup -Destination $entry.Original -ErrorAction Stop
                    } catch { $failure += "; no se pudo recuperar $($entry.Original): $($_.Exception.Message)" }
                }
            } finally {
                foreach ($name in @('cryptsvc','bits','wuauserv')) {
                    if ($running -contains $name) {
                        try {
                            $service = Get-Service -Name $name -ErrorAction Stop
                            if ($service.Status -ne 'Running') { Start-Service -Name $name -ErrorAction Stop }
                            (Get-Service -Name $name).WaitForStatus('Running', [TimeSpan]::FromMinutes(2))
                        } catch { $failure += "; no se pudo reiniciar $name : $($_.Exception.Message)" }
                    }
                }
            }
            if ($failure) { throw "Restablecimiento incompleto: $failure. Revisa servicios y carpetas WDigger." }
            Write-Output 'Servicios originalmente activos restaurados; las cachés anteriores se conservaron.'
            """.Replace("__WINDOWS__", windows.Replace("'", "''"));
        return context.RunPowerShellAsync(script, token);
    }

    private static async Task RunSfcAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var result = await context.RunArgumentsAsync("sfc", new[] { "/scannow" }, token);
        ExplainResult(context, result, "SFC");
    }

    private static async Task RunDismAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var result = await context.RunArgumentsAsync("DISM", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, token);
        ExplainResult(context, result, "DISM");
    }

    private static async Task RunChkdskAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var root = Path.GetPathRoot(Environment.SystemDirectory);
        if (string.IsNullOrEmpty(root) || root.Length != 3 || root[1] != ':')
            throw new InvalidOperationException("No se pudo determinar la unidad del sistema.");
        var result = await context.RunArgumentsAsync("chkdsk", new[] { root[..2], "/scan" }, token);
        ExplainResult(context, result, "CHKDSK");
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
        if (result.IsSimulated) return;
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
            "CHKDSK" when Regex.IsMatch(output, "No problems found|no errors", RegexOptions.IgnoreCase)
                => "No se encontraron errores en el sistema de archivos.",
            "CHKDSK" when Regex.IsMatch(output, "Correcting errors|errors found|unrecoverable", RegexOptions.IgnoreCase)
                => "Se detectaron o corrigieron errores. Revisa la salida; puede ser necesario reiniciar.",
            _ => "Resultado no interpretado con certeza. Revisa la salida de la herramienta; el registro no guarda toda la salida."
        };
        context.WriteLine($"Interpretación de {tool}: {explanation}",
            OutputKind.Warning);
    }
}
