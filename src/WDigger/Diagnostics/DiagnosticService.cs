using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using WDigger.Core;

namespace WDigger.Diagnostics;

/// <summary>
/// Conservative, read-only diagnostic catalog. A command completing with exit
/// code zero means its check ran; it is never silently converted into a claim
/// that Windows is healthy when output cannot be interpreted reliably.
/// </summary>
public sealed class DiagnosticService
{
    private readonly CommandExecutor? _commands;

    public DiagnosticService(CommandExecutor? commands = null) => _commands = commands;

    public async Task<DiagnosticReport> AnalyzeAsync(CancellationToken token = default)
    {
        var started = DateTimeOffset.UtcNow;
        var results = new List<DiagnosticResult>();
        token.ThrowIfCancellationRequested();
        results.Add(OperatingSystem.IsWindows()
            ? new DiagnosticResult("os", "Sistema", "Versión de Windows", DiagnosticStatus.Information,
                "Windows detectado; la versión no determina la integridad del sistema.", RuntimeInformation.OSDescription,
                "Revisa los hallazgos de integridad, imagen y actualizaciones antes de reparar.")
            : new DiagnosticResult("os", "Sistema", "Versión de Windows", DiagnosticStatus.NotApplicable,
                "Este análisis requiere Windows.", "El sistema actual no es Windows.", "Ejecuta el análisis en Windows."));

        token.ThrowIfCancellationRequested();
        results.Add(EvaluateSystemDiskSafe());
        results.Add(EvaluateNetworkAdapters());
        results.Add(EvaluateFileSystem());

        await AddCommandCheckAsync(results, "integrity", "Sistema", "Integridad de la imagen", "DISM CheckHealth",
            CommandSpec.Read(WindowsTool.Dism, "/Online", "/Cleanup-Image", "/CheckHealth"), token,
            "La comprobación DISM terminó; revisa la salida si necesitas interpretación localizada.",
            "Si existe daño confirmado, considera DISM /RestoreHealth desde Reparación del sistema.", "repair-dism");
        await AddCommandCheckAsync(results, "image", "Sistema", "Daño recuperable de la imagen", "DISM ScanHealth",
            new CommandSpec(WindowsTool.Dism, new[] { "/Online", "/Cleanup-Image", "/ScanHealth" }, CommandEffect.ReadOnly,
                TimeSpan.FromMinutes(30)), token,
            "El análisis profundo DISM terminó; WDigger no deduce salud a partir de texto localizado.",
            "Si DISM informa daño, revisa la evidencia y considera DISM /RestoreHealth.", "repair-dism");
        await AddCommandCheckAsync(results, "boot", "Arranque", "Configuración de arranque", "BCDEdit",
            CommandSpec.Read(WindowsTool.BcdEdit, "/enum", "{current}"), token,
            "BCDEdit pudo consultar la entrada de arranque actual.",
            "Revisa los detalles técnicos; no modifiques BCD sin una copia y un plan de recuperación.");
        await AddCommandCheckAsync(results, "drivers", "Controladores", "Dispositivos con problemas", "PnPUtil",
            CommandSpec.Read(WindowsTool.PnPUtil, "/enum-devices", "/problem"), token,
            "PnPUtil pudo consultar dispositivos con problemas; revisa la salida para identificar dispositivos concretos.",
            "No instales controladores solo por este inventario; verifica proveedor, firma y compatibilidad.");
        results.Add(Unavailable("registry", "Registro", "Servicios y claves de inicio",
            "No existe todavía una comprobación de registro con criterio seguro y validado.",
            "No modifiques el registro basándote en una comprobación no implementada."));
        results.Add(EvaluatePerformance());
        await AddCommandCheckAsync(results, "security", "Seguridad", "Perfiles de firewall", "Netsh firewall",
            CommandSpec.Read(WindowsTool.Netsh, "advfirewall", "show", "allprofiles"), token,
            "Netsh pudo consultar los perfiles de firewall; revisa los detalles de la salida antes de cambiar reglas.",
            "Consulta con el administrador de seguridad antes de modificar el firewall.");
        await AddEventCheckAsync(results, token);

        return new DiagnosticReport(started, DateTimeOffset.UtcNow, results
            .OrderByDescending(item => Severity(item.Status)).ThenBy(item => item.Category).ThenBy(item => item.Title).ToArray());
    }

