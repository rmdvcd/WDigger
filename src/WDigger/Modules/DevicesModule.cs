using System.Management;
using WDigger.Core;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class DevicesModule : IModule
{
    public string Name => "Dispositivos";
    public string Description => "Impresoras, dispositivos Plug and Play y activación de hardware.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Listar impresoras", "Muestra nombre, estado, puerto, uso compartido y predeterminada.",
            (context, token) => ListPrintersAsync(host, context, token)),
        new ModuleAction("Agregar impresora de red", "Registra una impresora mediante una dirección o host validado.",
            AddNetworkPrinterAsync, changesSystem: true, risk: OperationRisk.Moderate),
        new ModuleAction("Quitar una impresora", "Elimina una impresora instalada por su nombre exacto.",
            RemovePrinterAsync, changesSystem: true, risk: OperationRisk.Moderate),
        new ModuleAction("Listar dispositivos PnP", "Consulta nombre, estado, clase e identificador de cada dispositivo.",
            (context, token) => ListPnpDevicesAsync(host, context, token)),
        new ModuleAction("Habilitar o deshabilitar dispositivo", "Aplica una acción a un ID de instancia existente de Plug and Play.",
            EnableDisableDeviceAsync, changesSystem: true,
            confirmation: "Deshabilitar un dispositivo esencial puede hacer que el equipo deje de responder.", risk: OperationRisk.High)
    };

    private static async Task ListPrintersAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var rows = await Task.Run(() => WmiQuery(host,
            "SELECT Name, Status, PortName, Shared, Default FROM Win32_Printer"), token);
        context.ShowTable("Impresoras instaladas", new[] { "Nombre", "Estado", "Puerto", "Compartida", "Predeterminada" },
            rows.Select(row => new[] { Get(row, "Name"), Get(row, "Status"), Get(row, "PortName"), Get(row, "Shared"), Get(row, "Default") }));
        if (rows.Count == 0) context.WriteLine("No se encontraron impresoras.", OutputKind.Warning);
    }

    private static async Task AddNetworkPrinterAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var endpoint = await context.PromptAsync("Agregar impresora de red", "Dirección IP o nombre de host:");
        if (endpoint is null) return;
        endpoint = RequirePrinterEndpoint(endpoint);
        var name = await context.PromptAsync("Agregar impresora de red", "Nombre para mostrar:", endpoint);
        if (name is null) return;
        name = RequireDisplayName(name);
        if (!await context.ConfirmAsync("Confirmar impresora", $"Se registrará «{name}» en el puerto IP_{endpoint} con el controlador genérico. ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Rundll32,
            "printui.dll,PrintUIEntry", "/if", "/b", name, "/r", $"IP_{endpoint}", "/m", "Generic Printer", "/z"), token);
    }

    private static async Task RemovePrinterAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var name = await context.PromptAsync("Quitar impresora", "Nombre exacto de la impresora:");
        if (name is null) return;
        name = RequireDisplayName(name);
        if (!await context.ConfirmAsync("Quitar impresora", $"Se quitará la impresora «{name}». ¿Continuar?")) return;
        await context.RunCommandAsync(CommandSpec.Modify(WindowsTool.Rundll32,
            "printui.dll,PrintUIEntry", "/dl", "/n", name), token);
    }

    private static async Task ListPnpDevicesAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Consultando dispositivos PnP...");
        var rows = await Task.Run(() => WmiQuery(host,
            "SELECT Name, Status, ClassGuid, PNPDeviceID FROM Win32_PnPEntity WHERE Name IS NOT NULL"), token);
        token.ThrowIfCancellationRequested();
        context.ShowTable("Dispositivos Plug and Play", new[] { "Nombre", "Estado", "Clase", "ID de dispositivo" },
            rows.Select(row => new[] { Get(row, "Name"), Get(row, "Status"), Get(row, "ClassGuid"), Get(row, "PNPDeviceID") }));
        context.WriteLine($"{rows.Count} dispositivos encontrados.", OutputKind.Success);
    }

    private static async Task EnableDisableDeviceAsync(ModuleExecutionContext context, CancellationToken token)
    {
        var instanceId = await context.PromptAsync("Habilitar o deshabilitar", "ID de instancia (PNPDeviceID):");
        if (instanceId is null) return;
        instanceId = instanceId.Trim();
        if (instanceId.Length is 0 or > 512 || instanceId.Any(char.IsControl)) throw new ArgumentException("El ID de instancia no es válido.");
        var exists = await Task.Run(() => PnpDeviceExists(instanceId), token);
        if (!exists) throw new ArgumentException("El ID de instancia ya no corresponde a un dispositivo PnP existente.");
        var choice = await context.ChooseAsync("Acción del dispositivo", "Selecciona la acción:",
            new[] { "Habilitar", "Deshabilitar" }, item => item);
        if (choice is null) return;

        if (!await context.ConfirmAsync("Confirmar dispositivo",
            $"Se va a {choice.ToLowerInvariant()} el dispositivo {instanceId}. Si es esencial, el equipo podría dejar de responder. ¿Continuar?")) return;
        var script = choice == "Habilitar" ? PowerShellScriptId.EnablePnpDevice : PowerShellScriptId.DisablePnpDevice;
        await context.RunPowerShellAsync(script, new[] { instanceId }, token);
        context.RequireManualAction(
            $"Se solicitó {choice.ToLowerInvariant()} el dispositivo sin error de proceso.",
            "Comprueba físicamente el dispositivo y conserva una vía de entrada alternativa antes de reiniciar o cerrar sesión.");
    }

    private static bool PnpDeviceExists(string instanceId)
    {
        using var searcher = new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity");
        using var collection = searcher.Get();
        return collection.Cast<ManagementObject>().Any(item =>
        {
            using (item) return string.Equals(item["PNPDeviceID"]?.ToString(), instanceId, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string RequirePrinterEndpoint(string value)
    {
        var endpoint = value.Trim();
        if (endpoint.Length is 0 or > 253 || endpoint.Any(char.IsWhiteSpace) || endpoint.Any(char.IsControl))
            throw new ArgumentException("La dirección o host de impresora no es válido.");
        if (System.Net.IPAddress.TryParse(endpoint, out _) || Uri.CheckHostName(endpoint) != UriHostNameType.Unknown) return endpoint;
        throw new ArgumentException("La dirección o host de impresora no es válido.");
    }

    private static string RequireDisplayName(string value)
    {
        var name = value.Trim();
        if (name.Length is 0 or > 128 || name.Any(char.IsControl)) throw new ArgumentException("El nombre no es válido.");
        return name;
    }

    private static List<Dictionary<string, object?>> WmiQuery(AppHost host, string wql)
    {
        var results = new List<Dictionary<string, object?>>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                using (item)
                {
                    var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (PropertyData property in item.Properties) row[property.Name] = property.Value;
                    results.Add(row);
                }
            }
        }
        catch (Exception exception)
        {
            host.Logger.Warn($"WMI query failed: {exception}");
            throw new InvalidOperationException($"La consulta WMI falló: {exception.Message}", exception);
        }
        return results;
    }

    private static string Get(Dictionary<string, object?> row, string name)
        => row.TryGetValue(name, out var value) && value is not null ? value.ToString() ?? "n/a" : "n/a";
}
