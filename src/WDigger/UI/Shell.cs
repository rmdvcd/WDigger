using System.Data;
using System.Diagnostics;
using WDigger.Core;
using WDigger.Models;
using WDigger.Modules;

namespace WDigger.UI;

/// <summary>
/// Native Windows shell: module navigation, dialogs, result grids, live output,
/// progress, cancellation and portable settings.
/// </summary>
public sealed class Shell : Form
{
    private readonly AppHost _host;
    private readonly IReadOnlyList<IModule> _modules;
    private readonly List<NavigationItem> _navigationItems = new();
    private readonly CancellationTokenSource _lifetime = new();

    private readonly ListBox _navigationList = new();
    private readonly ListView _actionList = new();
    private readonly Label _titleLabel = new();
    private readonly Label _descriptionLabel = new();
    private readonly Label _actionDescriptionLabel = new();
    private readonly Label _actionStatusLabel = new();
    private readonly Button _executeButton = new();
    private readonly Button _cancelButton = new();
    private readonly CheckBox _dryRunToggle = new();
    private readonly RichTextBox _output = new();
    private readonly TabControl _resultTabs = new();
    private readonly TabPage _tableTab = new();
    private readonly Label _tableTitle = new();
    private readonly DataGridView _table = new();
    private readonly ToolStripStatusLabel _statusLabel = new();
    private readonly ToolStripProgressBar _progress = new();
    private readonly ToolStripStatusLabel _modeLabel = new();
    private readonly ToolTip _toolTip = new();

    private CancellationTokenSource? _operation;
    private bool _isBusy;
    private bool _closeWhenFinished;

    public Shell(AppHost host)
    {
        _host = host;
        _modules = new IModule[]
        {
            new HardwareModule(),
            new RepairModule(),
            new SoftwareModule(),
            new TweaksModule(),
            new DriversModule(),
            new NetworkModule(),
            new AdvancedNetworkModule(),
            new DevicesModule(),
            new BackupModule(),
            new ActivationModule(),
            new RecoveryModule(),
            new EventLogBiosModule(),
            new DiskModule()
        };

        Text = "WDigger · Diagnóstico y reparación de Windows";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1040, 660);
        Font = new Font("Segoe UI", 9F);
        BackColor = Color.FromArgb(243, 246, 250);
        Icon = SystemIcons.Shield;
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;

