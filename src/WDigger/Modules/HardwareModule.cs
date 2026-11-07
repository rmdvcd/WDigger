using WDigger.Core;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Modules;

public sealed class HardwareModule : IModule
{
    public string Name => "Hardware";
    public string Description => "Inventario WMI, sensores en tiempo real, estado de discos y batería.";

    public IReadOnlyList<ModuleAction> CreateActions(AppHost host) => new[]
    {
        new ModuleAction(
            "Ver inventario de componentes",
            "Windows, CPU, GPU, memoria, discos, placa base, batería y adaptadores de red.",
            (context, token) => ShowInventoryAsync(host, context, token)),
        new ModuleAction(
            "Supervisar sensores en tiempo real",
            "Actualiza temperaturas, cargas, ventiladores, voltajes y relojes cada dos segundos. Usa Cancelar para detener.",
            (context, token) => ShowSensorsAsync(host, context, token)),
        new ModuleAction(
            "Estado de discos y batería",
            "Consulta el estado físico de los discos y la carga de batería mediante WMI.",
            (context, token) => ShowSmartAndBatteryAsync(host, context, token))
    };

    private static async Task ShowInventoryAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Consultando inventario WMI...");
        var inventory = await Task.Run(() => host.Inventory.GetInventory(), token);
        token.ThrowIfCancellationRequested();
        context.ShowTable(
            "Inventario de componentes",
            new[] { "Componente", "Detalle", "Valor" },
            inventory.Select(item => new[] { item.Category, item.Item, NotAvailable(item.Value) }));
        context.WriteLine($"Inventario completado: {inventory.Count} elementos.", OutputKind.Success);
    }

    private static async Task ShowSensorsAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        context.WriteLine("Monitor iniciado. Pulsa Cancelar para detener la actualización.", OutputKind.Muted);
        context.ReportProgress();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var readings = await Task.Run(() => host.Sensors.ReadAll(), token);
            var visible = readings
                .Where(reading => reading.Value is not null)
                .OrderBy(reading => reading.HardwareType, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reading => reading.Hardware, StringComparer.OrdinalIgnoreCase)
                .ThenBy(reading => reading.SensorName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            context.ShowTable(
                $"Sensores en tiempo real · {DateTime.Now:HH:mm:ss}",
                new[] { "Hardware", "Sensor", "Tipo", "Valor", "Unidad" },
                visible.Select(reading => new[]
                {
                    reading.Hardware,
                    reading.SensorName,
                    reading.SensorType,
                    reading.Value ?? "n/a",
                    reading.Unit
                }));
            context.SetStatus($"Sensores actualizados · {visible.Count} lecturas · Cancelar para detener");
            await Task.Delay(TimeSpan.FromSeconds(2), token);
        }
    }

    private static async Task ShowSmartAndBatteryAsync(
        AppHost host, ModuleExecutionContext context, CancellationToken token)
    {
        context.SetStatus("Consultando discos y batería...");
        var items = await Task.Run(() => host.Inventory.GetSmartAndBattery(), token);
        token.ThrowIfCancellationRequested();
        context.ShowTable(
            "Estado de discos y batería",
            new[] { "Componente", "Detalle", "Valor" },
            items.Select(item => new[] { item.Category, item.Item, NotAvailable(item.Value) }));
        context.WriteLine("Las temperaturas SMART detalladas están disponibles en el monitor de sensores.", OutputKind.Muted);
    }

    private static string NotAvailable(string? value) => string.IsNullOrWhiteSpace(value) ? "n/a" : value;
}
