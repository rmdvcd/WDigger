using System.Management;
using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class EventLogBiosModule : IModule
{
    public string Name => "Eventos y BIOS";
    public string Description => "Registros de eventos de Windows e información de BIOS, placa base y firmware.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction("Listar registros de eventos", "Enumera los registros disponibles mediante wevtutil.",
            (context, token) => Run(host, context, token, "wevtutil", "el")),
        new ModuleAction("Consultar últimos 20 eventos", "Muestra los eventos más recientes de System, Application u otro registro.",
            (context, token) => QueryEventLogAsync(host, context, token)),
        new ModuleAction("Información de BIOS", "Consulta fabricante, versión y fecha de la BIOS.",
            (context, token) => ShowWmiAsync(host, context, token,
                "SELECT Name, Version, ReleaseDate, Manufacturer, SMBIOSBIOSVersion FROM Win32_BIOS", "BIOS")),
        new ModuleAction("Información de placa base", "Consulta fabricante, producto, número de serie y versión.",
            (context, token) => ShowWmiAsync(host, context, token,
                "SELECT Manufacturer, Product, SerialNumber, Version FROM Win32_BaseBoard", "Placa base")),
        new ModuleAction("Información UEFI / firmware", "Muestra los datos de firmware expuestos por Win32_BIOS.",
            (context, token) => ShowWmiAsync(host, context, token,
                "SELECT Name, Manufacturer, Version, ReleaseDate FROM Win32_BIOS", "Firmware"))
    };

    private static async Task QueryEventLogAsync(AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        _ = host;
        var log = await context.PromptAsync("Consultar eventos", "Nombre del registro (System, Application, Security...):", "System");
        if (log is not null)
        {
            await context.RunArgumentsAsync("wevtutil", new[] { "qe", log, "/c:20", "/f:text", "/rd:true" }, token);
        }
    }

    private static async Task ShowWmiAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string wql, string title)
    {
        var rows = await Task.Run(() => QueryWmi(wql), token);
        token.ThrowIfCancellationRequested();
        if (rows.Count == 0)
        {
            context.WriteLine("La consulta WMI no devolvió datos.", OutputKind.Warning);
            return;
        }
        context.ShowTable(title, new[] { "Propiedad", "Valor" }, rows);
    }

    private static List<string[]> QueryWmi(string wql)
    {
        var rows = new List<string[]>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            using var collection = searcher.Get();
            foreach (ManagementObject item in collection)
            {
                using (item)
                {
                    foreach (PropertyData property in item.Properties)
                    {
                        rows.Add(new[] { property.Name, property.Value?.ToString() ?? "n/a" });
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // WMI failures are logged and degraded gracefully.
            return new List<string[]> { new[] { "Error", exception.Message } };
        }
        return rows;
    }

    private static Task<CommandResult> Run(
        AppHost host, ModuleExecutionContext context, CancellationToken token, string fileName, string arguments)
    {
        _ = host;
        return context.RunCommandAsync(fileName, arguments, token);
    }
}
