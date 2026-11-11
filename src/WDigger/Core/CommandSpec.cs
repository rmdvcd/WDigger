namespace WDigger.Core;

/// <summary>
/// Closed set of Windows executables that WDigger may launch from System32.
/// Modules use this identifier instead of supplying a program name or a command line.
/// </summary>
public enum WindowsTool
{
    BcdEdit,
    Chkdsk,
    Cscript,
    Dism,
    Ipconfig,
    Net,
    Netsh,
    Netstat,
    PnPUtil,
    PowerShell,
    Rundll32,
    Sfc,
    Tracert,
    Wevtutil
}

/// <summary>Whether a command only observes Windows or may alter it.</summary>
public enum CommandEffect
{
    ReadOnly,
    ModifiesSystem
}

/// <summary>
/// Immutable, typed launch request. It keeps executable selection, argument
/// boundaries, expected effect, and timeout together at the call site.
/// </summary>
public sealed class CommandSpec
{
    public CommandSpec(WindowsTool tool, IEnumerable<string> arguments, CommandEffect effect = CommandEffect.ReadOnly,
        TimeSpan? timeout = null)
    {
        Tool = tool;
        if (arguments is null) throw new ArgumentNullException(nameof(arguments));
        Arguments = Array.AsReadOnly(arguments.Select(argument =>
            argument ?? throw new ArgumentException("Un argumento no puede ser nulo.", nameof(arguments))).ToArray());
        Effect = effect;
        Timeout = timeout;
        if (Timeout is { } configured && configured <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }
    }

    public WindowsTool Tool { get; }
    public IReadOnlyList<string> Arguments { get; }
    public CommandEffect Effect { get; }
    public TimeSpan? Timeout { get; }

    public static CommandSpec Read(WindowsTool tool, params string[] arguments)
        => new(tool, arguments, CommandEffect.ReadOnly);

    public static CommandSpec Modify(WindowsTool tool, params string[] arguments)
        => new(tool, arguments, CommandEffect.ModifiesSystem);
}
