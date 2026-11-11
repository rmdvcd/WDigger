namespace WDigger.Core;

/// <summary>
/// Best-effort crash logging. Prefer the portable directory, but fall back to
/// LocalAppData when WDigger is started from a protected or read-only folder.
/// Crash handling must never be able to crash.
/// </summary>
public static class CrashLog
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "WDigger.crash.log");

    public static void Write(Exception exception)
    {
        var content = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {exception}{Environment.NewLine}{Environment.NewLine}";
        if (TryAppend(FilePath, content))
        {
            return;
        }

        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(local))
            {
                var directory = Path.Combine(local, "WDigger", "logs");
                Directory.CreateDirectory(directory);
                _ = TryAppend(Path.Combine(directory, "WDigger.crash.log"), content);
            }
        }
        catch
        {
            // Best effort only.
        }
    }

    private static bool TryAppend(string path, string content)
    {
        try
        {
            File.AppendAllText(path, content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
