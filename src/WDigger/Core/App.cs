using WDigger.UI;

namespace WDigger.Core;

public static class App
{
    public static int Run(string[] args)
    {
        var dryRun = args.Contains("--dry-run", StringComparer.OrdinalIgnoreCase)
            || args.Contains("-dryrun", StringComparer.OrdinalIgnoreCase);

        using var host = AppHost.Create(AppContext.BaseDirectory);
        host.DryRun = dryRun;

        using var shell = new Shell(host);
        Application.Run(shell);
        return 0;
    }
}
