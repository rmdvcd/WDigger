using WDigger.Core;
using WDigger.Models;

namespace WDigger.UI;

/// <summary>Native dialogs, tables and command execution available to modules.</summary>
public sealed class ModuleExecutionContext
{
    private readonly Shell _shell;

    internal ModuleExecutionContext(Shell shell, AppHost host)
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

    public string? Prompt(string title, string label, string defaultValue = "", bool allowEmpty = false)
        => NativeDialogs.Prompt(_shell, title, label, defaultValue, allowEmpty);

    public T? Choose<T>(string title, string label, IEnumerable<T> choices, Func<T, string> formatter)
        where T : class
    {
        var items = choices.ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var selected = NativeDialogs.Choose(_shell, title, label, items.Select(formatter).ToArray());
        return selected.HasValue ? items[selected.Value] : null;
    }

    public string? PickFile(string title, string filter, string? initialPath = null)
        => NativeDialogs.PickFile(_shell, title, filter, initialPath);

    public string? PickFolder(string description, string? initialPath = null)
        => NativeDialogs.PickFolder(_shell, description, initialPath);

    public bool Confirm(string title, string message)
    {
        if (Host.DryRun)
        {
            WriteLine($"[SIMULACIÓN] {title}", OutputKind.Warning);
            return true;
        }

        return MessageBox.Show(
                   _shell,
                   message,
                   title,
                   MessageBoxButtons.YesNo,
                   MessageBoxIcon.Warning,
                   MessageBoxDefaultButton.Button2) == DialogResult.Yes;
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
