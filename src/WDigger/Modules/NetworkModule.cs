using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class NetworkModule : IModule
{
    public string Name => "Red";
    public string Description => "DNS, renovación IP, Winsock, TCP/IP y pruebas de conectividad.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Vaciar caché DNS", "Ejecuta ipconfig /flushdns.",
            (context, token) => context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Ipconfig, "/flushdns"), token), changesSystem: true,
            confirmation: "Se borrará la caché DNS local; las aplicaciones pueden volver a consultar sus servidores DNS.",
            allowedInElevatedSession: true, risk: OperationRisk.Low),
        new ModuleAction("Liberar y renovar IP", "Libera y renueva la configuración DHCP de los adaptadores.",
            ReleaseRenewAsync, changesSystem: true,
            confirmation: "La conexión de red se interrumpirá temporalmente.", risk: OperationRisk.Moderate),
        new ModuleAction("Restablecer Winsock", "Restablece el catálogo Winsock. Requiere reiniciar.",
            ResetWinsockAsync, changesSystem: true,
            confirmation: "Se restablecerá Winsock; las conexiones y sesiones remotas pueden interrumpirse y será necesario reiniciar Windows.",
            risk: OperationRisk.High),
        new ModuleAction("Restablecer TCP/IP", "Restablece la pila TCP/IP. Requiere reiniciar.",
            ResetTcpIpAsync, changesSystem: true,
            confirmation: "Se restablecerá la pila TCP/IP y será necesario reiniciar Windows.",
            risk: OperationRisk.High),
        new ModuleAction("Mostrar configuración DNS", "Muestra los servidores DNS de cada adaptador.",
            (context, token) => context.RunCommandAsync(CommandSpec.Read(WindowsTool.Netsh, "interface", "ip", "show", "dns"), token)),
        new ModuleAction("Configurar servidores DNS", "Asigna un DNS principal y, opcionalmente, uno secundario.",
            SetDnsAsync, changesSystem: true,
            confirmation: "Cambiar el DNS puede interrumpir la resolución de nombres si los servidores no son válidos.", risk: OperationRisk.Moderate),
        new ModuleAction("Trazar conectividad a 1.1.1.1", "Solicita un salto a 1.1.1.1 sin modificar la configuración.",
            (context, token) => context.RunCommandAsync(CommandSpec.Read(WindowsTool.Tracert, "-d", "-h", "1", "1.1.1.1"), token))
    };

    private static async Task ReleaseRenewAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Ipconfig, "/release"), token);
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Ipconfig, "/renew"), token);
    }

    private static async Task ResetWinsockAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh, "winsock", "reset"), token);
        context.RequireRestart("Winsock se restableció; reinicia Windows para aplicar el cambio.");
    }

    private static async Task ResetTcpIpAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh, "int", "ip", "reset"), token);
        context.RequireRestart("La pila TCP/IP se restableció; reinicia Windows para aplicar el cambio.");
    }

    private static async Task SetDnsAsync(ModuleExecutionContext context, CancellationToken token)
    {
        await context.RunCommandAsync(CommandSpec.Read(WindowsTool.Netsh, "interface", "ip", "show", "dns"), token);
        var adapter = await context.PromptAsync("Configurar DNS", "Nombre exacto del adaptador (por ejemplo, Ethernet o Wi-Fi):");
        if (adapter is null) return;
        adapter = RequireKnownAdapter(adapter);
        var primary = await context.PromptAsync("Configurar DNS", "Servidor DNS principal (por ejemplo, 1.1.1.1):");
        if (primary is null) return;
        if (!IsValidIp(primary)) throw new ArgumentException("El DNS principal debe ser una dirección IPv4 válida.");
        var secondary = await context.PromptAsync("Configurar DNS", "Servidor DNS secundario (opcional):", allowEmpty: true);
        if (secondary is null) return;
        if (!string.IsNullOrWhiteSpace(secondary) && !IsValidIp(secondary))
        {
            throw new ArgumentException("El DNS secundario debe ser una dirección IPv4 válida.");
        }

        if (!await context.ConfirmAsync("Confirmar DNS", $"Se cambiará el DNS de «{adapter}» a {primary} {secondary}. Una configuración incorrecta puede interrumpir la conectividad y sesiones remotas. ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh,
            "interface", "ip", "set", "dns", $"name={adapter}", "static", primary, "primary"), token);
        if (!string.IsNullOrWhiteSpace(secondary))
        {
            await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Netsh,
                "interface", "ip", "add", "dns", $"name={adapter}", secondary, "index=2"), token);
        }
        if (!context.IsSimulation) context.WriteLine($"DNS configurado en «{adapter}».", OutputKind.Success);
    }

    private static string RequireKnownAdapter(string value)
    {
        var name = value.Trim();
        if (name.Length is 0 or > 128 || name.Any(char.IsControl))
        {
            throw new ArgumentException("El nombre del adaptador no es válido.");
        }
        var found = NetworkInterface.GetAllNetworkInterfaces().Any(item =>
            item.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
            string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        if (!found) throw new ArgumentException("El adaptador indicado ya no existe. Vuelve a consultar la configuración DNS.");
        return name;
    }

    private static bool IsValidIp(string? ip)
        => !string.IsNullOrWhiteSpace(ip) && IPAddress.TryParse(ip.Trim(), out var address)
            && address.AddressFamily == AddressFamily.InterNetwork;
}
