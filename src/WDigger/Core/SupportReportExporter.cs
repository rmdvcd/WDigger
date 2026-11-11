using System.Text;
using WDigger.Diagnostics;

namespace WDigger.Core;

/// <summary>Creates a redacted, summary-first support report without command output.</summary>
public static class SupportReportExporter
{
    public static string Create(DiagnosticReport report, IReadOnlyList<OperationRecord> operations)
    {
        var text = new StringBuilder(report.ToPlainText());
        text.AppendLine("Operaciones recientes (journal local)");
        text.AppendLine("La presencia de un resultado correcto no certifica que Windows esté sano.");
        text.AppendLine();
        if (operations.Count == 0)
        {
            text.AppendLine("No hay operaciones persistentes disponibles.");
            return text.ToString();
        }

        foreach (var operation in operations)
        {
            text.AppendLine($"[{operation.Status}] {operation.ActionTitle}");
            text.AppendLine($"  Id: {operation.OperationId:N}");
            text.AppendLine($"  Inicio UTC: {operation.StartedAtUtc:O}");
            if (operation.CompletedAtUtc is { } completed) text.AppendLine($"  Fin UTC: {completed:O}");
            text.AppendLine($"  Riesgo: {operation.Risk}; simulación: {operation.IsSimulation}; elevado: {operation.IsElevated}.");
            text.AppendLine($"  Punto de restauración verificado: {operation.RestorePointCreated}.");
            text.AppendLine($"  Resultado: {operation.Summary}");
            if (!string.IsNullOrWhiteSpace(operation.ManualAction)) text.AppendLine($"  Acción manual: {operation.ManualAction}");
            foreach (var step in operation.Steps)
            {
                text.AppendLine($"    - [{step.Status}] {step.Name}: {step.Summary}" +
                    (step.ExitCode is { } code ? $" (código {code})" : string.Empty));
            }
            text.AppendLine();
        }
        return text.ToString();
    }
}
