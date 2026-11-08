using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WDigger.Core;
using WDigger.Modules;
using WDigger.Views;

namespace WDigger.UI;

/// <summary>
/// Main window: custom title bar, NavigationView with every tool, dashboard,
/// module pages and an integrated output/results pane (live command output,
/// tabular results, progress and cancellation).
/// </summary>
public sealed partial class ShellWindow : Window
{
    internal const string AppVersion = "2.0.0";

    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyDictionary<string, ModuleCatalog.Entry> _moduleIndex;
    private readonly Dictionary<OutputKind, SolidColorBrush> _outputBrushes;
    private readonly List<string> _outputPlain = new();

    private CancellationTokenSource? _operationCts;
    private NavigationViewItem? _lastContentItem;
    private bool _isBusy;
    private bool _closing;
    private bool _outputCollapsed;

    public DialogService Dialogs { get; }

    internal AppHost Host => _host;

    public ShellWindow()
    {
        InitializeComponent();

        Title = "WDigger";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        try
        {
            SystemBackdrop = new MicaBackdrop();
        }
        catch
        {
            // Mica requires Windows 11; Windows 10 keeps the default background.
        }

        AppWindow.Resize(new SizeInt32(1440, 900));

        Dialogs = new DialogService(this);
        _host = AppHost.Create(AppContext.BaseDirectory);
        _settings = AppSettings.Load(_host.ConfigDirectory);
        _moduleIndex = ModuleCatalog.Create().ToDictionary(entry => entry.Tag, StringComparer.OrdinalIgnoreCase);

        var commandLine = Environment.GetCommandLineArgs();
        var dryRunArgument = commandLine.Any(argument =>
            argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-dryrun", StringComparison.OrdinalIgnoreCase));
        _host.DryRun = dryRunArgument || _settings.DryRun;

        _outputBrushes = new Dictionary<OutputKind, SolidColorBrush>
        {
            [OutputKind.Success] = Brush(0x6C, 0xCB, 0x5F),
            [OutputKind.Warning] = Brush(0xFF, 0xC8, 0x3D),
            [OutputKind.Error] = Brush(0xFF, 0x99, 0xA4),
            [OutputKind.Command] = Brush(0x8A, 0xC9, 0xFF),
            [OutputKind.Muted] = Brush(0x8A, 0x97, 0xA5)
        };

        ApplyTheme(_settings.Theme, persist: false);
        UpdateThemeLabel();

        DryRunSwitch.IsOn = _host.DryRun;
        DryRunSwitch.Toggled += DryRunSwitch_Toggled;

        Closed += OnClosed;

        var dashboard = NavView.MenuItems.OfType<NavigationViewItem>()
            .First(item => string.Equals(item.Tag as string, "dashboard", StringComparison.OrdinalIgnoreCase));
        _lastContentItem = dashboard;
        NavView.SelectedItem = dashboard;
        PageHost.Content = new DashboardPage(this);

        ShowWelcome();
    }

    // ───────────────────────────── navigation ─────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        var tag = item.Tag as string;

        if (tag == "theme")
        {
            CycleTheme();
            RestoreContentSelection();
            return;
        }

        if (tag == "about")
        {
            _ = ShowAboutAsync();
            RestoreContentSelection();
            return;
        }

        _lastContentItem = item;

        if (tag == "dashboard")
        {
            PageHost.Content = new DashboardPage(this);
            return;
        }

        if (tag is not null && _moduleIndex.TryGetValue(tag, out var entry))
        {
            PageHost.Content = new ModulePage(this, entry);
        }
    }

