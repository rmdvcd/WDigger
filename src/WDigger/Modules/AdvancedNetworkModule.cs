using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class AdvancedNetworkModule : IModule
{
    public string Name => "Red avanzada";
    public string Description => "IP estática, DHCP, puertos, firewall, proxy y nombre del equipo.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Configurar IP estática", "Asigna dirección, máscara y puerta de enlace a un adaptador inventariado.",
            SetStaticIpAsync, changesSystem: true,
            confirmation: "Una dirección o puerta de enlace incorrecta puede desconectar el equipo.", risk: OperationRisk.High),
        new ModuleAction("Usar DHCP automáticamente", "Configura IP y DNS automáticos en un adaptador inventariado.",
            SetDhcpAsync, changesSystem: true, risk: OperationRisk.Moderate),
        new ModuleAction("Mostrar puertos abiertos", "Muestra conexiones y puertos mediante netstat -ano.",
            (context, token) => context.RunCommandAsync(CommandSpec.Read(WindowsTool.Netstat, "-ano"), token)),
        new ModuleAction("Trazar ruta a un host", "Ejecuta tracert hasta un nombre de host o IP validado.",
            TraceRouteAsync),
        new ModuleAction("Mostrar reglas del firewall", "Enumera las reglas de Windows Firewall.",
            (context, token) => context.RunCommandAsync(CommandSpec.Read(WindowsTool.Netsh,
                "advfirewall", "firewall", "show", "rule", "name=all"), token)),
        new ModuleAction("Configurar proxy WinHTTP", "Define un proxy validado o restablece la configuración si se deja vacío.",
            ConfigureProxyAsync, changesSystem: true, risk: OperationRisk.Moderate),
        new ModuleAction("Cambiar nombre del equipo", "Programa un nuevo nombre del equipo para el próximo reinicio.",
            RenameComputerAsync, changesSystem: true,
            confirmation: "El nuevo nombre se aplicará después de reiniciar Windows.", risk: OperationRisk.Moderate)
    };

    private static async Task SetStaticIpAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var adapter = await context.PromptAsync("IP estática", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        adapter = RequireKnownAdapter(adapter);
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
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh,
            "interface", "ip", "set", "address", $"name={adapter}", "static", ip, mask, gateway), token);
    }

    private static async Task SetDhcpAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var adapter = await context.PromptAsync("Configurar DHCP", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        adapter = RequireKnownAdapter(adapter);
        if (!await context.ConfirmAsync("Confirmar DHCP", $"Se cambiarán la IP y DNS de «{adapter}» a DHCP. Esto puede desconectar sesiones remotas. ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh,
            "interface", "ip", "set", "address", $"name={adapter}", "dhcp"), token);
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh,
            "interface", "ip", "set", "dns", $"name={adapter}", "dhcp"), token);
    }

    private static async Task TraceRouteAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var target = await context.PromptAsync("Trazar ruta", "Nombre de host o dirección IP:");
        if (target is null) return;
        target = RequireHostOrAddress(target);
        await context.RunCommandAsync(CommandSpec.Read(WindowsTool.Tracert, target), token);
    }

    private static async Task ConfigureProxyAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var proxy = await context.PromptAsync("Proxy WinHTTP",
            "Servidor (por ejemplo, proxy.empresa.com:8080). Déjalo vacío para restablecer:", allowEmpty: true);
        if (proxy is null) return;
        proxy = proxy.Trim();
        if (!string.IsNullOrEmpty(proxy) && (proxy.Length > 260 || proxy.Any(char.IsWhiteSpace) || proxy.Contains('"')))
        {
            throw new ArgumentException("El proxy no puede contener espacios, comillas ni superar 260 caracteres.");
        }
        await context.RunCommandAsync(string.IsNullOrWhiteSpace(proxy)
            ? CommandSpec.Modify(WindowsTool.Netsh, "winhttp", "reset", "proxy")
            : CommandSpec.Modify(WindowsTool.Netsh, "winhttp", "set", "proxy", proxy), token);
    }

    private static async Task RenameComputerAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var newName = await context.PromptAsync("Cambiar nombre del equipo", "Nuevo nombre:");
        if (newName is null) return;
        newName = newName.Trim();
        if (newName.Length is 0 or > 15 || newName.Any(c => !char.IsLetterOrDigit(c) && c != '-'))
        {
            context.WriteLine("El nombre del equipo no es válido (máximo 15 caracteres, alfanumérico y guiones).", OutputKind.Warning);
            return;
        }
        await context.RunPowerShellAsync(PowerShellScriptId.RenameComputer, new[] { newName }, token);
        if (!context.IsSimulation)
        {
            context.RequireRestart("El cambio de nombre está programado; reinicia Windows para aplicarlo.");
        }
    }

    private static string RequireKnownAdapter(string value)
    {
        var name = value.Trim();
        if (name.Length is 0 or > 128 || name.Any(char.IsControl)) throw new ArgumentException("El nombre del adaptador no es válido.");
        if (!NetworkInterface.GetAllNetworkInterfaces().Any(item => item.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("El adaptador indicado ya no existe.");
        }
        return name;
    }

    private static string RequireHostOrAddress(string value)
    {
        var target = value.Trim();
        if (target.Length is 0 or > 253 || target.Any(char.IsWhiteSpace) || target.Any(char.IsControl))
        {
            throw new ArgumentException("El destino no es un host o una dirección válidos.");
        }
        if (IPAddress.TryParse(target, out _)) return target;
        if (!Uri.CheckHostName(target).Equals(UriHostNameType.Unknown)) return target;
        throw new ArgumentException("El destino no es un host o una dirección válidos.");
    }

    private static bool IsValidIp(string? ip)
        => !string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip.Trim(), out var address)
            && address.AddressFamily == AddressFamily.InterNetwork;

    private static bool IsValidMask(string mask)
    {
        if (!IsValidIp(mask)) return false;
        var bytes = IPAddress.Parse(mask.Trim()).GetAddressBytes();
        uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var inverse = ~value;
        return (inverse & unchecked(inverse + 1)) == 0;
    }
}
