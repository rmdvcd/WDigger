using Spectre.Console;
using WDigger.Core;
using WDigger.UI;

// Global catch-all: if anything crashes, leave a trace for the technician instead
// of disappearing silently.
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    try
    {
        File.AppendAllText("WDigger.crash.log",
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FATAL UNHANDLED EXCEPTION:{Environment.NewLine}{e.ExceptionObject}{Environment.NewLine}{Environment.NewLine}");
    }
    catch
    {
        // Best effort only.
    }
};

try
{
    return App.Run(args);
}
catch (Exception ex)
{
    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
    return 1;
}
