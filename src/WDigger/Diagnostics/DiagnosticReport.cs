using System.Text;

namespace WDigger.Diagnostics;

public enum DiagnosticStatus
{
    Ok,
    Warning,
    Error,
    Information,
    Unavailable,
    AccessDenied,
    NotApplicable,
    Failed
}

/// <summary>One read-only check with stable identity and optional linked repair action.</summary>
public sealed record DiagnosticResult(
    string Id,
    string Category,
    string Title,
    DiagnosticStatus Status,
    string Summary,
    string Details,
    string Recommendation,
    string? RecommendedActionId = null);

public sealed record DiagnosticReport(DateTimeOffset StartedAtUtc, DateTimeOffset CompletedAtUtc,
    IReadOnlyList<DiagnosticResult> Results)
{
    public static string StatusText(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Ok => "OK",
        DiagnosticStatus.Warning => "Advertencia",
        DiagnosticStatus.Error => "Error",
        DiagnosticStatus.Information => "Información",
        DiagnosticStatus.AccessDenied => "Permiso denegado",
        DiagnosticStatus.NotApplicable => "No aplicable",
        DiagnosticStatus.Failed => "Error de comprobación",
        _ => "No disponible"
    };

    public string ToPlainText()
    {
        var text = new StringBuilder();
        text.AppendLine("WDigger — informe de diagnóstico (solo lectura)");
        text.AppendLine($"Inicio UTC: {StartedAtUtc:O}");
        text.AppendLine($"Fin UTC: {CompletedAtUtc:O}");
        text.AppendLine("Las comprobaciones no disponibles, con error o con permiso denegado no indican que el sistema esté sano.");
        text.AppendLine("No se ha ejecutado ninguna reparación con este análisis.");
        text.AppendLine();
        foreach (var item in Results)
        {
            text.AppendLine($"[{StatusText(item.Status)}] {item.Category} — {item.Title}");
            text.AppendLine($"  Resultado: {item.Summary}");
            text.AppendLine($"  Detalles: {item.Details}");
            text.AppendLine($"  Recomendación: {item.Recommendation}");
            if (!string.IsNullOrWhiteSpace(item.RecommendedActionId)) text.AppendLine($"  Acción asociada: {item.RecommendedActionId}");
            text.AppendLine();
        }
        return text.ToString();
    }
}
