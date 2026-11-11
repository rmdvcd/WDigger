using WDigger.Core;
using WDigger.Models;

namespace WDigger.UI;

/// <summary>
/// UI helpers available to modules. The context also collects a typed outcome
/// and journal steps so a normal return is never blindly presented as success.
/// </summary>
public sealed class ModuleExecutionContext
{
    private readonly ShellWindow _shell;
    private readonly bool _simulate;
    private readonly TimeSpan _commandTimeout;
    private readonly OperationSession? _session;
    private OperationOutcome? _outcome;

    internal ModuleExecutionContext(ShellWindow shell, AppHost host, bool simulate, bool changesSystem,
        TimeSpan commandTimeout, OperationSession? session)
    {
        _shell = shell;
        Host = host;
        _simulate = simulate;
        ChangesSystem = changesSystem;
        _commandTimeout = commandTimeout;
        _session = session;
    }

    public AppHost Host { get; }
    public bool IsSimulation => _simulate;
    public bool ChangesSystem { get; }

    public void WriteLine(string message = "", OutputKind kind = OutputKind.Info)
        => _shell.AppendOutput(message, kind);

    public void SetStatus(string message) => _shell.SetStatus(message);

    public void ReportProgress(int? percent = null) => _shell.ReportProgress(percent);

    public void ShowTable(string title, IReadOnlyList<string> columns, IEnumerable<string[]> rows)
        => _shell.ShowTable(title, columns, rows);

    public async Task<string?> PromptAsync(string title, string label, string defaultValue = "", bool allowEmpty = false)
    {
        var value = await _shell.Dialogs.PromptAsync(title, label, defaultValue, allowEmpty);
        if (value is null) MarkNotStarted("El usuario canceló la solicitud de datos.");
        return value;
    }

    public async Task<T?> ChooseAsync<T>(string title, string label, IEnumerable<T> choices, Func<T, string> formatter)
        where T : class
    {
        var value = await _shell.Dialogs.ChooseAsync(title, label, choices, formatter);
        if (value is null) MarkNotStarted("El usuario canceló la selección.");
        return value;
    }

    public async Task<string?> PickFileAsync(string title, string filter, string? initialPath = null)
    {
        var value = await _shell.Dialogs.PickFileAsync(title, filter, initialPath);
        if (value is null) MarkNotStarted("El usuario canceló la selección de archivo.");
        return value;
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension)
    {
        var value = await _shell.Dialogs.PickSaveFileAsync(suggestedName, extension);
        if (value is null) MarkNotStarted("El usuario canceló la selección de destino.");
        return value;
    }

