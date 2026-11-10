using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class TweaksModule : IModule
{
    public string Name => "Ajustes y privacidad";
    public string Description => "Ajustes definidos en JSON, punto de restauración, historial y reversión.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Aplicar un ajuste",
            "Muestra una vista previa y aplica un ajuste de config/tweaks.json. Crea un punto de restauración si es reversible.",
            (context, token) => ApplyTweakAsync(host, context, token)),
        new ModuleAction(
            "Revertir un ajuste aplicado",
            "Ejecuta las acciones de reversión de un ajuste registrado previamente.",
            (context, token) => RollbackTweakAsync(host, context, token)),
        new ModuleAction(
            "Ver historial de ajustes",
            "Muestra los ajustes aplicados y su fecha. Las simulaciones no se guardan como cambios reales.",
            (context, token) => ShowHistory(host, context, token))
    };

    private static async Task ApplyTweakAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        if (host.Config.Tweaks.Count == 0)
        {
            context.WriteLine("No se encontraron ajustes en config/tweaks.json.", OutputKind.Warning);
            return;
        }

        var tweak = await context.ChooseAsync("Aplicar ajuste", "Selecciona un ajuste:", host.Config.Tweaks,
            item => $"{item.Name} · {item.Category} · {(item.Reversible ? "Reversible" : "No reversible")}");
        if (tweak is null)
        {
            return;
        }

        context.ShowTable($"Vista previa · {tweak.Name}", new[] { "Tipo", "Acción" },
            tweak.Actions.Select(action => new[] { action.Type, FormatAction(action) }));
        context.WriteLine(tweak.Description, OutputKind.Muted);
        if (!await context.ConfirmAsync("Aplicar ajuste", $"{tweak.Description}\n\n¿Aplicar «{tweak.Name}»?"))
        {
            return;
        }

        if (tweak.Reversible && !host.DryRun)
        {
            context.SetStatus("Creando punto de restauración...");
            var created = await Task.Run(() => host.RestorePoints.Create($"WDigger tweak: {tweak.Name}"), token);
            context.WriteLine(created
                ? "Punto de restauración creado."
                : "No se pudo crear el punto de restauración; se continuará según lo confirmado.",
                created ? OutputKind.Success : OutputKind.Warning);
        }

        for (var index = 0; index < tweak.Actions.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            await ExecuteActionAsync(context, tweak.Actions[index], token);
            context.ReportProgress((index + 1) * 100 / tweak.Actions.Count);
        }

        if (!host.DryRun)
        {
            host.TweakHistory.Record(tweak.Id, tweak.Name);
        }
        context.WriteLine(host.DryRun
            ? $"Ajuste «{tweak.Name}» simulado; no se guardó en el historial."
            : $"Ajuste «{tweak.Name}» aplicado y registrado.", OutputKind.Success);
    }

    private static async Task RollbackTweakAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var appliedIds = host.TweakHistory.Records
            .Select(record => record.TweakId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var applicable = host.Config.Tweaks
            .Where(tweak => appliedIds.Contains(tweak.Id) && tweak.Reversible && tweak.Rollback.Count > 0)
            .ToList();

        if (applicable.Count == 0)
        {
            context.WriteLine("No hay ajustes aplicados que se puedan revertir.", OutputKind.Warning);
            return;
        }

        var tweak = await context.ChooseAsync("Revertir ajuste", "Selecciona el ajuste que quieres revertir:",
            applicable, item => $"{item.Name} ({item.Id})");
        if (tweak is null)
        {
            return;
        }

        context.ShowTable($"Reversión · {tweak.Name}", new[] { "Tipo", "Acción" },
            tweak.Rollback.Select(action => new[] { action.Type, FormatAction(action) }));
        if (!await context.ConfirmAsync("Revertir ajuste", $"¿Ejecutar la reversión de «{tweak.Name}»?"))
        {
            return;
        }

        if (!host.DryRun)
        {
            var created = await Task.Run(() => host.RestorePoints.Create($"WDigger rollback: {tweak.Name}"), token);
            context.WriteLine(created ? "Punto de restauración creado." : "No se pudo crear el punto de restauración.",
                created ? OutputKind.Success : OutputKind.Warning);
        }

        foreach (var action in tweak.Rollback)
        {
            token.ThrowIfCancellationRequested();
            await ExecuteActionAsync(context, action, token);
        }

        if (!host.DryRun)
        {
            host.TweakHistory.Remove(tweak.Id);
        }
        context.WriteLine(host.DryRun ? "Reversión simulada." : "Ajuste revertido y retirado del historial.", OutputKind.Success);
    }

    private static Task ShowHistory(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = token;
        var records = host.TweakHistory.Records.OrderByDescending(record => record.AppliedAt).ToList();
        if (records.Count == 0)
        {
            context.WriteLine("Todavía no hay ajustes aplicados.", OutputKind.Warning);
            return Task.CompletedTask;
        }

        context.ShowTable("Historial de ajustes", new[] { "Id", "Nombre", "Aplicado" },
            records.Select(record => new[]
            {
                record.TweakId,
                record.TweakName,
                record.AppliedAt.ToString("yyyy-MM-dd HH:mm")
            }));
        return Task.CompletedTask;
    }

    private static Task<CommandResult> ExecuteActionAsync(
        ModuleExecutionContext context, TweakAction action, CancellationToken token)
        => context.RunCommandAsync("cmd", $"/c \"{EscapeForCmd(FormatAction(action))}\"", token);

    private static string FormatAction(TweakAction action)
        => action.Type.Equals("registry", StringComparison.OrdinalIgnoreCase)
            ? $"reg add \"{action.Hive}\\{action.Key}\" /v {action.Value} /t {MapKind(action.Kind)} /d {action.Data} /f"
            : action.Command;

    private static string MapKind(string kind) => kind switch
    {
        "DWord" => "REG_DWORD",
        "QWord" => "REG_QWORD",
        "String" => "REG_SZ",
        "ExpandString" => "REG_EXPAND_SZ",
        "Binary" => "REG_BINARY",
        _ => "REG_DWORD"
    };

    /// <summary>
    /// Escapes a command line for safe use inside cmd /c "...".
    /// </summary>
    private static string EscapeForCmd(string command)
    {
        // Replace each " with \" and each \ before a " with \\
        var result = command.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return result;
    }
}
