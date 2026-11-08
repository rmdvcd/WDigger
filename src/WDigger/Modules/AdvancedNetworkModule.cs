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
        var adapter = context.Prompt("IP estática", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        var ip = context.Prompt("IP estática", "Dirección IP (por ejemplo, 192.168.1.50):");
        if (ip is null) return;
        var mask = context.Prompt("IP estática", "Máscara de subred:", "255.255.255.0");
        if (mask is null) return;
        var gateway = context.Prompt("IP estática", "Puerta de enlace predeterminada:");
        if (gateway is null) return;

        await context.RunCommandAsync("netsh",
            $"interface ip set address name=\"{adapter}\" static {ip} {mask} {gateway}", token);
    }

    private static async Task SetDhcpAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var adapter = context.Prompt("Configurar DHCP", "Nombre exacto del adaptador:");
        if (adapter is null) return;
        await context.RunCommandAsync("netsh", $"interface ip set address name=\"{adapter}\" dhcp", token);
        await context.RunCommandAsync("netsh", $"interface ip set dns name=\"{adapter}\" dhcp", token);
    }

    private static async Task TraceRouteAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var target = context.Prompt("Trazar ruta", "Nombre de host o dirección IP:");
        if (target is not null)
        {
            await context.RunCommandAsync("tracert", target, token);
        }
    }

    private static async Task ConfigureProxyAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var proxy = context.Prompt("Proxy WinHTTP",
            "Servidor (por ejemplo, proxy.empresa.com:8080). Déjalo vacío para restablecer:", allowEmpty: true);
        if (proxy is null) return;
        await context.RunCommandAsync("netsh", string.IsNullOrWhiteSpace(proxy)
            ? "winhttp reset proxy"
            : $"winhttp set proxy {proxy}", token);
    }

    private static async Task RenameComputerAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var newName = context.Prompt("Cambiar nombre del equipo", "Nuevo nombre:");
        if (newName is null) return;
        var escaped = newName.Replace("'", "''");
        await context.RunCommandAsync("powershell",
            $"-NoProfile -Command \"Rename-Computer -NewName '{escaped}' -Restart:$false\"", token);
        context.WriteLine("Nombre programado. Reinicia Windows para aplicarlo.", OutputKind.Success);
    }

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
