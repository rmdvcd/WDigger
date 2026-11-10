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
        bool allowedInElevatedSession = false)
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
    }

    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public string? Confirmation { get; }
    public string? UnavailableReason { get; }
    public bool CanCancel { get; }
    public bool WritesFile { get; }
    /// <summary>Explicitly reviewed, hard-coded actions allowed in a manually elevated session.</summary>
    public bool AllowedInElevatedSession { get; }
    public Func<ModuleExecutionContext, CancellationToken, Task> Execute { get; }

    public override string ToString() => Title;
}
