namespace WDigger.Models;

/// <summary>
/// Result of a command executed by CommandExecutor.
/// </summary>
public sealed class CommandResult
{
    public string Command { get; init; } = "";
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = "";
    public string StandardError { get; init; } = "";
    public TimeSpan Duration { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.Now;

    public bool Success => ExitCode == 0;

    public CommandResult() { }

    public CommandResult(string command, int exitCode, string standardOutput, string standardError, TimeSpan duration)
    {
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput;
        StandardError = standardError;
        Duration = duration;
    }
}
