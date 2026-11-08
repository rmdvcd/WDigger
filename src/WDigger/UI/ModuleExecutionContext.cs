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

    internal ModuleExecutionContext(ShellWindow shell, AppHost host)
    {
        _shell = shell;
        Host = host;
    }

    public AppHost Host { get; }

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

    public Task<string?> PickFolderAsync(string description, string? initialPath = null)
        => _shell.Dialogs.PickFolderAsync(description, initialPath);

    public async Task<bool> ConfirmAsync(string title, string message)
    {
        if (Host.DryRun)
        {
            WriteLine($"[SIMULACIÓN] {title}", OutputKind.Warning);
            return true;
        }

        return await _shell.Dialogs.ConfirmAsync(title, message);
    }

    public async Task<CommandResult> RunCommandAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        _shell.ShowActivity();
        WriteLine($"> {fileName} {arguments}".TrimEnd(), OutputKind.Command);
        SetStatus($"Ejecutando {fileName}...");
        ReportProgress();

        var result = await Host.Commands.RunAsync(
            fileName,
            arguments,
            onOutputLine: line => WriteLine(line),
            cancellationToken: cancellationToken);

        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            WriteLine(result.StandardError.TrimEnd(), OutputKind.Error);
        }

        WriteLine(
            result.Success
                ? $"Completado en {result.Duration.TotalSeconds:F1} s (código {result.ExitCode})."
                : $"Finalizó con errores en {result.Duration.TotalSeconds:F1} s (código {result.ExitCode}).",
            result.Success ? OutputKind.Success : OutputKind.Error);
        return result;
    }
}
