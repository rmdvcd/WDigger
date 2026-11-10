using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class TweaksModule : IModule
{
    public string Name => "Ajustes y privacidad";
    public string Description => "Historial de ajustes; aplicación y reversión suspendidas por seguridad.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Aplicar un ajuste", "Los ajustes configurables pueden modificar Windows.",
            (_, _) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Suspendida hasta sustituir comandos arbitrarios y guardar el estado anterior."),
        new ModuleAction("Revertir un ajuste aplicado", "Los valores anteriores no se guardaban en el historial.",
            (_, _) => Task.CompletedTask, changesSystem: true,
            unavailableReason: "Suspendida: los valores predeterminados no garantizan recuperar el estado anterior."),
        new ModuleAction("Ver historial de ajustes", "Muestra el historial disponible de ejecuciones anteriores.",
            (context, token) => ShowHistory(host, context, token))
    };

    private static Task ShowHistory(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var records = host.TweakHistory.Records.OrderByDescending(record => record.AppliedAt).ToList();
        if (records.Count == 0)
        {
            context.WriteLine("No hay historial de ajustes disponible.", OutputKind.Warning);
            return Task.CompletedTask;
        }
        context.WriteLine("El historial anterior no demuestra que los ajustes se aplicaran correctamente ni contiene el estado previo.", OutputKind.Warning);
        context.ShowTable("Historial de ajustes", new[] { "Id", "Nombre", "Aplicado" },
            records.Select(record => new[] { record.TweakId, record.TweakName, record.AppliedAt.ToString("yyyy-MM-dd HH:mm") }));
        return Task.CompletedTask;
    }
}
