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
        string? confirmation = null)
    {
        Title = title;
        Description = description;
        Execute = execute;
        ChangesSystem = changesSystem;
        Confirmation = confirmation;
    }

    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public string? Confirmation { get; }
    public Func<ModuleExecutionContext, CancellationToken, Task> Execute { get; }

    public override string ToString() => Title;
}
