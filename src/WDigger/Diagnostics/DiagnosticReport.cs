using System.Text;

namespace WDigger.Diagnostics;

public enum DiagnosticStatus { Ok, Warning, Error, Information, Unavailable }

public sealed record DiagnosticResult(string Id, string Category, string Title, DiagnosticStatus Status,
    string Summary, string Details, string Recommendation);

public sealed record DiagnosticReport(DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    IReadOnlyList<DiagnosticResult> Results)
{
    public static string StatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.Warning => "Advertencia",
        DiagnosticStatus.Error => "Error",
        DiagnosticStatus.Information => "Información",
        _ => "No disponible"
    };

    public string ToPlainText()
    {
        var text = new StringBuilder();
        text.AppendLine("WDigger — informe de diagnóstico (solo lectura)");
        text.AppendLine($"Inicio UTC: {StartedAtUtc:O}");
        text.AppendLine($"Fin UTC: {CompletedAtUtc:O}");
        text.AppendLine("Las comprobaciones no disponibles no indican que el sistema esté sano.");
        text.AppendLine("No se ha ejecutado ninguna reparación con este análisis.");
        text.AppendLine();
        foreach (var item in Results)
        {
            text.AppendLine($"[{StatusText(item.Status)}] {item.Category} — {item.Title}");
            text.AppendLine($"  Resultado: {item.Summary}");
            text.AppendLine($"  Detalles: {item.Details}");
            text.AppendLine($"  Recomendación: {item.Recommendation}");
            text.AppendLine();
        }
        return text.ToString();
    }
}
