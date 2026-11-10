using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace WDigger.Diagnostics;

/// <summary>Conservative read-only checks. An unavailable check is never reported as healthy.</summary>
public sealed class DiagnosticService
{
    public Task<DiagnosticReport> AnalyzeAsync(CancellationToken token = default)
        => Task.Run(() => Analyze(token), token);

    private static DiagnosticReport Analyze(CancellationToken token)
    {
        var started = DateTimeOffset.UtcNow;
        var results = new List<DiagnosticResult>();
        token.ThrowIfCancellationRequested();
        results.Add(OperatingSystem.IsWindows()
            ? new("os", "Sistema", "Versión de Windows", DiagnosticStatus.Information,
                "Windows detectado; la versión no determina la integridad del sistema.", RuntimeInformation.OSDescription,
                "Revisa integridad y actualizaciones con herramientas verificadas.")
            : new("os", "Sistema", "Versión de Windows", DiagnosticStatus.Unavailable,
                "Este análisis requiere Windows.", "El sistema actual no es Windows.", "Ejecuta el análisis en Windows."));

        token.ThrowIfCancellationRequested();
        try
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var root = Path.GetPathRoot(system);
            if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(root)) throw new IOException("No se pudo identificar el volumen del sistema.");
            var drive = new DriveInfo(root);
            results.Add(EvaluateSystemDisk(drive.TotalSize, drive.AvailableFreeSpace));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            results.Add(Unavailable("disk", "Disco", "Espacio del sistema", ex.Message));
        }

        token.ThrowIfCancellationRequested();
        try
        {
            var count = NetworkInterface.GetAllNetworkInterfaces().Count(item =>
                item.NetworkInterfaceType != NetworkInterfaceType.Loopback && item.OperationalStatus == OperationalStatus.Up);
            results.Add(count > 0
                ? new("network", "Red", "Adaptadores activos", DiagnosticStatus.Information,
                    $"Hay {count} adaptador(es) activo(s); esto no confirma acceso a Internet o DNS.",
                    "Estado local de adaptadores; sin conexión a servicios externos.", "Comprueba DNS y conectividad si experimentas problemas.")
                : new("network", "Red", "Adaptadores activos", DiagnosticStatus.Warning,
                    "No se detectó ningún adaptador activo.", "La conexión puede estar deshabilitada o no disponible.", "Revisa el adaptador y su conexión."));
        }
        catch (Exception ex) when (ex is NetworkInformationException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            results.Add(Unavailable("network", "Red", "Adaptadores activos", ex.Message));
        }

        foreach (var (id, category, title) in new[]
        {
            ("integrity", "Sistema", "Integridad de archivos"),
            ("image", "Sistema", "Imagen de Windows"),
            ("filesystem", "Disco", "Sistema de archivos"),
            ("boot", "Arranque", "Configuración de arranque"),
            ("drivers", "Controladores", "Errores de controladores"),
            ("registry", "Registro", "Servicios y claves de inicio"),
            ("performance", "Rendimiento", "Rendimiento histórico"),
            ("defender", "Seguridad", "Defender y firewall"),
            ("events", "Eventos", "Eventos críticos recientes")
        })
        {
            token.ThrowIfCancellationRequested();
            results.Add(Unavailable(id, category, title, "Comprobación aún no implementada o validada para ejecución sin elevación."));
        }
        return new DiagnosticReport(started, DateTimeOffset.UtcNow, results
            .OrderByDescending(item => Severity(item.Status)).ThenBy(item => item.Category).ToArray());
    }

    public static DiagnosticResult EvaluateSystemDisk(long totalBytes, long freeBytes)
    {
        if (totalBytes <= 0 || freeBytes < 0 || freeBytes > totalBytes)
            return Unavailable("disk", "Disco", "Espacio del sistema", "Los datos de capacidad no son válidos.");
        var percent = freeBytes * 100.0 / totalBytes;
        var status = percent < 5 ? DiagnosticStatus.Error : percent < 10 ? DiagnosticStatus.Warning : DiagnosticStatus.Ok;
        return new("disk", "Disco", "Espacio del sistema", status,
            $"Espacio libre: {percent:F1} %.", $"{freeBytes / 1073741824.0:F1} GiB libres de {totalBytes / 1073741824.0:F1} GiB.",
            status == DiagnosticStatus.Ok ? "Supervisa el espacio disponible." : "Revisa los archivos y haz una copia de seguridad antes de liberar espacio.");
    }

    private static int Severity(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Error => 4, DiagnosticStatus.Warning => 3, DiagnosticStatus.Unavailable => 2,
        DiagnosticStatus.Information => 1, _ => 0
    };

    private static DiagnosticResult Unavailable(string id, string category, string title, string reason)
        => new(id, category, title, DiagnosticStatus.Unavailable, "No se puede determinar el estado.", reason,
            "Consulta un técnico o utiliza una herramienta de diagnóstico apropiada.");
}
