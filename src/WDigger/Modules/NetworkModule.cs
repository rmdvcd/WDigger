using System.Net;
using System.Net.Sockets;
using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class NetworkModule : IModule
{
    public string Name => "Red";
    public string Description => "DNS, renovación IP, Winsock, TCP/IP y pruebas de conectividad.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Vaciar caché DNS", "Ejecuta ipconfig /flushdns.",
            (context, token) => context.RunArgumentsAsync("ipconfig", new[] { "/flushdns" }, token), changesSystem: true,
            confirmation: "Se borrará la caché DNS local; las aplicaciones pueden volver a consultar sus servidores DNS.", allowedInElevatedSession: true),
        new ModuleAction("Liberar y renovar IP", "Libera y renueva la configuración DHCP de los adaptadores.",
            (context, token) => ReleaseRenewAsync(host, context, token), changesSystem: true,
            confirmation: "La conexión de red se interrumpirá temporalmente."),
        new ModuleAction("Restablecer Winsock", "Restablece el catálogo Winsock. Requiere reiniciar.",
            (context, token) => Run(host, context, token, "netsh", "winsock reset"), changesSystem: true,
            confirmation: "Se restablecerá Winsock; las conexiones y sesiones remotas pueden interrumpirse y será necesario reiniciar Windows."),
        new ModuleAction("Restablecer TCP/IP", "Restablece la pila TCP/IP. Requiere reiniciar.",
            (context, token) => Run(host, context, token, "netsh", "int ip reset"), changesSystem: true,
            confirmation: "Se restablecerá la pila TCP/IP y será necesario reiniciar Windows."),
        new ModuleAction("Mostrar configuración DNS", "Muestra los servidores DNS de cada adaptador.",
            (context, token) => Run(host, context, token, "netsh", "interface ip show dns")),
        new ModuleAction("Configurar servidores DNS", "Asigna un DNS principal y, opcionalmente, uno secundario.",
            (context, token) => SetDnsAsync(host, context, token), changesSystem: true,
            confirmation: "Cambiar el DNS puede interrumpir la resolución de nombres si los servidores no son válidos."),
        new ModuleAction("Probar conectividad a 1.1.1.1", "Ejecuta Test-NetConnection sin modificar la configuración.",
            (context, token) => Run(host, context, token, "powershell",
                "-NoProfile -Command \"Test-NetConnection -ComputerName 1.1.1.1\""))
    };

    private static async Task ReleaseRenewAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        await context.RunCommandAsync("ipconfig", "/release", token);
        await context.RunCommandAsync("ipconfig", "/renew", token);
    }

    private static async Task SetDnsAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        await context.RunCommandAsync("netsh", "interface ip show dns", token);
        var adapter = await context.PromptAsync("Configurar DNS", "Nombre exacto del adaptador (por ejemplo, Ethernet o Wi-Fi):");
        if (adapter is null) return;
        var primary = await context.PromptAsync("Configurar DNS", "Servidor DNS principal (por ejemplo, 1.1.1.1):");
        if (primary is null) return;
        if (!IsValidIp(primary)) throw new ArgumentException("El DNS principal debe ser una dirección IPv4 válida.");
        var secondary = await context.PromptAsync("Configurar DNS", "Servidor DNS secundario (opcional):", allowEmpty: true);
        if (secondary is null) return;
        if (!string.IsNullOrWhiteSpace(secondary) && !IsValidIp(secondary))
            throw new ArgumentException("El DNS secundario debe ser una dirección IPv4 válida.");

        if (!await context.ConfirmAsync("Confirmar DNS", $"Se cambiará el DNS de «{adapter}» a {primary} {secondary}. Una configuración incorrecta puede interrumpir la conectividad y sesiones remotas. ¿Continuar?")) return;
        await context.RunArgumentsAsync("netsh", new[] { "interface", "ip", "set", "dns", $"name={adapter}", "static", primary, "primary" }, token);
        if (!string.IsNullOrWhiteSpace(secondary))
            await context.RunArgumentsAsync("netsh", new[] { "interface", "ip", "add", "dns", $"name={adapter}", secondary, "index=2" }, token);
        if (!host.DryRun) context.WriteLine($"DNS configurado en «{adapter}».", OutputKind.Success);
    }

    private static bool IsValidIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return false;
        return IPAddress.TryParse(ip.Trim(), out var address) && address.AddressFamily == AddressFamily.InterNetwork;
    }

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
