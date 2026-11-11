using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class TweaksModule : IModule
{
    public string Name => "Ajustes y privacidad";
    public string Description => "Ajustes de registro predefinidos con copia verificable del valor anterior; no se ejecutan comandos ni JSON editables.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Aplicar un ajuste de registro", "Solo ajustes predefinidos; guarda el valor previo en HKLM antes de modificarlo.",
            (context, token) => ChangeAsync(context, token, revert: false), changesSystem: true,
            confirmation: "Las directivas pueden estar administradas por tu organización. La copia previa se guarda en el registro de este equipo.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Moderate,
            recovery: OperationRecovery.SnapshotAndVerifiedRevert),
        new ModuleAction("Revertir un ajuste de registro", "Restaura la copia anterior creada por esta versión, si el valor no ha cambiado externamente.",
            (context, token) => ChangeAsync(context, token, revert: true), changesSystem: true,
            confirmation: "Solo se revierten ajustes con copia válida. La reversión conserva la copia si no se puede verificar.",
            canCancel: false, allowedInElevatedSession: true, risk: OperationRisk.Moderate,
            recovery: OperationRecovery.SnapshotAndVerifiedRevert)
    };

    private static async Task ChangeAsync(ModuleExecutionContext context, CancellationToken token, bool revert)
    {
        var item = await context.ChooseAsync(revert ? "Revertir ajuste" : "Aplicar ajuste",
            "Selecciona un ajuste predefinido:", RegistryTweakService.Catalog, entry => entry.Name);
        if (item is null) return;
        if (!await context.ConfirmAsync(revert ? "Confirmar reversión" : "Confirmar ajuste",
            $"{(revert ? "Se intentará restaurar el valor previo de" : "Se establecerá a 0 el valor de")} «{item.Name}» en HKLM\\{item.Key}\\{item.Value}. ¿Continuar?")) return;
        token.ThrowIfCancellationRequested();
        if (context.IsSimulation)
        {
            context.WriteLine($"[SIMULACIÓN] No se {(revert ? "restauró" : "modificó")} el registro ni se creó copia previa.", OutputKind.Warning);
            return;
        }
        var service = new RegistryTweakService();
        context.StartStep(revert ? "registry-revert" : "registry-apply", $"Preparando el ajuste fijo {item.Id}.");
        context.EnsurePersistentJournal();
        try
        {
            if (revert) service.Revert(item.Id); else service.Apply(item.Id);
            context.CompleteStep(OperationStatus.Succeeded, revert ? "Reversión de registro verificada." : "Ajuste de registro verificado.");
        }
        catch
        {
            context.CompleteStep(OperationStatus.Failed, "El ajuste de registro no terminó de forma verificable.");
            throw;
        }
        context.Host.Logger.Info($"Registry tweak {(revert ? "reverted" : "applied")}: {item.Id}");
        context.WriteLine(revert ? "Valor anterior restaurado y verificado." : "Valor actualizado y verificado; copia previa guardada en HKLM.", OutputKind.Info);
    }
}
