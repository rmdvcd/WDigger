using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WDigger.Diagnostics;
using WDigger.Models;
using WDigger.UI;

namespace WDigger.Views;

/// <summary>
/// Dashboard: system summary gathered from WMI, quick navigation actions and
/// portable-data locations. Refresh is best effort: WMI failures degrade to
/// "n/a" instead of blocking the UI.
/// </summary>
public sealed partial class DashboardPage : UserControl
{
    private readonly ShellWindow _shell;
    private readonly ObservableCollection<SensorSummaryItem> _sensors = new();
    private readonly ObservableCollection<DiagnosticSummaryItem> _diagnostics = new();

    public DashboardPage(ShellWindow shell)
    {
        _shell = shell;
        InitializeComponent();

        DataFolderText.Text = _shell.Host.BaseDirectory;
        LogFolderText.Text = _shell.Host.Logger.LogFilePath;
        JournalFolderText.Text = _shell.Host.Operations.IsPersistent
            ? _shell.Host.Operations.DirectoryPath
            : "No disponible; las operaciones reales están bloqueadas.";
        ConfigFolderText.Text = _shell.Host.ConfigDirectory;

        DryRunBanner.IsOpen = _shell.Host.DryRun;
        SensorSummary.ItemsSource = _sensors;
        DiagnosticSummary.ItemsSource = _diagnostics;
        LoadDiagnosticSummary();

        _ = LoadAsync();
    }

    internal void SetDryRunBanner(bool dryRun) => DryRunBanner.IsOpen = dryRun;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private async void OpenSensorsButton_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("hardware");

    private async Task LoadAsync()
    {
        OsValue.Text = "Consultando...";
        CpuValue.Text = "Consultando...";
        MemoryValue.Text = "Consultando...";
        DiskValue.Text = "Consultando...";

        try
        {
            var inventory = await Task.Run(() => _shell.Host.Inventory.GetInventory());
            var disk = await Task.Run(() => _shell.Host.Inventory.GetSystemDriveSpace());

            OsValue.Text = Value(inventory, "Operating system", "Name");
            var build = Value(inventory, "Operating system", "Build");
            OsSub.Text = build == "n/a" ? string.Empty : $"Compilación {build}";

            CpuValue.Text = Value(inventory, "CPU", "Name");
            var cores = Value(inventory, "CPU", "Cores");
            var logical = Value(inventory, "CPU", "Logical processors");
            CpuSub.Text = $"{cores} núcleos · {logical} hilos";

            MemoryValue.Text = Value(inventory, "Computer system", "Total memory");
            var systemType = Value(inventory, "Computer system", "System type");
            MemorySub.Text = systemType == "n/a" ? string.Empty : systemType;

            if (disk is { } space)
            {
                var used = space.TotalGB - space.FreeGB;
                DiskValue.Text = $"{space.FreeGB:F0} GB libres · {space.TotalGB:F0} GB";
                DiskUsedBar.Value = space.TotalGB > 0 ? used / space.TotalGB * 100.0 : 0;
            }
            else
            {
                DiskValue.Text = "n/a";
                DiskUsedBar.Value = 0;
            }

            // Cargar sensores resumen (mejor esfuerzo, sin bloquear)
            _sensors.Clear();
            try
            {
                var readings = await Task.Run(() => _shell.Host.Sensors.ReadAll());
                var important = readings
                    .Where(r => r.Value is not null)
                    .Where(r =>
                        r.SensorType.Equals("Temperature", StringComparison.OrdinalIgnoreCase) ||
                        r.SensorType.Equals("Load", StringComparison.OrdinalIgnoreCase) ||
                        r.SensorType.Equals("Clock", StringComparison.OrdinalIgnoreCase) ||
                        r.SensorType.Equals("Fan", StringComparison.OrdinalIgnoreCase))
                    .Take(8)
                    .ToList();

                foreach (var r in important)
                {
                    _sensors.Add(new SensorSummaryItem
                    {
                        Label = $"{r.Hardware} · {r.SensorName}",
                        Value = r.Value ?? "n/a",
                        Unit = r.Unit
                    });
                }
            }
            catch
            {
                // Sensores son opcionales; no bloquear el dashboard.
            }
        }
        catch (Exception exception)
        {
            _shell.AppendOutput($"No se pudo actualizar el inventario: {exception.Message}", OutputKind.Warning);
            OsValue.Text = "n/a";
            CpuValue.Text = "n/a";
            MemoryValue.Text = "n/a";
            DiskValue.Text = "n/a";
        }
    }

    private static string Value(IReadOnlyList<ComponentInfo> inventory, string category, string item)
        => inventory.FirstOrDefault(row =>
                row.Category.Equals(category, StringComparison.OrdinalIgnoreCase)
                && row.Item.Equals(item, StringComparison.OrdinalIgnoreCase))
               ?.Value is { Length: > 0 } value and not "n/a"
            ? value
            : "n/a";

    private void LoadDiagnosticSummary()
    {
        _diagnostics.Clear();
        var report = _shell.Host.LatestDiagnosticReport;
        if (report is null)
        {
            DiagnosticSummaryText.Text = "Aún no hay un análisis reciente. Ejecuta el diagnóstico antes de decidir reparar.";
            return;
        }

        var findings = report.Results
            .Where(result => result.Status is DiagnosticStatus.Error or DiagnosticStatus.Warning or DiagnosticStatus.Failed or DiagnosticStatus.AccessDenied)
            .Take(6)
            .ToList();
        DiagnosticSummaryText.Text = findings.Count == 0
            ? $"Análisis completado {report.CompletedAtUtc.ToLocalTime():g}. No hay hallazgos de error o advertencia; revisa las comprobaciones no disponibles."
            : $"Análisis completado {report.CompletedAtUtc.ToLocalTime():g}. Se muestran {findings.Count} hallazgo(s) priorizado(s).";
        foreach (var result in findings)
        {
            _diagnostics.Add(new DiagnosticSummaryItem
            {
                Header = $"[{DiagnosticReport.StatusText(result.Status)}] {result.Category} · {result.Title}",
                Summary = result.Summary,
                Recommendation = result.Recommendation
            });
        }
    }

    private void OpenDiagnosticsButton_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("diagnostics");

    private void QuickRepair_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("repair");
    private void QuickSensors_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("hardware");
    private void QuickSoftware_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("software");
    private void QuickDisk_Click(object sender, RoutedEventArgs e) => _shell.NavigateToTag("disk");

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_shell.Host.BaseDirectory);
    private void OpenLogsFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_shell.Host.LogDirectory);
    private void OpenConfigFolder_Click(object sender, RoutedEventArgs e) => OpenFolder(_shell.Host.ConfigDirectory);

    private static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // Explorer is always present on Windows; ignore failures.
        }
    }
}

/// <summary>Un resumen de sensor para mostrar en el dashboard.</summary>
public sealed class SensorSummaryItem
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public string Unit { get; set; } = "";
}

public sealed class DiagnosticSummaryItem
{
    public string Header { get; set; } = "";
    public string Summary { get; set; } = "";
    public string Recommendation { get; set; } = "";
}
