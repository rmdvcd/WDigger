using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Module for advanced network configuration: static IP, firewall, ports,
/// traceroute, proxy and computer rename.
/// </summary>
public class AdvancedNetworkModule : IModule
{
    public string Name => "Advanced Network";
    public string Description => "Advanced network configuration: static IP, firewall, ports, traceroute, proxy.";

    public void Run(AppHost host)
    {
        while (true)
        {
            var choice = AnsiConsole.Prompt(new SelectionPrompt<string>()
                .Title("Advanced Network module")
                .AddChoices(
                    "Set static IP address",
                    "Set DHCP (obtain automatically)",
                    "Show open ports (netstat)",
                    "Traceroute to host",
                    "Windows Firewall rules",
                    "Configure WinHTTP proxy",
                    "Rename computer",
                    "Back to main menu"));

            switch (choice)
            {
                case "Set static IP address":
                    SetStaticIp(host);
                    break;
                case "Set DHCP (obtain automatically)":
                    SetDhcp(host);
                    break;
                case "Show open ports (netstat)":
                    host.Commands.Run("netstat", "-ano");
                    break;
                case "Traceroute to host":
                    {
                        var target = AnsiConsole.Ask<string>("Host or IP to trace");
                        if (!string.IsNullOrWhiteSpace(target))
                        {
                            host.Commands.Run("tracert", target);
                        }
                    }
                    break;
                case "Windows Firewall rules":
                    host.Commands.Run("netsh", "advfirewall firewall show rule name=all");
                    break;
                case "Configure WinHTTP proxy":
                    ConfigureProxy(host);
                    break;
                case "Rename computer":
                    RenameComputer(host);
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static void SetStaticIp(AppHost host)
    {
        AnsiConsole.MarkupLine("[yellow]Requires Administrator. Choose the adapter name from 'Show DNS configuration' in Network module.[/]");
        var adapter = AnsiConsole.Ask<string>("Adapter name (e.g. Ethernet, Wi-Fi)");
        if (string.IsNullOrWhiteSpace(adapter)) return;
        var ip = AnsiConsole.Ask<string>("IP address (e.g. 192.168.1.50)");
        if (string.IsNullOrWhiteSpace(ip)) return;
        var mask = AnsiConsole.Ask<string>("Subnet mask (e.g. 255.255.255.0)", "255.255.255.0");
        var gateway = AnsiConsole.Ask<string>("Default gateway (e.g. 192.168.1.1)");
        if (!Confirm(host)) return;

        host.Commands.Run("netsh", $"interface ip set address name=\"{adapter}\" static {ip} {mask} {gateway}");
        AnsiConsole.MarkupLine("[green]Static IP configured (simulated if dry-run mode is on).[/]");
    }

    private static void SetDhcp(AppHost host)
    {
        var adapter = AnsiConsole.Ask<string>("Adapter name (e.g. Ethernet, Wi-Fi)");
        if (string.IsNullOrWhiteSpace(adapter)) return;
        if (!Confirm(host)) return;
        host.Commands.Run("netsh", $"interface ip set address name=\"{adapter}\" dhcp");
        host.Commands.Run("netsh", $"interface ip set dns name=\"{adapter}\" dhcp");
        AnsiConsole.MarkupLine("[green]DHCP enabled (simulated if dry-run mode is on).[/]");
    }

    private static void ConfigureProxy(AppHost host)
    {
        var proxy = AnsiConsole.Ask<string>("Proxy server (e.g. proxy.company.com:8080), leave empty to reset");
        if (!Confirm(host)) return;
        if (string.IsNullOrWhiteSpace(proxy))
        {
            host.Commands.Run("netsh", "winhttp reset proxy");
        }
        else
        {
            host.Commands.Run("netsh", $"winhttp set proxy {proxy}");
        }
    }

    private static void RenameComputer(AppHost host)
    {
        var newName = AnsiConsole.Ask<string>("New computer name");
        if (string.IsNullOrWhiteSpace(newName)) return;
        AnsiConsole.MarkupLine("[yellow]A reboot is required after renaming the computer.[/]");
        if (!Confirm(host)) return;
        host.Commands.Run("powershell", $"-Command \"Rename-Computer -NewName '{newName}' -Restart:$false\"");
        AnsiConsole.MarkupLine("[green]Computer rename scheduled. Reboot to apply.[/]");
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
