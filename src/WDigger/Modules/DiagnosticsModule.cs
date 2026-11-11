using WDigger.Core;
using WDigger.Diagnostics;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class DiagnosticsModule : IModule
{
    public string Name => "Diagnóstico";
    public string Description => "Comprobaciones de lectura e informe. Las categorías sin cobertura se identifican como no disponibles.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Analizar todo", "Ejecuta las comprobaciones disponibles sin modificar Windows.", AnalyzeAsync),
        new ModuleAction("Exportar informe", "Guarda el último informe de diagnóstico en un archivo de texto.",
            ExportAsync, writesFile: true)
    };

    private static async Task AnalyzeAsync(ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Analizando comprobaciones de lectura...");
        var report = await context.Host.Diagnostics.AnalyzeAsync(token);
        token.ThrowIfCancellationRequested();
        context.Host.LatestDiagnosticReport = report;
        context.ShowTable("Diagnóstico", new[] { "Estado", "Categoría", "Comprobación", "Resultado", "Acción recomendada" },
            report.Results.Select(r => new[] { DiagnosticReport.StatusText(r.Status), r.Category, r.Title, r.Summary, r.Recommendation }));
        foreach (var result in report.Results)
            context.WriteLine($"[{DiagnosticReport.StatusText(result.Status)}] {result.Category} · {result.Title}: {result.Summary}",
                result.Status is DiagnosticStatus.Error or DiagnosticStatus.Failed ? OutputKind.Error
                    : result.Status is DiagnosticStatus.Warning or DiagnosticStatus.Unavailable or DiagnosticStatus.AccessDenied ? OutputKind.Warning
                    : OutputKind.Info);
        context.WriteLine("El informe incluye detalles y puede exportarse desde Diagnóstico. No se ha ejecutado ninguna reparación.", OutputKind.Info);
    }

    private static async Task ExportAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var report = context.Host.LatestDiagnosticReport;
        if (report is null)
        {
            context.WriteLine("Ejecuta primero un análisis para crear un informe.", OutputKind.Warning);
            context.SetOutcome(OperationOutcome.NotStarted("No hay un informe de diagnóstico para exportar."));
            return;
        }
        var path = await context.PickSaveFileAsync("WDigger-informe", ".txt");
        if (path is null) return;
        token.ThrowIfCancellationRequested();
        var supportReport = SupportReportExporter.Create(report, context.Host.Operations.GetRecords(20));
        await File.WriteAllTextAsync(path, supportReport, token);
        context.WriteLine("Informe de diagnóstico y resumen de operaciones guardado. Revísalo antes de compartirlo.", OutputKind.Info);
    }
}
