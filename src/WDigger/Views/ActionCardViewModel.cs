using System.ComponentModel;
using System.Runtime.CompilerServices;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Views;

/// <summary>Presentation state for one reviewed module action.</summary>
public sealed class ActionCardViewModel : INotifyPropertyChanged
{
    public ActionCardViewModel(ModuleAction action, Func<ModuleAction, Task> run, Action<string>? trace = null)
    {
        _trace = trace;
        Action = action;
        Title = action.Title;
        var diagnosticNote = string.IsNullOrWhiteSpace(action.DiagnosticCheckId)
            ? string.Empty
            : $"\nDiagnóstico asociado: {action.DiagnosticCheckId}. Ejecuta Diagnóstico antes de decidir reparar.";
        Description = action.UnavailableReason is not null ? $"{action.Description}\nNo disponible: {action.UnavailableReason}"
            : action.ChangesSystem && !action.AllowedInElevatedSession ? $"{action.Description}\nSolo simulación: ejecución real pendiente de revisión.{diagnosticNote}"
            : action.ChangesSystem ? $"{action.Description}\nRiesgo {RiskText(action.Risk)}: WDigger solicitará confirmación final.{diagnosticNote}"
            : action.Description;
        ChangesSystem = action.ChangesSystem;
        BadgeText = action.ChangesSystem ? $"Riesgo {RiskText(action.Risk)}" : action.WritesFile ? "Guarda un archivo" : "Solo lectura";
        _run = run;
    }

    private readonly Func<ModuleAction, Task> _run;
    private readonly Action<string>? _trace;
    private bool _isRunning;

    public ModuleAction Action { get; }
    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public bool IsAvailable => Action.UnavailableReason is null;
    public string BadgeText { get; }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunningBadgeText));
            }
        }
    }

    public string RunningBadgeText => IsRunning ? "Ejecutando…" : BadgeText;

    public async Task ExecuteAsync()
    {
        if (IsRunning) return;
        _trace?.Invoke($"Action card starting: {Title}");
        IsRunning = true;
        try
        {
            await _run(Action);
        }
        finally
        {
            _trace?.Invoke($"Action card clearing running state: {Title}");
            IsRunning = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string RiskText(OperationRisk risk) => risk switch
    {
        OperationRisk.Destructive => "destructivo",
        OperationRisk.High => "alto",
        OperationRisk.Moderate => "moderado",
        OperationRisk.Low => "bajo",
        _ => "de solo lectura"
    };
}