    /// <summary>Navigate programmatically (used by dashboard quick actions).</summary>
    public void NavigateToTag(string tag)
    {
        var item = NavView.MenuItems.OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(entry => string.Equals(entry.Tag as string, tag, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }
    }

    private void RestoreContentSelection()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_lastContentItem is not null && !ReferenceEquals(NavView.SelectedItem, _lastContentItem))
            {
                NavView.SelectedItem = _lastContentItem;
            }
        });
    }

    // ───────────────────────────── theme ─────────────────────────────

    private void CycleTheme()
    {
        var next = _settings.Theme switch
        {
            "System" => "Dark",
            "Dark" => "Light",
            _ => "System"
        };
        ApplyTheme(next, persist: true);
        AppendOutput($"Tema de la interfaz: {ThemeLabel(next)}.", OutputKind.Muted);
    }

    private void ApplyTheme(string theme, bool persist)
    {
        _settings.Theme = theme;
        RootGrid.RequestedTheme = theme switch
        {
            "Light" => ElementTheme.Light,
            "Dark" => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        if (persist)
        {
            _settings.Save(_host.ConfigDirectory);
        }

        UpdateThemeLabel();
    }

    private void UpdateThemeLabel() => ThemeItem.Content = $"Tema: {ThemeLabel(_settings.Theme)}";

    private static string ThemeLabel(string theme) => theme switch
    {
        "Light" => "Claro",
        "Dark" => "Oscuro",
        _ => "Automático"
    };

    // ───────────────────────────── dry-run ─────────────────────────────

    private void DryRunSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        _host.DryRun = DryRunSwitch.IsOn;
        _settings.DryRun = _host.DryRun;
        _settings.Save(_host.ConfigDirectory);
        _host.Logger.Info($"Dry-run mode changed: {_host.DryRun}");

        if (PageHost.Content is DashboardPage dashboard)
        {
            dashboard.SetDryRunBanner(_host.DryRun);
        }

        AppendOutput(_host.DryRun
            ? "Modo simulación habilitado: las acciones de modificación solo se registrarán."
            : "Modo real habilitado: las acciones confirmadas modificarán el sistema.",
            _host.DryRun ? OutputKind.Warning : OutputKind.Success);
    }

    // ───────────────────────────── output pane ─────────────────────────────

    public void AppendOutput(string message, OutputKind kind = OutputKind.Info)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendOutput(message, kind));
            return;
        }

        if (_closing)
        {
            return;
        }

        try
        {
            _outputPlain.Add(message);
            if (_outputPlain.Count > 1500)
            {
                _outputPlain.RemoveRange(0, _outputPlain.Count - 1500);
            }

            var paragraph = new Paragraph();
            var run = new Run { Text = message.Length == 0 ? " " : message };
            if (kind != OutputKind.Info && _outputBrushes.TryGetValue(kind, out var brush))
            {
                run.Foreground = brush;
            }

            paragraph.Inlines.Add(run);
            OutputText.Blocks.Add(paragraph);
            while (OutputText.Blocks.Count > 800)
            {
                OutputText.Blocks.Remove(OutputText.Blocks[0]);
            }

            OutputScroller.UpdateLayout();
            OutputScroller.ChangeView(null, OutputScroller.ScrollableHeight, null, disableAnimation: true);
        }
        catch
        {
            // The window may be closing; output is best effort.
        }
    }

    public void SetStatus(string message)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => SetStatus(message));
            return;
        }

        try
        {
            StatusText.Text = message;
        }
        catch
        {
            // Window closing.
        }
    }

    public void ReportProgress(int? percent = null)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ReportProgress(percent));
            return;
        }

        try
        {
            if (!percent.HasValue)
            {
                ActivityProgress.IsIndeterminate = true;
                ActivityProgress.Visibility = Visibility.Visible;
                return;
            }

            var clamped = Math.Clamp(percent.Value, 0, 100);
            ActivityProgress.IsIndeterminate = false;
            ActivityProgress.Value = clamped;
            ActivityProgress.Visibility = clamped >= 100 ? Visibility.Collapsed : Visibility.Visible;
        }
        catch
        {
            // Window closing.
        }
    }

    public void ShowActivity()
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(ShowActivity);
            return;
        }

        try
        {
            SetOutputCollapsed(false);
            SwitchOutputView(showTable: false);
        }
        catch
        {
            // Window closing.
        }
    }

    public void ShowTable(string title, IReadOnlyList<string> columns, IEnumerable<string[]> rows)
    {
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowTable(title, columns, rows));
            return;
        }

        if (_closing)
        {
            return;
        }

        try
        {
            TableHost.Children.Clear();
            TableHost.Children.Add(TableRenderer.Create(title, columns, rows));
            SetOutputCollapsed(false);
            SwitchOutputView(showTable: true);
        }
        catch
        {
            // Window closing.
        }
    }

    private void SwitchOutputView(bool showTable)
    {
        OutputScroller.Visibility = showTable ? Visibility.Collapsed : Visibility.Visible;
        TableScroller.Visibility = showTable ? Visibility.Visible : Visibility.Collapsed;
        OutputViewButton.IsChecked = !showTable;
        TableViewButton.IsChecked = showTable;
    }

    private void SetOutputCollapsed(bool collapsed)
    {
        _outputCollapsed = collapsed;
        OutputBodyRow.Height = collapsed ? new GridLength(0) : new GridLength(264);
        OutputBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        if (OutputCollapseButton.Icon is FontIcon chevron)
        {
            chevron.Glyph = collapsed ? "\uE70E" : "\uE70D";
        }
    }

    private void OutputViewButton_Click(object sender, RoutedEventArgs e) => SwitchOutputView(showTable: false);
    private void TableViewButton_Click(object sender, RoutedEventArgs e) => SwitchOutputView(showTable: true);
    private void OutputCollapseButton_Click(object sender, RoutedEventArgs e) => SetOutputCollapsed(!_outputCollapsed);

    private void ClearOutputButton_Click(object sender, RoutedEventArgs e)
    {
        OutputText.Blocks.Clear();
        _outputPlain.Clear();
        TableHost.Children.Clear();
        SetStatus("Salida borrada.");
    }

    private async void CopyOutputButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(string.Join(Environment.NewLine, _outputPlain));
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            SetStatus("Salida copiada al portapapeles.");
        }
        catch (Exception exception)
        {
            AppendOutput($"No se pudo copiar la salida: {exception.Message}", OutputKind.Warning);
        }
    }

    private void CancelOperationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_operationCts is null || _operationCts.IsCancellationRequested)
        {
            return;
        }

        CancelOperationButton.IsEnabled = false;
        SetStatus("Cancelando; espera a que finalice el proceso...");
        _operationCts.Cancel();
    }

    // ───────────────────────────── action execution ─────────────────────────────

    public async Task RunActionAsync(ModuleAction action)
    {
        if (_isBusy)
        {
            return;
        }

        if (action.ChangesSystem && !_host.DryRun)
        {
            var message = action.Confirmation
                ?? "Esta acción puede modificar Windows. Revisa la descripción antes de continuar.";
            var confirmed = await Dialogs.ConfirmAsync(
                "Confirmar cambios en el sistema",
                $"{message}\n\n¿Quieres ejecutar «{action.Title}» en el sistema real?",
                "Ejecutar");
            if (!confirmed)
            {
                return;
            }
        }

        var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operationCts = operation;
        var context = new ModuleExecutionContext(this, _host);

        SetBusy(true, $"Ejecutando: {action.Title}...");
        AppendOutput(string.Empty, OutputKind.Muted);
        AppendOutput($"═══ {action.Title} ═══", OutputKind.Command);
        if (action.ChangesSystem && _host.DryRun)
        {
            AppendOutput("Modo simulación activo: no se aplicarán cambios reales.", OutputKind.Warning);
        }

        try
        {
            await action.Execute(context, operation.Token);
            if (!operation.IsCancellationRequested)
            {
                SetStatus("Acción finalizada");
                AppendOutput("Acción finalizada.", OutputKind.Success);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("Operación cancelada");
            AppendOutput("Operación cancelada por el usuario.", OutputKind.Warning);
        }
        catch (Exception exception)
        {
            _host.Logger.Error($"Action '{action.Title}' failed: {exception}");
            SetStatus("La acción produjo un error");
            AppendOutput(exception.ToString(), OutputKind.Error);
            await Dialogs.ShowMessageAsync("Error al ejecutar la acción", exception.Message);
        }
        finally
        {
            _operationCts = null;
            operation.Dispose();
            ReportProgress(100);
            SetBusy(false, "Listo");
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        NavView.IsEnabled = !busy;
        CancelOperationButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = busy;
        SetStatus(status);
    }

    // ───────────────────────────── misc ─────────────────────────────

    private void ShowWelcome()
    {
        AppendOutput($"WDigger {AppVersion} · WinUI 3 · Fluent Design", OutputKind.Success);
        AppendOutput($"Ubicación de datos: {_host.BaseDirectory}", OutputKind.Muted);
        AppendOutput($"Registro de auditoría: {_host.Logger.LogFilePath}", OutputKind.Muted);
        AppendOutput("Selecciona una herramienta en el panel lateral y pulsa Ejecutar en cualquier tarjeta.", OutputKind.Info);
        AppendOutput("Las operaciones largas se pueden cancelar desde el botón de cancelación de la barra de salida.", OutputKind.Info);
    }

    private async Task ShowAboutAsync()
    {
        try
        {
            var textBrush = new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0x88, 0x88, 0x88));
            var panel = new StackPanel { Spacing = 10, MaxWidth = 460 };
            panel.Children.Add(new FontIcon { Glyph = "\uE946", FontSize = 28, HorizontalAlignment = HorizontalAlignment.Left });
            panel.Children.Add(new TextBlock
            {
                Text = $"WDigger {AppVersion}",
                FontSize = 20,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Herramienta nativa de diagnóstico y reparación para Windows 10 y 11. WMI, sensores en tiempo real, SMART, batería, ajustes reversibles, red, copias Acronis, activación y limpieza — en una aplicación Fluent Design portable.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Portable · .NET 8 · WinUI 3 (Windows App SDK) · Siempre elevada con requireAdministrator",
                TextWrapping = TextWrapping.Wrap,
                Foreground = textBrush
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"Datos: {_host.BaseDirectory}\nRegistro: {_host.Logger.LogFilePath}",
                TextWrapping = TextWrapping.Wrap,
                Foreground = textBrush,
                FontSize = 12
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Herramienta de uso personal. No está destinada a distribución comercial.",
                FontSize = 11,
                Foreground = textBrush,
                TextWrapping = TextWrapping.Wrap
            });

            var dialog = new ContentDialog
            {
                XamlRoot = RootGrid.XamlRoot,
                Title = "Acerca de WDigger",
                Content = panel,
                CloseButtonText = "Cerrar",
                DefaultButton = ContentDialogButton.Close
            };
            await dialog.ShowAsync();
        }
        catch
        {
            // The dialog is cosmetic; never crash on it.
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closing = true;
        try
        {
            _lifetime.Cancel();
        }
        catch
        {
            // Best effort.
        }

        try
        {
            _host.Dispose();
        }
        catch
        {
            // Best effort.
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
        => new(ColorHelper.FromArgb(0xFF, r, g, b));
}