    public static DiagnosticResult EvaluateSystemDisk(long totalBytes, long freeBytes)
    {
        if (totalBytes <= 0 || freeBytes < 0 || freeBytes > totalBytes)
            return Unavailable("disk", "Disco", "Espacio del sistema", "Los datos de capacidad no son válidos.",
                "Comprueba el volumen del sistema antes de liberar espacio.");
        var percent = freeBytes * 100.0 / totalBytes;
        var status = percent < 5 ? DiagnosticStatus.Error : percent < 10 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
        return new DiagnosticResult("disk", "Disco", "Espacio del sistema", status,
            $"Espacio libre: {percent:F1} %.", $"{freeBytes / 1073741824.0:F1} GiB libres de {totalBytes / 1073741824.0:F1} GiB.",
            status == DiagnosticStatus.Ok ? "Supervisa el espacio disponible." : "Haz una copia de seguridad y libera espacio antes de ejecutar reparaciones.");
    }

    private static DiagnosticResult EvaluateSystemDiskSafe()
    {
        try
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var root = Path.GetPathRoot(system);
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(root)) throw new IOException("No se pudo identificar el volumen del sistema.");
            var drive = new DriveInfo(root);
            return EvaluateSystemDisk(drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unavailable("disk", "Disco", "Espacio del sistema", exception.Message,
                "Comprueba el volumen del sistema antes de liberar espacio.");
        }
    }

    private static DiagnosticResult EvaluateNetworkAdapters()
    {
        try
        {
            var count = NetworkInterface.GetAllNetworkInterfaces().Count(item =>
                item.NetworkInterfaceType != NetworkInterfaceType.Loopback && item.OperationalStatus == OperationalStatus.Up);
            return count > 0
                ? new DiagnosticResult("network", "Red", "Adaptadores activos", DiagnosticStatus.Information,
                    $"Hay {count} adaptador(es) activo(s); esto no confirma acceso a Internet o DNS.",
                    "Estado local de adaptadores; sin conexión a servicios externos.", "Comprueba DNS y conectividad si experimentas problemas.")
                : new DiagnosticResult("network", "Red", "Adaptadores activos", DiagnosticStatus.Warning,
                    "No se detectó ningún adaptador activo.", "La conexión puede estar deshabilitada o no disponible.", "Revisa el adaptador y su conexión.");
        }
        catch (Exception exception) when (exception is NetworkInformationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return Unavailable("network", "Red", "Adaptadores activos", exception.Message,
                "Comprueba la disponibilidad del adaptador y los permisos.");
        }
    }

    private static DiagnosticResult EvaluateFileSystem()
    {
        try
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(root)) throw new IOException("No se pudo identificar el volumen del sistema.");
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return new DiagnosticResult("filesystem", "Disco", "Volumen del sistema", DiagnosticStatus.Error,
                "El volumen del sistema no está listo.", "DriveInfo informó que el volumen no está disponible.",
                "No ejecutes reparaciones hasta recuperar acceso estable al volumen.");
            return new DiagnosticResult("filesystem", "Disco", "Volumen del sistema", DiagnosticStatus.Information,
                $"El volumen {drive.Name} está disponible con formato {drive.DriveFormat}.",
                "Esta comprobación no analiza sectores, estructura NTFS ni errores de archivos.",
                "Ejecuta CHKDSK /scan solo después de revisar su impacto.", "repair-chkdsk");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Unavailable("filesystem", "Disco", "Volumen del sistema", exception.Message,
                "Comprueba el volumen del sistema antes de ejecutar CHKDSK.");
        }
    }

    private static DiagnosticResult EvaluatePerformance()
    {
        var disk = EvaluateSystemDiskSafe();
        return disk.Status switch
        {
            DiagnosticStatus.Error or DiagnosticStatus.Warning => new DiagnosticResult("performance", "Rendimiento", "Capacidad para operaciones", disk.Status,
                "El espacio libre del volumen del sistema puede afectar a reparaciones y actualizaciones.", disk.Details,
                "Libera espacio y realiza una copia de seguridad antes de continuar."),
            DiagnosticStatus.Ok => new DiagnosticResult("performance", "Rendimiento", "Capacidad para operaciones", DiagnosticStatus.Information,
                "El espacio libre actual no activa un umbral de advertencia.", "No se recopilaron métricas históricas ni telemetría.",
                "Supervisa rendimiento con herramientas apropiadas si el problema persiste."),
            _ => Unavailable("performance", "Rendimiento", "Capacidad para operaciones", disk.Details,
                "No se puede valorar capacidad sin información fiable del volumen.")
        };
    }

    private async Task AddCommandCheckAsync(List<DiagnosticResult> results, string id, string category, string title,
        string toolName, CommandSpec specification, CancellationToken token, string successSummary, string recommendation,
        string? actionId = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            results.Add(new DiagnosticResult(id, category, title, DiagnosticStatus.NotApplicable,
                "Esta comprobación requiere Windows.", "El sistema actual no es Windows.", recommendation, actionId));
            return;
        }
        if (_commands is null)
        {
            results.Add(Unavailable(id, category, title, "No hay un ejecutor de diagnóstico disponible.", recommendation, actionId));
            return;
        }

        try
        {
            var result = await _commands.RunAsync(specification, cancellationToken: token, simulate: false);
            if (result.Success)
            {
                results.Add(new DiagnosticResult(id, category, title, DiagnosticStatus.Information,
                    successSummary, $"{toolName} finalizó con código {result.ExitCode}. La salida completa no se incorpora al informe.", recommendation, actionId));
                return;
            }
            results.Add(new DiagnosticResult(id, category, title, ClassifyCommandFailure(result.StandardError),
                $"{toolName} terminó con código {result.ExitCode}.", result.StandardError.Length == 0 ? "No se obtuvo detalle de error." : "La herramienta devolvió un error; revisa el journal y la salida visible.",
                recommendation, actionId));
        }
        catch (TimeoutException exception)
        {
            results.Add(new DiagnosticResult(id, category, title, DiagnosticStatus.Failed,
                $"{toolName} superó el tiempo de espera.", exception.Message, recommendation, actionId));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            results.Add(new DiagnosticResult(id, category, title, DiagnosticStatus.Failed,
                $"No se pudo ejecutar {toolName}.", exception.Message, recommendation, actionId));
        }
    }

    private async Task AddEventCheckAsync(List<DiagnosticResult> results, CancellationToken token)
    {
        const string query = "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) <= 604800000]]]";
        if (!OperatingSystem.IsWindows() || _commands is null)
        {
            results.Add(Unavailable("events", "Eventos", "Eventos críticos recientes",
                "No hay un ejecutor de eventos disponible en este entorno.", "Consulta los eventos críticos recientes en Windows."));
            return;
        }
        try
        {
            var result = await _commands.RunAsync(new CommandSpec(WindowsTool.Wevtutil,
                new[] { "qe", "System", $"/q:{query}", "/c:20", "/f:xml", "/rd:true" }, CommandEffect.ReadOnly,
                TimeSpan.FromMinutes(2)), cancellationToken: token, simulate: false);
            if (!result.Success)
            {
                results.Add(new DiagnosticResult("events", "Eventos", "Eventos críticos recientes", ClassifyCommandFailure(result.StandardError),
                    $"Wevtutil terminó con código {result.ExitCode}.", "No se pudo consultar el registro System.",
                    "Abre Visor de eventos y revisa permisos y disponibilidad del registro."));
                return;
            }
            var count = result.StandardOutput.Split("<Event ", StringSplitOptions.None).Length - 1;
            results.Add(count > 0
                ? new DiagnosticResult("events", "Eventos", "Eventos críticos recientes", DiagnosticStatus.Warning,
                    $"Se encontraron hasta {count} evento(s) crítico(s) o de error en los últimos 7 días.",
                    "Los eventos requieren correlación con hora, componente y síntomas; no prueban una causa por sí solos.",
                    "Revisa los eventos relevantes antes de ejecutar una reparación.")
                : new DiagnosticResult("events", "Eventos", "Eventos críticos recientes", DiagnosticStatus.Information,
                    "No se devolvieron eventos de nivel crítico o error en la ventana consultada.",
                    "La consulta está limitada a 20 eventos System de los últimos 7 días.",
                    "Si el problema persiste, revisa Application y registros específicos."));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            results.Add(new DiagnosticResult("events", "Eventos", "Eventos críticos recientes", DiagnosticStatus.Failed,
                "No se pudo consultar el registro System.", exception.Message,
                "Abre Visor de eventos y revisa permisos y disponibilidad del registro."));
        }
    }

    private static DiagnosticStatus ClassifyCommandFailure(string error)
        => error.Contains("access", StringComparison.OrdinalIgnoreCase) || error.Contains("acceso", StringComparison.OrdinalIgnoreCase)
            ? DiagnosticStatus.AccessDenied : DiagnosticStatus.Failed;

    private static int Severity(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Error => 6,
        DiagnosticStatus.Warning => 5,
        DiagnosticStatus.Failed => 4,
        DiagnosticStatus.AccessDenied => 3,
        DiagnosticStatus.Unavailable => 2,
        DiagnosticStatus.Information => 1,
        _ => 0
    };

    private static DiagnosticResult Unavailable(string id, string category, string title, string reason,
        string recommendation, string? actionId = null)
        => new(id, category, title, DiagnosticStatus.Unavailable, "No se puede determinar el estado.", reason, recommendation, actionId);
}
