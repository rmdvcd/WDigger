using System.Text;
using WDigger.Core;
using WDigger.Models;

namespace WDigger.UI;

/// <summary>
/// UI helpers available to modules: output, status, progress, tabular results,
/// dialogs and command execution. Implemented by the WinUI shell. All dialog
/// helpers are asynchronous because ContentDialog cannot be shown
/// synchronously without deadlocking the dispatcher.
/// </summary>
public sealed class ModuleExecutionContext
{
    private readonly ShellWindow _shell;
    private readonly bool _simulate;
    private readonly bool _changesSystem;

    internal ModuleExecutionContext(ShellWindow shell, AppHost host, bool simulate, bool changesSystem)
    {
        _shell = shell;
        Host = host;
        _simulate = simulate;
        _changesSystem = changesSystem;
    }

    public AppHost Host { get; }
    public bool IsSimulation => _simulate;

    public void WriteLine(string message = "", OutputKind kind = OutputKind.Info)
        => _shell.AppendOutput(message, kind);

    public void SetStatus(string message) => _shell.SetStatus(message);

    public void ReportProgress(int? percent = null) => _shell.ReportProgress(percent);

    public void ShowTable(string title, IReadOnlyList<string> columns, IEnumerable<string[]> rows)
        => _shell.ShowTable(title, columns, rows);

    public Task<string?> PromptAsync(string title, string label, string defaultValue = "", bool allowEmpty = false)
        => _shell.Dialogs.PromptAsync(title, label, defaultValue, allowEmpty);

    public Task<T?> ChooseAsync<T>(string title, string label, IEnumerable<T> choices, Func<T, string> formatter)
        where T : class
        => _shell.Dialogs.ChooseAsync(title, label, choices, formatter);

    public Task<string?> PickFileAsync(string title, string filter, string? initialPath = null)
        => _shell.Dialogs.PickFileAsync(title, filter, initialPath);

    public Task<string?> PickSaveFileAsync(string suggestedName, string extension)
        => _shell.Dialogs.PickSaveFileAsync(suggestedName, extension);

    public Task<string?> PickFolderAsync(string description, string? initialPath = null)
        => _shell.Dialogs.PickFolderAsync(description, initialPath);

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (_simulate)
        {
            WriteLine($"[SIMULACIÓN] {title}", OutputKind.Warning);
            return true;
        }

        return await _shell.Dialogs.ConfirmAsync(title, message);
    }

    public Task<CommandResult> RunCommandAsync(string fileName, string arguments, CancellationToken cancellationToken)
        => RunCommandCoreAsync(fileName, arguments, null, cancellationToken);

    public Task<CommandResult> RunPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        // Encode a fixed script plus PowerShell-escaped data so Windows argument
        // parsing cannot terminate the -Command string unexpectedly.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; " + script));
        return RunArgumentsAsync("powershell", new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded }, cancellationToken);
    }

    /// <summary>For fixed inventory scripts only; remains a real read during simulation.</summary>
    public async Task<CommandResult> RunReadOnlyPowerShellAsync(string script, CancellationToken cancellationToken)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("$ErrorActionPreference = 'Stop'; " + script));
        var result = await Host.Commands.RunArgumentsAsync("powershell", new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded },
            cancellationToken: cancellationToken, simulate: false, enforceTimeout: true);
        if (!result.Success) throw new InvalidOperationException("La consulta de inventario falló; no se aplicarán cambios.");
        return result;
    }

    public Task<CommandResult> RunArgumentsAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        => RunCommandCoreAsync(fileName, "", arguments, cancellationToken);

    private async Task<CommandResult> RunCommandCoreAsync(string fileName, string arguments,
        IReadOnlyList<string>? argumentList, CancellationToken cancellationToken)
    {
        _shell.ShowActivity();
        var display = argumentList is null ? arguments : string.Join(" ", argumentList.Select(arg => $"[{arg}]"));
        WriteLine($"> {fileName} {display}".TrimEnd(), OutputKind.Command);
        SetStatus($"Ejecutando {fileName}...");
        ReportProgress();

        var result = argumentList is null
            ? await Host.Commands.RunAsync(fileName, arguments, onOutputLine: line => WriteLine(line),
                cancellationToken: cancellationToken, simulate: _simulate, enforceTimeout: !_changesSystem)
            : await Host.Commands.RunArgumentsAsync(fileName, argumentList, onOutputLine: line => WriteLine(line),
                cancellationToken: cancellationToken, simulate: _simulate, enforceTimeout: !_changesSystem);

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
            throw new InvalidOperationException($"El comando {fileName} falló con el código {result.ExitCode}. Los cambios previos pueden ser parciales.");
        return result;
    }
}