        BuildInterface();
        BuildNavigation();
        UpdateModeLabel();
        ShowWelcome();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);
        root.SetColumnSpan(header, 2);

        var navigation = BuildNavigationPanel();
        root.Controls.Add(navigation, 0, 1);

        var content = BuildContentPanel();
        root.Controls.Add(content, 1, 1);

        var status = BuildStatusBar();
        root.Controls.Add(status, 0, 2);
        root.SetColumnSpan(status, 2);
        Controls.Add(root);
    }

    private Control BuildHeader()
    {
        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(0, 78, 137),
            Padding = new Padding(22, 10, 18, 8)
        };

        var titles = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent
        };
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        titles.Controls.Add(new Label
        {
            Text = "WDigger",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 20F, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);
        titles.Controls.Add(new Label
        {
            Text = "Herramientas nativas de diagnóstico y reparación · Portable",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(218, 235, 247),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 1);
        header.Controls.Add(titles);

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 370,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 20, 0, 0)
        };
        _dryRunToggle.Text = "Modo simulación";
        _dryRunToggle.Appearance = Appearance.Button;
        _dryRunToggle.AutoSize = true;
        _dryRunToggle.FlatStyle = FlatStyle.System;
        _dryRunToggle.ForeColor = Color.White;
        _dryRunToggle.BackColor = Color.FromArgb(0, 105, 180);
        _dryRunToggle.Padding = new Padding(10, 5, 10, 5);
        _dryRunToggle.Margin = new Padding(10, 0, 0, 0);
        _dryRunToggle.Checked = _host.DryRun;
        _dryRunToggle.CheckedChanged += DryRunToggle_CheckedChanged;
        options.Controls.Add(_dryRunToggle);
        options.Controls.Add(new Label
        {
            Text = "●  ADMINISTRADOR",
            AutoSize = true,
            ForeColor = Color.FromArgb(195, 239, 206),
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            Margin = new Padding(0, 10, 8, 0)
        });
        header.Controls.Add(options);
        return header;
    }

    private Control BuildNavigationPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12, 10, 8, 10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        panel.Controls.Add(new Label
        {
            Text = "HERRAMIENTAS",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(90, 100, 115),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _navigationList.Dock = DockStyle.Fill;
        _navigationList.BorderStyle = BorderStyle.None;
        _navigationList.IntegralHeight = false;
        _navigationList.Font = new Font("Segoe UI", 10F);
        _navigationList.ItemHeight = 30;
        _navigationList.SelectedIndexChanged += NavigationList_SelectedIndexChanged;
        panel.Controls.Add(_navigationList, 0, 1);

        var portable = new Label
        {
            Text = "Sin instalación\r\nDatos junto al ejecutable",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 110, 120),
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true
        };
        _toolTip.SetToolTip(portable, _host.BaseDirectory);
        panel.Controls.Add(portable, 0, 2);
        return panel;
    }

    private Control BuildContentPanel()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24, 18, 20, 14)
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold);
        _titleLabel.ForeColor = Color.FromArgb(28, 42, 56);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.AutoEllipsis = true;
        content.Controls.Add(_titleLabel, 0, 0);

        _descriptionLabel.Dock = DockStyle.Fill;
        _descriptionLabel.ForeColor = Color.FromArgb(85, 96, 108);
        _descriptionLabel.AutoEllipsis = true;
        content.Controls.Add(_descriptionLabel, 0, 1);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            SplitterWidth = 12,
            Panel1MinSize = 250,
            Panel2MinSize = 350
        };
        split.Panel1.BackColor = Color.White;
        split.Panel2.BackColor = Color.White;
        split.Panel1.Controls.Add(BuildActionsPanel());
        split.Panel2.Controls.Add(BuildResultsPanel());
        Shown += (_, _) =>
        {
            if (split.Width > 700)
            {
                split.SplitterDistance = Math.Min(390, split.Width - 420);
            }
        };
        content.Controls.Add(split, 0, 2);
        return content;
    }

    private Control BuildActionsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        _actionList.Dock = DockStyle.Fill;
        _actionList.View = View.Details;
        _actionList.FullRowSelect = true;
        _actionList.GridLines = true;
        _actionList.MultiSelect = false;
        _actionList.HideSelection = false;
        _actionList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _actionList.Columns.Add("Acción", 245);
        _actionList.Columns.Add("Tipo", 72);
        _actionList.SelectedIndexChanged += ActionList_SelectedIndexChanged;
        _actionList.DoubleClick += ActionList_DoubleClick;
        panel.Controls.Add(_actionList, 0, 0);

        _actionDescriptionLabel.Dock = DockStyle.Fill;
        _actionDescriptionLabel.Padding = new Padding(2, 9, 2, 4);
        _actionDescriptionLabel.ForeColor = Color.FromArgb(85, 96, 108);
        _actionDescriptionLabel.AutoEllipsis = true;
        panel.Controls.Add(_actionDescriptionLabel, 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));

        _actionStatusLabel.Dock = DockStyle.Fill;
        _actionStatusLabel.Text = "Selecciona una acción";
        _actionStatusLabel.ForeColor = Color.FromArgb(100, 110, 120);
        _actionStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _actionStatusLabel.AutoEllipsis = true;
        buttons.Controls.Add(_actionStatusLabel, 0, 0);

        _cancelButton.Text = "Cancelar";
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Enabled = false;
        _cancelButton.Click += (_, _) => CancelOperation();
        buttons.Controls.Add(_cancelButton, 1, 0);

        _executeButton.Text = "Ejecutar";
        _executeButton.Dock = DockStyle.Fill;
        _executeButton.BackColor = Color.FromArgb(0, 120, 215);
        _executeButton.ForeColor = Color.White;
        _executeButton.FlatStyle = FlatStyle.System;
        _executeButton.Enabled = false;
        _executeButton.Click += ExecuteButton_Click;
        buttons.Controls.Add(_executeButton, 2, 0);
        AcceptButton = _executeButton;
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Control BuildResultsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        toolbar.Controls.Add(new Label
        {
            Text = "RESULTADOS Y ACTIVIDAD",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold),
            ForeColor = Color.FromArgb(85, 96, 108),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        var copy = new Button { Text = "Copiar", Dock = DockStyle.Fill, FlatStyle = FlatStyle.System };
        copy.Click += (_, _) =>
        {
            if (!string.IsNullOrEmpty(_output.Text))
            {
                Clipboard.SetText(_output.Text);
            }
        };
        toolbar.Controls.Add(copy, 1, 0);
        var clear = new Button { Text = "Limpiar", Dock = DockStyle.Fill, FlatStyle = FlatStyle.System };
        clear.Click += (_, _) => _output.Clear();
        toolbar.Controls.Add(clear, 2, 0);
        panel.Controls.Add(toolbar, 0, 0);

        _resultTabs.Dock = DockStyle.Fill;
        var outputTab = new TabPage("Actividad");
        _output.Dock = DockStyle.Fill;
        _output.ReadOnly = true;
        _output.BorderStyle = BorderStyle.None;
        _output.BackColor = Color.FromArgb(20, 27, 34);
        _output.ForeColor = Color.FromArgb(225, 230, 235);
        _output.Font = new Font("Consolas", 9F);
        _output.DetectUrls = false;
        _output.WordWrap = false;
        _output.ScrollBars = RichTextBoxScrollBars.Both;
        outputTab.Controls.Add(_output);
        _resultTabs.TabPages.Add(outputTab);

        _tableTab.Text = "Tabla";
        var tablePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        tablePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        tablePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _tableTitle.Dock = DockStyle.Fill;
        _tableTitle.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
        _tableTitle.TextAlign = ContentAlignment.MiddleLeft;
        _tableTitle.AutoEllipsis = true;
        tablePanel.Controls.Add(_tableTitle, 0, 0);

        _table.Dock = DockStyle.Fill;
        _table.ReadOnly = true;
        _table.AllowUserToAddRows = false;
        _table.AllowUserToDeleteRows = false;
        _table.AllowUserToResizeRows = true;
        _table.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _table.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders;
        _table.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _table.RowHeadersVisible = false;
        _table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _table.MultiSelect = true;
        _table.BackgroundColor = Color.White;
        _table.BorderStyle = BorderStyle.None;
        _table.ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText;
        tablePanel.Controls.Add(_table, 0, 1);
        _tableTab.Controls.Add(tablePanel);
        _resultTabs.TabPages.Add(_tableTab);
        panel.Controls.Add(_resultTabs, 0, 1);
        return panel;
    }

    private Control BuildStatusBar()
    {
        var status = new StatusStrip { Dock = DockStyle.Fill, SizingGrip = false };
        _statusLabel.Spring = true;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Text = "Listo";
        _progress.Width = 140;
        _progress.Visible = false;
        _modeLabel.Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold);
        status.Items.AddRange(new ToolStripItem[] { _statusLabel, _progress, _modeLabel });
        return status;
    }

    private void BuildNavigation()
    {
        _navigationItems.Add(new NavigationItem(null));
        _navigationList.Items.Add("⌂  Inicio");
        foreach (var module in _modules)
        {
            _navigationItems.Add(new NavigationItem(module));
            _navigationList.Items.Add("   " + module.Name);
        }
        _navigationList.SelectedIndex = 0;
    }

    private void NavigationList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_isBusy || _navigationList.SelectedIndex < 0)
        {
            return;
        }

        var selected = _navigationItems[_navigationList.SelectedIndex];
        if (selected.Module is null)
        {
            _titleLabel.Text = "Panel de control";
            _descriptionLabel.Text = "Resumen del equipo y acceso rápido a los datos portables de WDigger.";
            LoadActions(BuildDashboardActions());
        }
        else
        {
            _titleLabel.Text = selected.Module.Name;
            _descriptionLabel.Text = selected.Module.Description;
            LoadActions(selected.Module.CreateActions(_host));
        }
    }

    private IReadOnlyList<ModuleAction> BuildDashboardActions()
    {
        return new[]
        {
            new ModuleAction(
                "Actualizar resumen del sistema",
                "Consulta Windows, procesador, memoria y sensores principales mediante WMI.",
                ShowSystemSummaryAsync),
            new ModuleAction(
                "Abrir carpeta de registros",
                "Abre los registros de auditoría y el historial de ajustes aplicados.",
                (_, _) => OpenPortableFolder(_host.LogDirectory)),
            new ModuleAction(
                "Abrir configuración portable",
                "Abre tweaks.json y software.json para personalizarlos sin recompilar.",
                (_, _) => OpenPortableFolder(_host.ConfigDirectory)),
            new ModuleAction(
                "Acerca de esta copia",
                "Muestra la ubicación portable, arquitectura y modo de ejecución.",
                (_, _) =>
                {
                    MessageBox.Show(this,
                        $"WDigger Portable para Windows x64\n\nEjecutable: {Environment.ProcessPath}\nDatos: {_host.BaseDirectory}\nRegistro: {_host.Logger.LogFilePath}\n\nSe ejecuta como administrador y no requiere .NET instalado.",
                        "Acerca de WDigger", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return Task.CompletedTask;
                })
        };
    }

    private async Task ShowSystemSummaryAsync(ModuleExecutionContext context, CancellationToken cancellationToken)
    {
        context.SetStatus("Consultando inventario WMI...");
        var inventory = await Task.Run(
            () => _host.Inventory.GetInventory().Take(6).ToList(), cancellationToken);
        context.SetStatus("Leyendo sensores...");
        var readings = await Task.Run(() => _host.Sensors.ReadAll(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var rows = inventory
            .Select(item => new[] { item.Category, item.Item, EmptyAsNotAvailable(item.Value) })
            .ToList();

        AddSensorRow(rows, readings, "CPU", "Temperature", "CPU", "Temperatura");
        AddSensorRow(rows, readings, "CPU", "Load", "CPU", "Carga");
        AddSensorRow(rows, readings, "Gpu", "Temperature", "GPU", "Temperatura");
        AddSensorRow(rows, readings, "Gpu", "Load", "GPU", "Carga");
        if (readings.Count == 0)
        {
            rows.Add(new[] { "Sensores", "Estado", "No hay lecturas disponibles en este equipo." });
        }

        context.ShowTable("Resumen del sistema", new[] { "Componente", "Detalle", "Valor" }, rows);
        context.WriteLine($"Resumen actualizado: {rows.Count} elementos.", OutputKind.Success);
    }

    private static void AddSensorRow(
        ICollection<string[]> rows,
        IReadOnlyList<SensorReading> readings,
        string hardwareType,
        string sensorType,
        string category,
        string label)
    {
        var reading = readings.FirstOrDefault(item =>
            item.HardwareType.Equals(hardwareType, StringComparison.OrdinalIgnoreCase)
            && item.SensorType.Equals(sensorType, StringComparison.OrdinalIgnoreCase));
        if (reading is not null)
        {
            rows.Add(new[] { category, label, reading.DisplayValue });
        }
    }

    private Task OpenPortableFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"\"{path}\"",
            UseShellExecute = true
        });
        return Task.CompletedTask;
    }

    private void LoadActions(IReadOnlyList<ModuleAction> actions)
    {
        _actionList.BeginUpdate();
        _actionList.Items.Clear();
        foreach (var action in actions)
        {
            var item = new ListViewItem(action.Title) { Tag = action, ToolTipText = action.Description };
            item.SubItems.Add(action.ChangesSystem ? "Modifica" : "Consulta");
            if (action.ChangesSystem)
            {
                item.ForeColor = Color.FromArgb(155, 75, 0);
            }
            _actionList.Items.Add(item);
        }
        _actionList.EndUpdate();

        if (_actionList.Items.Count > 0)
        {
            _actionList.Items[0].Selected = true;
            _actionList.Items[0].Focused = true;
        }
        else
        {
            _actionDescriptionLabel.Text = "No hay acciones disponibles.";
            _executeButton.Enabled = false;
        }
    }

    private void ActionList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        var action = SelectedAction();
        _executeButton.Enabled = action is not null && !_isBusy;
        _actionDescriptionLabel.Text = action?.Description ?? "Selecciona una acción.";
        _actionStatusLabel.Text = action is null
            ? "Selecciona una acción"
            : action.ChangesSystem ? "Requiere confirmación" : "Solo lectura";
    }

    private ModuleAction? SelectedAction()
        => _actionList.SelectedItems.Count == 0 ? null : _actionList.SelectedItems[0].Tag as ModuleAction;

    private async void ExecuteButton_Click(object? sender, EventArgs e) => await ExecuteSelectedActionAsync();

    private async void ActionList_DoubleClick(object? sender, EventArgs e) => await ExecuteSelectedActionAsync();

    private async Task ExecuteSelectedActionAsync()
    {
        if (_isBusy)
        {
            return;
        }

        var action = SelectedAction();
        if (action is null)
        {
            return;
        }

        if (action.ChangesSystem && !_host.DryRun)
        {
            var message = action.Confirmation
                ?? "Esta acción puede modificar Windows. Revisa la descripción antes de continuar.";
            var result = MessageBox.Show(this,
                $"{message}\n\n¿Quieres ejecutar «{action.Title}» en el sistema real?",
                "Confirmar cambios en el sistema",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (result != DialogResult.Yes)
            {
                return;
            }
        }

        var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        var context = new ModuleExecutionContext(this, _host);
        SetBusy(true, action.Title);
        AppendOutput($"{Environment.NewLine}═══ {action.Title} ═══", OutputKind.Command);
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
            MessageBox.Show(this, exception.Message, "Error al ejecutar la acción",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _operation = null;
            operation.Dispose();
            ReportProgress(100);
            SetBusy(false, "Listo");
            if (_closeWhenFinished)
            {
                BeginInvoke(new MethodInvoker(Close));
            }
        }
    }

    private void SetBusy(bool busy, string status)
    {
        _isBusy = busy;
        _navigationList.Enabled = !busy;
        _actionList.Enabled = !busy;
        _dryRunToggle.Enabled = !busy;
        _executeButton.Enabled = !busy && SelectedAction() is not null;
        _cancelButton.Enabled = busy;
        _actionStatusLabel.Text = busy ? "Operación en curso..." : "Selecciona una acción";
        SetStatus(status);
    }

    private void CancelOperation()
    {
        if (_operation is null || _operation.IsCancellationRequested)
        {
            return;
        }

        _cancelButton.Enabled = false;
        SetStatus("Cancelando; espera a que finalice el proceso...");
        _operation.Cancel();
    }

    private void DryRunToggle_CheckedChanged(object? sender, EventArgs e)
    {
        _host.DryRun = _dryRunToggle.Checked;
        _host.Logger.Info($"Dry-run mode changed: {_host.DryRun}");
        UpdateModeLabel();
        AppendOutput(_host.DryRun
            ? "Modo simulación habilitado: las acciones de modificación solo se registrarán."
            : "Modo real habilitado: las acciones confirmadas modificarán el sistema.",
            _host.DryRun ? OutputKind.Warning : OutputKind.Success);
    }

    private void UpdateModeLabel()
    {
        _modeLabel.Text = _host.DryRun ? "SIMULACIÓN" : "SISTEMA REAL";
        _modeLabel.ForeColor = _host.DryRun ? Color.FromArgb(155, 75, 0) : Color.DarkGreen;
    }

    private void ShowWelcome()
    {
        AppendOutput("WDigger está listo. Interfaz nativa de Windows · Portable · Administrador", OutputKind.Success);
        AppendOutput($"Ubicación de datos: {_host.BaseDirectory}", OutputKind.Muted);
        AppendOutput($"Registro de auditoría: {_host.Logger.LogFilePath}", OutputKind.Muted);
        AppendOutput("Selecciona una herramienta y pulsa Ejecutar. Las operaciones largas se pueden cancelar.", OutputKind.Info);
    }

    internal void ShowActivity()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(ShowActivity)); } catch { /* Window closed. */ }
            return;
        }
        _resultTabs.SelectedIndex = 0;
    }

    internal void AppendOutput(string message, OutputKind kind = OutputKind.Info)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => AppendOutput(message, kind))); } catch { /* Window closed. */ }
            return;
        }

        _output.SelectionStart = _output.TextLength;
        _output.SelectionLength = 0;
        _output.SelectionColor = kind switch
        {
            OutputKind.Success => Color.FromArgb(120, 220, 145),
            OutputKind.Warning => Color.FromArgb(245, 190, 90),
            OutputKind.Error => Color.FromArgb(255, 120, 120),
            OutputKind.Command => Color.FromArgb(100, 190, 255),
            OutputKind.Muted => Color.FromArgb(145, 155, 165),
            _ => Color.FromArgb(225, 230, 235)
        };
        _output.AppendText(message + Environment.NewLine);
        _output.SelectionStart = _output.TextLength;
        _output.ScrollToCaret();
    }

    internal void SetStatus(string message)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => SetStatus(message))); } catch { /* Window closed. */ }
            return;
        }
        _statusLabel.Text = message;
    }

    internal void ReportProgress(int? percent)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try { BeginInvoke(new Action(() => ReportProgress(percent))); } catch { /* Window closed. */ }
            return;
        }

        if (!percent.HasValue)
        {
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Visible = true;
            return;
        }

        _progress.Style = ProgressBarStyle.Continuous;
        _progress.Value = Math.Clamp(percent.Value, 0, 100);
        _progress.Visible = percent.Value < 100;
    }

    internal void ShowTable(string title, IReadOnlyList<string> columns, IEnumerable<string[]> rows)
    {
        var data = new DataTable(title);
        foreach (var column in columns)
        {
            data.Columns.Add(column);
        }
        foreach (var row in rows)
        {
            var values = new object[columns.Count];
            for (var index = 0; index < columns.Count; index++)
            {
                values[index] = index < row.Length ? row[index] : string.Empty;
            }
            data.Rows.Add(values);
        }

        if (IsDisposed || Disposing)
        {
            data.Dispose();
            return;
        }
        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => ApplyTable(title, data)));
            return;
        }
        ApplyTable(title, data);
    }

    private void ApplyTable(string title, DataTable data)
    {
        if (IsDisposed || Disposing)
        {
            data.Dispose();
            return;
        }

        var previous = _table.DataSource as DataTable;
        _table.DataSource = null;
        previous?.Dispose();
        _table.DataSource = data;
        _tableTitle.Text = $"{title} · {data.Rows.Count} filas";
        _resultTabs.SelectedTab = _tableTab;
    }

    private static string EmptyAsNotAvailable(string? value)
        => string.IsNullOrWhiteSpace(value) ? "No disponible" : value;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_isBusy)
        {
            var close = MessageBox.Show(this,
                "Hay una operación en curso. ¿Quieres cancelarla y cerrar WDigger?",
                "Operación en curso", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (close == DialogResult.Yes)
            {
                _closeWhenFinished = true;
                CancelOperation();
            }
            e.Cancel = true;
            base.OnFormClosing(e);
            return;
        }

        _lifetime.Cancel();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnFormClosed(e);
    }

    private sealed record NavigationItem(IModule? Module);
}
