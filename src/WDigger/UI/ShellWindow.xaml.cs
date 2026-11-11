using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
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
    internal const string AppVersion = "3.0.1";

    private readonly AppHost _host;
    private readonly AppSettings _settings;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly IReadOnlyDictionary<string, ModuleCatalog.Entry> _moduleIndex;
    private readonly List<string> _outputPlain = new();

    private CancellationTokenSource? _operationCts;
    private NavigationViewItem? _lastContentItem;
    private bool _isBusy;
    private bool _closing;
    private bool _outputCollapsed;
    private bool _currentChangesSystem;
    private bool _currentCanCancel;
    private readonly object _operationLock = new();

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
        ElevationPolicy.RequireElevatedProcess();
        _host = AppHost.Create(AppContext.BaseDirectory);
        PrivilegeLabel.Text = "ADMINISTRADOR";
        RootGrid.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(RootGrid_PointerPressed), true);
        _settings = AppSettings.Load(_host.ConfigDirectory);
        _moduleIndex = ModuleCatalog.Create().ToDictionary(entry => entry.Tag, StringComparer.OrdinalIgnoreCase);

        var commandLine = Environment.GetCommandLineArgs();
        var dryRunArgument = commandLine.Any(argument =>
            argument.Equals("--dry-run", StringComparison.OrdinalIgnoreCase) ||
            argument.Equals("-dryrun", StringComparison.OrdinalIgnoreCase));
        _host.DryRun = dryRunArgument || _settings.DryRun;

        ApplyTheme(_settings.Theme, persist: false);
        UpdateThemeLabel();

        DryRunSwitch.IsOn = _host.DryRun;
        DryRunSwitch.Toggled += DryRunSwitch_Toggled;

        AppWindow.Closing += OnAppWindowClosing;
        Closed += OnClosed;
        RegisterKeyboardShortcuts();

        var dashboard = NavView.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, "dashboard", StringComparison.OrdinalIgnoreCase));
        if (dashboard is not null)
        {
            _lastContentItem = dashboard;
            NavView.SelectedItem = dashboard;
            PageHost.Content = new DashboardPage(this);
        }

        ShowWelcome();
    }

    // Breadcrumbs are best effort: a logging failure must not break the UI.
    // Record only control types/names, never input text or pointer coordinates.
    internal void TraceUi(string message)
    {
        if (_closing) return;
        try { _host.Logger.Info(message); }
        catch { /* Logging must not interrupt an interaction. */ }
    }

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs args)
    {
        var source = args.OriginalSource;
        var type = source?.GetType().Name ?? "unknown";
        var name = (source as FrameworkElement)?.Name;
        TraceUi($"UI pointer pressed: type={type}, name={(string.IsNullOrEmpty(name) ? "<unnamed>" : name)}");
    }

    // ───────────────────────────── navigation ─────────────────────────────

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_closing || args.SelectedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        var tag = item.Tag as string;
        TraceUi($"Navigation selected: {tag ?? "<none>"}");

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
        if (_closing)
        {
            return;
        }

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
        if (_closing || _lastContentItem is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closing)
            {
                return;
            }

            if (!ReferenceEquals(NavView.SelectedItem, _lastContentItem))
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
            try
            {
                _settings.Save(_host.ConfigDirectory);
            }
            catch
            {
                // Best effort; settings persistence is not critical.
            }
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
        try
        {
            _settings.Save(_host.ConfigDirectory);
        }
        catch
        {
            // Best effort.
        }

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
        if (_closing)
        {
            return;
        }

        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => AppendOutput(message, kind));
            return;
        }

        try
        {
            var wasAtEnd = OutputScroller.ScrollableHeight - OutputScroller.VerticalOffset < 4;
            _outputPlain.Add(message);
            if (_outputPlain.Count > 1500)
            {
                _outputPlain.RemoveRange(0, _outputPlain.Count - 1500);
            }

            var paragraph = new Paragraph();
            var prefix = kind switch { OutputKind.Error => "[Error] ", OutputKind.Warning => "[Aviso] ", OutputKind.Success => "[OK] ", _ => "" };
            var run = new Run { Text = message.Length == 0 ? " " : prefix + message };
            paragraph.Inlines.Add(run);
            OutputText.Blocks.Add(paragraph);
            while (OutputText.Blocks.Count > 800)
            {
                OutputText.Blocks.Remove(OutputText.Blocks[0]);
            }

            OutputScroller.UpdateLayout();
            if (wasAtEnd) OutputScroller.ChangeView(null, OutputScroller.ScrollableHeight, null, disableAnimation: true);
        }
        catch
        {
            // The window may be closing; output is best effort.
        }
    }

    public void SetStatus(string message)
    {
        if (_closing)
        {
            return;
        }

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
        if (_closing)
        {
            return;
        }

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
        if (_closing)
        {
            return;
        }

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
        if (_closing)
        {
            return;
        }

        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ShowTable(title, columns, rows));
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

    private async void CancelOperationButton_Click(object sender, RoutedEventArgs e) => await RequestCancellationAsync();

    private async Task RequestCancellationAsync()
    {
        CancellationTokenSource? operation;
        lock (_operationLock) operation = _operationCts;
        if (operation is null || operation.IsCancellationRequested || !_currentCanCancel) return;

        CancelOperationButton.IsEnabled = false;
        if (_currentChangesSystem)
        {
            var confirmed = await Dialogs.ConfirmAsync("Cancelar operación",
                "Interrumpir una operación que modifica Windows puede dejar cambios parciales o el sistema en un estado inconsistente. Se recomienda esperar. ¿Interrumpir de todos modos?",
                "Interrumpir");
            if (!confirmed)
            {
                if (ReferenceEquals(_operationCts, operation)) CancelOperationButton.IsEnabled = true;
                return;
            }
        }
        lock (_operationLock)
        {
            if (!ReferenceEquals(_operationCts, operation)) return;
            SetStatus("Cancelando; revisa si hay cambios parciales...");
            operation.Cancel();
        }
    }

    private void RegisterKeyboardShortcuts()
    {
        AddShortcut(VirtualKey.F5, VirtualKeyModifiers.None, async () =>
        {
            if (_isBusy || _closing) return;
            NavigateToTag("diagnostics");
            var action = _moduleIndex["diagnostics"].Module.CreateActions(_host)[0];
            await RunActionAsync(action);
        });
        AddShortcut(VirtualKey.R, VirtualKeyModifiers.Control, async () =>
        {
            if (_isBusy) return;
            await Dialogs.ShowMessageAsync("Reparaciones", "WDigger se inició como administrador. Abre Reparación del sistema para seleccionar una operación; las acciones suspendidas seguirán bloqueadas hasta completar su revisión. F5 inicia el análisis.");
        });
        AddShortcut(VirtualKey.L, VirtualKeyModifiers.Control, () =>
        {
            SetOutputCollapsed(!_outputCollapsed);
            return Task.CompletedTask;
        });
        AddShortcut(VirtualKey.Escape, VirtualKeyModifiers.None, RequestCancellationAsync);
        AddShortcut(VirtualKey.F1, VirtualKeyModifiers.None, async () =>
        {
            if (_isBusy) return;
            await Dialogs.ShowMessageAsync("Ayuda", "F5: analizar; Ctrl+R: reparaciones; Ctrl+L: mostrar u ocultar salida; Esc: cancelar si está permitido; F1: ayuda. Las comprobaciones no disponibles no significan que Windows esté sano.");
        });
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Func<Task> callback)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += async (_, args) =>
        {
            args.Handled = true;
            try { await callback(); }
            catch (Exception exception) { TraceUi($"Keyboard shortcut failed: {exception.Message}"); }
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    // ───────────────────────────── action execution ─────────────────────────────

    public async Task RunActionAsync(ModuleAction action)
    {
        TraceUi($"Action requested: {action.Title}");
        if (action.UnavailableReason is not null)
        {
            AppendOutput($"Acción no disponible: {action.UnavailableReason}", OutputKind.Warning);
            return;
        }
        var decision = OperationPolicy.Evaluate(action.ChangesSystem, _host.DryRun,
            action.AllowedInElevatedSession, _host.IsAdministrator, _host.Logger.IsPersistent);
        if (decision != OperationDecision.Allowed)
        {
            var (title, reason) = decision switch
            {
                OperationDecision.BlockedPendingReview => ("Acción no disponible", "Esta operación sigue suspendida hasta completar su revisión de seguridad y recuperación. Puedes usar la simulación."),
                OperationDecision.AdministratorRequired => ("Elevación no disponible", "WDigger debe iniciarse elevado. La sesión actual no tiene un token de administrador, por lo que la acción se bloqueó. Cierra la aplicación y vuelve a aceptar el aviso de Control de cuentas de usuario."),
                _ => ("Registro no disponible", "La operación real está bloqueada porque no se puede guardar el registro. Libera espacio o usa una carpeta de datos escribible.")
            };
            await Dialogs.ShowMessageAsync(title, reason);
            return;
        }
        lock (_operationLock)
        {
            if (_isBusy || _closing)
            {
                return;
            }

            _isBusy = true;
        }

        DryRunSwitch.IsEnabled = false;
        var simulate = action.ChangesSystem && _host.DryRun;
        _currentChangesSystem = action.ChangesSystem && !simulate;
        _currentCanCancel = action.CanCancel;
        try
        {
            if (action.ChangesSystem && !simulate)
            {
                var message = action.Confirmation
                    ?? "Esta acción puede modificar Windows. Revisa la descripción antes de continuar.";
                var confirmed = await Dialogs.ConfirmAsync(
                    "Confirmar cambios en el sistema",
                    $"{message}\n\nEsta operación puede ser irreversible; un punto de restauración no sustituye una copia de seguridad ni garantiza el rollback. No apagues el equipo durante su ejecución.\n\n¿Quieres ejecutar «{action.Title}» en el sistema real?",
                    "Ejecutar", requireAcknowledgement: true);
                if (!confirmed)
                {
                    TraceUi($"Action confirmation declined: {action.Title}");
                    return;
                }
            }

            var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            CancellationTokenSource? previousCts = null;
            lock (_operationLock)
            {
                previousCts = _operationCts;
                _operationCts = operation;
            }
            previousCts?.Dispose();

            var context = new ModuleExecutionContext(this, _host, simulate, action.ChangesSystem);

            SetBusy(true, $"Ejecutando: {action.Title}...");
            AppendOutput(string.Empty, OutputKind.Muted);
            AppendOutput($"═══ {action.Title} ═══", OutputKind.Command);
            if (simulate)
            {
                AppendOutput("Modo simulación activo: no se aplicarán cambios reales.", OutputKind.Warning);
            }

            try
            {
                if (action.ChangesSystem && !simulate)
                {
                    CancelOperationButton.IsEnabled = false;
                    SetStatus("Creando y verificando punto de restauración...");
                    var created = await Task.Run(() => _host.RestorePoints.Create(action.Title), operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    if (!created)
                    {
                        _host.Logger.Warn($"No verified restore point for action: {action.Title}");
                        var proceed = await Dialogs.ConfirmAsync("Punto de restauración no disponible",
                            "Windows no ha creado un punto de restauración verificable. Continuar no permite recuperar automáticamente los cambios. Los puntos de restauración tampoco sustituyen una copia de seguridad de tus archivos. ¿Continuar sin punto de restauración?",
                            "Continuar sin punto");
                        _host.Logger.Warn($"Continue without restore point: {proceed}; action={action.Title}");
                        if (!proceed) { SetStatus("Acción no ejecutada"); return; }
                    }
                    else AppendOutput("Punto de restauración verificado; no sustituye una copia de seguridad.", OutputKind.Info);
                    CancelOperationButton.IsEnabled = _currentCanCancel;
                    operation.Token.ThrowIfCancellationRequested();
                }
                TraceUi($"Action executing: {action.Title}");
                if (action.ChangesSystem && !simulate && !_host.Logger.IsPersistent)
                    throw new InvalidOperationException("El registro persistente dejó de estar disponible; no se iniciaron los cambios.");
                await action.Execute(context, operation.Token);
                TraceUi($"Action returned: {action.Title}; cancelled={operation.IsCancellationRequested}");
                if (!operation.IsCancellationRequested)
                {
                    SetStatus(simulate ? "Simulación finalizada" : "Acción terminada; revisa el resultado");
                    AppendOutput(simulate ? "Simulación finalizada: no se ejecutaron los cambios." : "Acción terminada; revisa los resultados anteriores.", simulate ? OutputKind.Warning : OutputKind.Info);
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("Operación cancelada");
                AppendOutput("Operación cancelada. Si se realizaron cambios, pueden haber quedado incompletos; revisa el sistema.", OutputKind.Warning);
            }
            catch (Exception exception)
            {
                _host.Logger.Error($"Action '{action.Title}' failed: {exception}");
                SetStatus("La acción produjo un error");
                AppendOutput(exception.ToString(), OutputKind.Error);
                try
                {
                    await Dialogs.ShowMessageAsync("Error al ejecutar la acción", exception.Message);
                }
                catch
                {
                    // Dialog failure must not crash the app.
                }
            }
            finally
            {
                lock (_operationLock)
                {
                    if (ReferenceEquals(_operationCts, operation))
                    {
                        _operationCts = null;
                    }
                }
                operation.Dispose();
                ReportProgress(100);
                SetBusy(false, null);
            }
        }
        finally
        {
            DryRunSwitch.IsEnabled = true;
            lock (_operationLock)
            {
                _isBusy = false;
            }
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        _isBusy = busy;
        NavView.IsEnabled = !busy;
        CancelOperationButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        CancelOperationButton.IsEnabled = busy && _currentCanCancel;
        if (status is not null) SetStatus(status);
    }

    // ───────────────────────────── misc ─────────────────────────────

    private void ShowWelcome()
    {
        AppendOutput($"WDigger {AppVersion} · WinUI 3 · Fluent Design", OutputKind.Success);
        AppendOutput("Sesión iniciada con permisos de administrador.", OutputKind.Info);
        AppendOutput($"Ubicación de datos: {_host.BaseDirectory}", OutputKind.Muted);
        AppendOutput($"Registro de auditoría: {_host.Logger.LogFilePath}", OutputKind.Muted);
        if (!_host.Logger.IsPersistent) AppendOutput("No hay almacenamiento para el registro persistente. Las operaciones de modificación permanecen bloqueadas.", OutputKind.Warning);
        AppendOutput("Selecciona una herramienta en el panel lateral y pulsa Ejecutar en cualquier tarjeta.", OutputKind.Info);
        AppendOutput("Algunas operaciones no se pueden cancelar de forma segura. No cierres la aplicación mientras estén activas.", OutputKind.Info);
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
                Text = "Herramienta nativa de diagnóstico y reparación para Windows 10 y 11. WMI, sensores, diagnóstico y algunas reparaciones de Windows. Varias operaciones siguen suspendidas por seguridad; consulta la documentación.",
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(new TextBlock
            {
                Text = "Portable · .NET 10 · WinUI 3 · Inicio obligatorio como administrador",
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

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_isBusy) return;
        args.Cancel = true;
        SetStatus("Espera a que termine la operación antes de cerrar WDigger.");
        AppendOutput("No se puede cerrar la ventana durante una operación. Espera a que termine; si la cancelación está disponible, revisa primero sus riesgos.", OutputKind.Warning);
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

        lock (_operationLock)
        {
            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = null;
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

}
