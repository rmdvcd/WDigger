using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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

    public DashboardPage(ShellWindow shell)
    {
        _shell = shell;
        InitializeComponent();

        DataFolderText.Text = _shell.Host.BaseDirectory;
        LogFolderText.Text = _shell.Host.Logger.LogFilePath;
        ConfigFolderText.Text = _shell.Host.ConfigDirectory;

        DryRunBanner.IsOpen = _shell.Host.DryRun;
        _ = LoadAsync();
    }

    internal void SetDryRunBanner(bool dryRun) => DryRunBanner.IsOpen = dryRun;

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadAsync();

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