    public async Task<string?> PickFolderAsync(string description, string? initialPath = null)
    {
        var value = await _shell.Dialogs.PickFolderAsync(description, initialPath);
        if (value is null) MarkNotStarted("El usuario canceló la selección de carpeta.");
        return value;
    }

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_simulate)
        {
            WriteLine($"[SIMULACIÓN] {title}", OutputKind.Warning);
            return true;
        }

        var confirmed = await _shell.Dialogs.ConfirmAsync(title, message);
        if (!confirmed) MarkNotStarted("El usuario rechazó la confirmación requerida.");
        return confirmed;
    }

    public void SetOutcome(OperationOutcome outcome) => _outcome = outcome;

    /// <summary>Records a non-command phase such as validated local file or registry work.</summary>
    public void StartStep(string name, string summary) => _session?.StartStep(name, summary);

    public void CompleteStep(OperationStatus status, string summary) => _session?.CompleteLatestStep(status, summary);

    /// <summary>Fails closed before a local mutation if the operation journal was lost.</summary>
    public void EnsurePersistentJournal()
    {
        if (ChangesSystem && !_simulate && (_session is null || !_session.IsPersistent))
        {
            throw new InvalidOperationException("El journal persistente dejó de estar disponible; no se iniciarán más cambios.");
        }
    }

    public void RequireManualAction(string summary, string manualAction)
        => SetOutcome(_simulate ? OperationOutcome.Simulated(summary) :
            new OperationOutcome(OperationStatus.ManualActionRequired, summary, false, manualAction));

    public void RequireRestart(string summary)
        => SetOutcome(_simulate ? OperationOutcome.Simulated(summary) : OperationOutcome.Succeeded(summary, restartRequired: true));

    internal OperationOutcome ResolveOutcome()
        => _outcome ?? (_simulate ? OperationOutcome.Simulated() : OperationOutcome.Succeeded());

    public Task<CommandResult> RunCommandAsync(CommandSpec specification, CancellationToken cancellationToken)
        => RunCommandCoreAsync(specification, cancellationToken, forceRealRead: false);

    /// <summary>
    /// Invokes one of the embedded, reviewed PowerShell scripts. Modules cannot
    /// pass PowerShell source text through this API.
    /// </summary>
    public Task<CommandResult> RunPowerShellAsync(PowerShellScriptId script, IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken)
        => RunCommandCoreAsync(PowerShellScriptCatalog.Create(script, arguments,
            ChangesSystem ? CommandEffect.ModifiesSystem : CommandEffect.ReadOnly), cancellationToken, forceRealRead: false);

    /// <summary>Runs a reviewed inventory script even while a surrounding repair is simulated.</summary>
    public Task<CommandResult> RunReadOnlyPowerShellAsync(PowerShellScriptId script, IReadOnlyList<string>? arguments,
        CancellationToken cancellationToken)
        => RunCommandCoreAsync(PowerShellScriptCatalog.Create(script, arguments, CommandEffect.ReadOnly),
            cancellationToken, forceRealRead: true);

    private async Task<CommandResult> RunCommandCoreAsync(CommandSpec specification, CancellationToken cancellationToken,
        bool forceRealRead)
    {
        _shell.ShowActivity();
        WriteLine($"> {specification.Tool} ({specification.Arguments.Count} argumento(s))", OutputKind.Command);
        SetStatus($"Ejecutando {ToolDisplayName(specification.Tool)}...");
        ReportProgress();
        StartCommandStep(specification.Tool.ToString(), $"Comando iniciado: {specification.Tool}.");
        EnsurePersistentJournal();

        CommandResult result;
        try
        {
            result = await Host.Commands.RunAsync(specification, line => WriteLine(line), cancellationToken,
                simulate: forceRealRead ? false : _simulate, enforceTimeout: true, timeoutOverride: _commandTimeout);
        }
        catch (TimeoutException exception)
        {
            _session?.CompleteLatestStep(OperationStatus.Failed, "El comando superó el tiempo de espera.");
            throw new TimeoutException(exception.Message, exception);
        }
        catch (OperationCanceledException)
        {
            _session?.CompleteLatestStep(OperationStatus.Cancelled, "El comando fue cancelado.");
            throw;
        }
        catch (Exception exception)
        {
            _session?.CompleteLatestStep(OperationStatus.Failed, $"No se pudo iniciar el comando: {exception.Message}");
            throw;
        }

        CompleteCommandStep(result);
        if (!string.IsNullOrWhiteSpace(result.StandardError)) WriteLine(result.StandardError.TrimEnd(), OutputKind.Error);
        if (result.IsSimulated)
        {
            WriteLine("Simulado: el comando no se ejecutó.", OutputKind.Warning);
            return result;
        }
        WriteLine(result.Success
            ? $"Completado en {result.Duration.TotalSeconds:F1} s (código {result.ExitCode})."
            : $"Finalizó con errores en {result.Duration.TotalSeconds:F1} s (código {result.ExitCode}).",
            result.Success ? OutputKind.Info : OutputKind.Error);
        if (!result.Success)
        {
            throw new InvalidOperationException($"{ToolDisplayName(specification.Tool)} falló con el código {result.ExitCode}. Los cambios previos pueden ser parciales.");
        }
        return result;
    }

    private void StartCommandStep(string name, string summary) => _session?.StartStep(name, summary);

    private void CompleteCommandStep(CommandResult result)
    {
        var status = result.IsSimulated
            ? OperationStatus.Simulated
            : result.Success ? OperationStatus.Succeeded : OperationStatus.Failed;
        _session?.CompleteLatestStep(status,
            result.IsSimulated ? "Comando simulado." : $"Comando finalizó con código {result.ExitCode}.",
            result.IsSimulated ? null : result.ExitCode);
    }

    private void MarkNotStarted(string summary)
    {
        // Pickers and confirmations occur before their corresponding mutation.
        // A restore-point or read-only preflight step must not turn a declined
        // input into a generic successful operation.
        if (_outcome is null)
        {
            _outcome = OperationOutcome.NotStarted(summary);
        }
    }

    private static string ToolDisplayName(WindowsTool tool) => tool switch
    {
        WindowsTool.Dism => "DISM",
        WindowsTool.PnPUtil => "PnPUtil",
        WindowsTool.Sfc => "SFC",
        _ => tool.ToString()
    };
}
