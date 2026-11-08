namespace WDigger.Core;

/// <summary>
/// Best-effort crash logging next to the executable. Never throws: crash
/// handling must not be able to crash.
/// </summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "WDigger.crash.log");

    public static void Write(Exception exception)
    {
        try
        {
            File.AppendAllText(
                FilePath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Crash logging is best effort (for example, on read-only media).
        }
    }
}
