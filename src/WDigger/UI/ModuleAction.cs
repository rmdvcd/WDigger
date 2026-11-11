using WDigger.Core;

namespace WDigger.UI;

public enum OutputKind
{
    Info,
    Success,
    Warning,
    Error,
    Command,
    Muted
}

/// <summary>An executable row in a native module view.</summary>
public sealed class ModuleAction
{
    public ModuleAction(
        string title,
        string description,
        Func<ModuleExecutionContext, CancellationToken, Task> execute,
        bool changesSystem = false,
        string? confirmation = null,
        string? unavailableReason = null,
        bool canCancel = true,
        bool writesFile = false,
        bool allowedInElevatedSession = false,
        OperationRisk? risk = null,
        TimeSpan? commandTimeout = null,
        string? diagnosticCheckId = null,
        OperationRecovery? recovery = null,
        OperationCancellationPolicy? cancellationPolicy = null)
    {
        Title = title;
        Description = description;
        Execute = execute;
        ChangesSystem = changesSystem;
        Confirmation = confirmation;
        UnavailableReason = unavailableReason;
        CanCancel = canCancel;
        WritesFile = writesFile;
        AllowedInElevatedSession = allowedInElevatedSession;
        Risk = risk ?? (changesSystem ? OperationRisk.Moderate : OperationRisk.ReadOnly);
        DiagnosticCheckId = diagnosticCheckId;
        Recovery = recovery ?? (changesSystem ? OperationRecovery.ManualReviewRequired : OperationRecovery.NoAutomaticRecovery);
        CancellationPolicy = cancellationPolicy ?? (canCancel
            ? OperationCancellationPolicy.AllowedWithConfirmation
            : OperationCancellationPolicy.NotAllowed);
        CommandTimeout = commandTimeout ?? TimeSpan.FromMinutes(30);
        if (CommandTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(commandTimeout));
        }
    }

    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public string? Confirmation { get; }
    public string? UnavailableReason { get; }
    public bool CanCancel { get; }
    public bool WritesFile { get; }
    /// <summary>Explicitly reviewed, hard-coded actions allowed in the required elevated session.</summary>
    public bool AllowedInElevatedSession { get; }
    public OperationRisk Risk { get; }
    /// <summary>Stable read-only check ID that provides evidence for this repair, when available.</summary>
    public string? DiagnosticCheckId { get; }
    public OperationRecovery Recovery { get; }
    public OperationCancellationPolicy CancellationPolicy { get; }
    public TimeSpan CommandTimeout { get; }
    public Func<ModuleExecutionContext, CancellationToken, Task> Execute { get; }

    public override string ToString() => Title;
}
