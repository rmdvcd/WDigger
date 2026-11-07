using Spectre.Console;
using WDigger.Core;

namespace WDigger.Modules;

/// <summary>
/// Phase 5 module (skeleton): network stack reset, DNS management and
/// connectivity testing.
/// </summary>
public class NetworkModule : IModule
{
    public string Name => "Network";
    public string Description => "TCP/IP and Winsock reset, DNS flush, IP release/renew, connectivity tests.";

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
                    "Test connectivity to 1.1.1.1",
                    "Back to main menu"));

            switch (choice)
            {
                case "Flush DNS cache (ipconfig /flushdns)":
                    host.Commands.Run("ipconfig", "/flushdns");
                    break;
                case "Release & renew IP (ipconfig /release /renew)":
                    host.Commands.Run("ipconfig", "/release");
                    host.Commands.Run("ipconfig", "/renew");
                    break;
                case "Reset Winsock catalog (netsh winsock reset)":
                    AnsiConsole.MarkupLine("[yellow]A reboot is required after resetting Winsock.[/]");
                    if (Confirm(host)) host.Commands.Run("netsh", "winsock reset");
                    break;
                case "Reset TCP/IP stack (netsh int ip reset)":
                    AnsiConsole.MarkupLine("[yellow]A reboot is required after resetting TCP/IP.[/]");
                    if (Confirm(host)) host.Commands.Run("netsh", "int ip reset");
                    break;
                case "Show DNS configuration":
                    host.Commands.Run("netsh", "interface ip show dns");
                    break;
                case "Test connectivity to 1.1.1.1":
                    host.Commands.Run("powershell", "-Command \"Test-NetConnection -ComputerName 1.1.1.1\"");
                    break;
                case "Back to main menu":
                    return;
            }

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press any key to return...[/]");
            Console.ReadKey(true);
        }
    }

    private static bool Confirm(AppHost host)
    {
        AnsiConsole.MarkupLine(host.DryRun ? "[grey][DRY-RUN][/]" : "[red][LIVE - changes will be applied][/]");
        return host.DryRun || AnsiConsole.Confirm("Apply this action on the real system?");
    }
}
