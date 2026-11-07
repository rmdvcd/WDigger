using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 5 module: network stack reset, DNS management, connectivity
/// testing and quick DNS configuration.
/// </summary>
public class NetworkModule : IModule
{
    public string Name => "Network";
    public string Description => "TCP/IP and Winsock reset, DNS flush, IP release/renew, connectivity tests, DNS configuration.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Network module")
                .AddChoices(
                    "Flush DNS cache (ipconfig /flushdns)",
                    "Release & renew IP (ipconfig /release /renew)",
                    "Reset Winsock catalog (netsh winsock reset)",
                    "Reset TCP/IP stack (netsh int ip reset)",
                    "Show DNS configuration",
                    "Set DNS servers on an adapter",
                    "Test connectivity to 1.1.1.1",
                    "Back to main menu"));

            switch (choice)
            {
                case "Flush DNS cache (ipconfig /flushdns)":
                    RunSimple(host, "Flush DNS cache", "ipconfig", "/flushdns");
                    break;
                case "Release & renew IP (ipconfig /release /renew)":
                    RunSimple(host, "Release IP", "ipconfig", "/release");
                    RunSimple(host, "Renew IP", "ipconfig", "/renew");
                    break;
                case "Reset Winsock catalog (netsh winsock reset)":
                    AnsiConsole.MarkupLine("[yellow]A reboot is required after resetting Winsock.[/]");
                    if (Confirm(host)) RunSimple(host, "Reset Winsock", "netsh", "winsock reset");
                    break;
                case "Reset TCP/IP stack (netsh int ip reset)":
                    AnsiConsole.MarkupLine("[yellow]A reboot is required after resetting TCP/IP.[/]");
                    if (Confirm(host)) RunSimple(host, "Reset TCP/IP", "netsh", "int ip reset");
                    break;
                case "Show DNS configuration":
                    host.Commands.Run("netsh", "interface ip show dns");
                    break;
                case "Set DNS servers on an adapter":
                    SetDns(host);
                    break;
                case "Test connectivity to 1.1.1.1":
                    RunSimple(host, "Test connectivity to 1.1.1.1", "powershell",
                        "-Command \"Test-NetConnection -ComputerName 1.1.1.1\"");
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void RunSimple(AppHost host, string label, string fileName, string arguments)
    {
        AnsiConsole.MarkupLine($"[grey]{label}...[/]");
        if (!Confirm(host)) return;
        host.Commands.Run(fileName, arguments);
    }

    private static void SetDns(AppHost host)
    {
        AnsiConsole.MarkupLine("[grey]Listing network adapters...[/]");
        host.Commands.Run("netsh", "interface ip show dns");

        var adapter = AnsiConsole.Ask<string>("Adapter name (e.g. Ethernet, Wi-Fi)");
        if (string.IsNullOrWhiteSpace(adapter)) return;

        var dns = AnsiConsole.Ask<string>("Primary DNS server (e.g. 1.1.1.1)");
        if (string.IsNullOrWhiteSpace(dns)) return;

        var secondary = AnsiConsole.Ask<string>("Secondary DNS server (optional, press Enter to skip)");

        if (!Confirm(host)) return;

        host.Commands.Run("netsh",
            $"interface ip set dns name=\"{adapter}\" static {dns} primary");
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            host.Commands.Run("netsh",
                $"interface ip add dns name=\"{adapter}\" {secondary} index=2");
        }

        AnsiConsole.MarkupLine($"[green]DNS configured on '{adapter}' (simulated if dry-run mode is on).[/]");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
