using System.Net;
using System.Net.Sockets;
using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class AdvancedNetworkModule : IModule
{
    public string Name => "Red avanzada";
    public string Description => "IP estática, DHCP, puertos, firewall, proxy y nombre del equipo.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Configurar IP estática", "Asigna dirección, máscara y puerta de enlace a un adaptador.",
            (context, token) => SetStaticIpAsync(host, context, token), changesSystem: true,
            confirmation: "Una dirección o puerta de enlace incorrecta puede desconectar el equipo."),
        new ModuleAction("Usar DHCP automáticamente", "Configura IP y DNS automáticos en un adaptador.",
            (context, token) => SetDhcpAsync(host, context, token), changesSystem: true),
        new ModuleAction("Mostrar puertos abiertos", "Muestra conexiones y puertos mediante netstat -ano.",
            (context, token) => Run(host, context, token, "netstat", "-ano")),
        new ModuleAction("Trazar ruta a un host", "Ejecuta tracert hasta un nombre de host o IP.",
            (context, token) => TraceRouteAsync(host, context, token)),
        new ModuleAction("Mostrar reglas del firewall", "Enumera las reglas de Windows Firewall.",
            (context, token) => Run(host, context, token, "netsh", "advfirewall firewall show rule name=all")),
        new ModuleAction("Configurar proxy WinHTTP", "Define un proxy o restablece la configuración si se deja vacío.",
            (context, token) => ConfigureProxyAsync(host, context, token), changesSystem: true),
        new ModuleAction("Cambiar nombre del equipo", "Programa un nuevo nombre de equipo para el próximo reinicio.",
            (context, token) => RenameComputerAsync(host, context, token), changesSystem: true,
            confirmation: "El nuevo nombre se aplicará después de reiniciar Windows.")
    };

    private static async Task SetStaticIpAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var adapter = await context.PromptAsync("IP estática", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        var ip = await context.PromptAsync("IP estática", "Dirección IP (por ejemplo, 192.168.1.50):");
        if (ip is null) return;
        if (!IsValidIp(ip)) throw new ArgumentException("La dirección debe ser IPv4 válida.");
        var mask = await context.PromptAsync("IP estática", "Máscara de subred:", "255.255.255.0");
        if (mask is null) return;
        if (!IsValidMask(mask)) throw new ArgumentException("La máscara IPv4 no es válida o no es contigua.");
        var gateway = await context.PromptAsync("IP estática", "Puerta de enlace predeterminada:");
        if (gateway is null) return;
        if (!IsValidIp(gateway)) throw new ArgumentException("La puerta de enlace debe ser IPv4 válida.");

        if (!await context.ConfirmAsync("Confirmar IP", $"Se cambiará la IP de «{adapter}» a {ip}, máscara {mask}, puerta de enlace {gateway}. Esto puede desconectar sesiones remotas. ¿Continuar?")) return;
        await context.RunArgumentsAsync("netsh", new[] { "interface", "ip", "set", "address", $"name={adapter}", "static", ip, mask, gateway }, token);
    }

    private static async Task SetDhcpAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var adapter = await context.PromptAsync("Configurar DHCP", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        if (!await context.ConfirmAsync("Confirmar DHCP", $"Se cambiarán la IP y DNS de «{adapter}» a DHCP. Esto puede desconectar sesiones remotas. ¿Continuar?")) return;
        await context.RunArgumentsAsync("netsh", new[] { "interface", "ip", "set", "address", $"name={adapter}", "dhcp" }, token);
        await context.RunArgumentsAsync("netsh", new[] { "interface", "ip", "set", "dns", $"name={adapter}", "dhcp" }, token);
    }

    private static async Task TraceRouteAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var target = await context.PromptAsync("Trazar ruta", "Nombre de host o dirección IP:");
        if (target is not null)
        {
            await context.RunArgumentsAsync("tracert", new[] { target }, token);
        }
    }

    private static async Task ConfigureProxyAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var proxy = await context.PromptAsync("Proxy WinHTTP",
            "Servidor (por ejemplo, proxy.empresa.com:8080). Déjalo vacío para restablecer:", allowEmpty: true);
        if (proxy is null) return;
        await context.RunArgumentsAsync("netsh", string.IsNullOrWhiteSpace(proxy)
            ? new[] { "winhttp", "reset", "proxy" }
            : new[] { "winhttp", "set", "proxy", proxy }, token);
    }

    private static async Task RenameComputerAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var newName = await context.PromptAsync("Cambiar nombre del equipo", "Nuevo nombre:");
        if (newName is null) return;
        if (newName.Length > 15 || newName.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
        {
            context.WriteLine("El nombre del equipo no es válido (máximo 15 caracteres, alfanumérico y guiones).",
                OutputKind.Warning);
            return;
        }
        var escaped = newName.Replace("'", "''");
        await context.RunPowerShellAsync($"Rename-Computer -NewName '{escaped}' -Restart:$false", token);
        if (!host.DryRun) context.WriteLine("Nombre programado. Reinicia Windows para aplicarlo.", OutputKind.Success);
    }

    private static bool IsValidIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return IPAddress.TryParse(ip.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static bool IsValidMask(string mask)
    {
        if (!IsValidIp(mask)) return false;
        var bytes = IPAddress.Parse(mask.Trim()).GetAddressBytes();
        uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var inverse = ~value;
        return (inverse & unchecked(inverse + 1)) == 0;
    }

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
