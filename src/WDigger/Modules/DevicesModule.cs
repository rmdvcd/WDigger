using System.Collections.Generic;
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
        new ModuleAction("Listar impresoras", "Muestra nombre, estado, puerto, uso compartido y impresora predeterminada.",
            (context, token) => ListPrintersAsync(host, context, token)),
        new ModuleAction("Agregar impresora de red", "Registra una impresora mediante su IP o nombre de host.",
            (context, token) => AddNetworkPrinterAsync(host, context, token), changesSystem: true),
        new ModuleAction("Quitar una impresora", "Elimina una impresora instalada por su nombre exacto.",
            (context, token) => RemovePrinterAsync(host, context, token), changesSystem: true),
        new ModuleAction("Listar dispositivos PnP", "Consulta nombre, estado, clase e identificador de cada dispositivo.",
            (context, token) => ListPnpDevicesAsync(host, context, token)),
        new ModuleAction("Habilitar o deshabilitar dispositivo", "Aplica Enable-PnpDevice o Disable-PnpDevice a un ID de instancia.",
            (context, token) => EnableDisableDeviceAsync(host, context, token), changesSystem: true,
            confirmation: "Deshabilitar un dispositivo esencial puede hacer que el equipo deje de responder.")
    };

    private static async Task ListPrintersAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        var rows = await Task.Run(() => WmiQuery(host,
            "SELECT Name, Status, PortName, Shared, Default FROM Win32_Printer"), token);
        context.ShowTable("Impresoras instaladas", new[] { "Nombre", "Estado", "Puerto", "Compartida", "Predeterminada" },
            rows.Select(row => new[]
            {
                Get(row, "Name"), Get(row, "Status"), Get(row, "PortName"), Get(row, "Shared"), Get(row, "Default")
            }));
        if (rows.Count == 0) context.WriteLine("No se encontraron impresoras.", OutputKind.Warning);
    }

    private static async Task AddNetworkPrinterAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var ip = await context.PromptAsync("Agregar impresora de red", "Dirección IP o nombre de host:");
        if (ip is null) return;
        var name = await context.PromptAsync("Agregar impresora de red", "Nombre para mostrar:", ip);
        if (name is null) return;
        await context.RunArgumentsAsync("rundll32.exe", new[] { "printui.dll,PrintUIEntry", "/if", "/b", name, "/r", $"IP_{ip}", "/m", "Generic Printer", "/z" }, token);
    }

    private static async Task RemovePrinterAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var name = await context.PromptAsync("Quitar impresora", "Nombre exacto de la impresora:");
        if (name is null) return;
        if (!await context.ConfirmAsync("Quitar impresora", $"Se quitará la impresora «{name}». ¿Continuar?")) return;
        await context.RunArgumentsAsync("rundll32.exe", new[] { "printui.dll,PrintUIEntry", "/dl", "/n", name }, token);
    }

    private static async Task ListPnpDevicesAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Consultando dispositivos PnP...");
        var rows = await Task.Run(() => WmiQuery(host,
            "SELECT Name, Status, ClassGuid, PNPDeviceID FROM Win32_PnPEntity WHERE Name IS NOT NULL"), token);
        token.ThrowIfCancellationRequested();
        context.ShowTable("Dispositivos Plug and Play", new[] { "Nombre", "Estado", "Clase", "ID de dispositivo" },
            rows.Select(row => new[]
            {
                Get(row, "Name"), Get(row, "Status"), Get(row, "ClassGuid"), Get(row, "PNPDeviceID")
            }));
        context.WriteLine($"{rows.Count} dispositivos encontrados.", OutputKind.Success);
    }

    private static async Task EnableDisableDeviceAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var instanceId = await context.PromptAsync("Habilitar o deshabilitar", "ID de instancia (PNPDeviceID):");
        if (instanceId is null) return;
        var choice = await context.ChooseAsync("Acción del dispositivo", "Selecciona la acción:",
            new[] { "Habilitar", "Deshabilitar" }, item => item);
        if (choice is null) return;

        if (!await context.ConfirmAsync("Confirmar dispositivo",
            $"Se va a {choice.ToLowerInvariant()} el dispositivo {instanceId}. Si es esencial, el equipo podría dejar de responder. ¿Continuar?")) return;
        var command = choice == "Habilitar" ? "Enable-PnpDevice" : "Disable-PnpDevice";
        var escaped = instanceId.Replace("'", "''");
        await context.RunPowerShellAsync($"{command} -InstanceId '{escaped}' -Confirm:$false", token);
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
                    foreach (PropertyData property in item.Properties)
                    {
                        row[property.Name] = property.Value;
                    }
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
