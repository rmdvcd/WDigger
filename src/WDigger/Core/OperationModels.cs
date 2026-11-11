namespace WDigger.Core;

/// <summary>Impact level used to describe an operation before it runs.</summary>
public enum OperationRisk
{
    ReadOnly,
    Low,
    Moderate,
    High,
    Destructive
}

/// <summary>Terminal and non-terminal states recorded for every operation.</summary>
public enum OperationCancellationPolicy
{
    NotAllowed,
    AllowedWithConfirmation
}

/// <summary>Recovery commitment exposed before a modifying operation starts.</summary>
public enum OperationRecovery
{
    NoAutomaticRecovery,
    RestorePointOnly,
    SnapshotAndVerifiedRevert,
    CompensatesInProcess,
    ManualReviewRequired
}

public enum OperationStatus
{
    NotStarted,
    Running,
    Simulated,
    Succeeded,
    SucceededWithRestartRequired,
    Failed,
    Partial,
    Cancelled,
    Blocked,
    ManualActionRequired
}

/// <summary>Typed result shown to the UI and persisted in the operation journal.</summary>
public sealed record OperationOutcome(
    OperationStatus Status,
    string Summary,
    bool RestartRequired = false,
    string? ManualAction = null)
{
    public static OperationOutcome Simulated(string summary = "La operación se simuló; no se aplicaron cambios.")
        => new(OperationStatus.Simulated, summary);

    public static OperationOutcome Succeeded(string summary = "La operación terminó correctamente.", bool restartRequired = false)
        => new(restartRequired ? OperationStatus.SucceededWithRestartRequired : OperationStatus.Succeeded,
            summary, restartRequired);

    public static OperationOutcome NotStarted(string summary)
        => new(OperationStatus.NotStarted, summary);

    public static OperationOutcome Blocked(string summary)
        => new(OperationStatus.Blocked, summary);

    public static OperationOutcome Cancelled(string summary)
        => new(OperationStatus.Cancelled, summary);

    public static OperationOutcome Failed(string summary, string? manualAction = null)
        => new(OperationStatus.Failed, summary, false, manualAction);

    public static OperationOutcome Partial(string summary, string? manualAction = null)
        => new(OperationStatus.Partial, summary, false, manualAction);
}

/// <summary>Serializable evidence for one phase of an operation.</summary>
public sealed class OperationStepRecord
{
    public string Name { get; set; } = "";
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.Running;
    public string Summary { get; set; } = "";
    public int? ExitCode { get; set; }
}

/// <summary>Persistent, redacted lifecycle record for a system operation.</summary>
public sealed class OperationRecord
{
    public int SchemaVersion { get; set; } = 1;
    public Guid OperationId { get; set; }
    public string ActionTitle { get; set; } = "";
    public OperationRisk Risk { get; set; }
    public bool IsSimulation { get; set; }
    public bool IsElevated { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; }
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public OperationStatus Status { get; set; } = OperationStatus.Running;
    public string Summary { get; set; } = "";
    public bool RestorePointCreated { get; set; }
    public string? RestorePointReference { get; set; }
    public bool RestartRequired { get; set; }
    public string? ManualAction { get; set; }
    public List<OperationStepRecord> Steps { get; set; } = new();
}
