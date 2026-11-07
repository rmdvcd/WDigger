namespace WDigger.Core;

public static class App
{
    public static int Run(string[] args)
    {
        var dryRun = args.Contains("--dry-run") || args.Contains("-dryrun");
        var host = AppHost.Create(AppDomain.CurrentDomain.BaseDirectory);
        host.DryRun = dryRun;

        var shell = new UI.Shell(host);
        shell.Run();
        return 0;
    }
}
