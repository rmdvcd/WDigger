namespace WDigger.UI;

internal static class NativeDialogs
{
    public static string? Prompt(
        IWin32Window owner,
        string title,
        string label,
        string defaultValue,
        bool allowEmpty)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(500, 154),
            Font = new Font("Segoe UI", 9F),
            AutoScaleMode = AutoScaleMode.Dpi
        };

        var promptLabel = new Label
        {
            Text = label,
            AutoSize = false,
            Bounds = new Rectangle(16, 14, 468, 38)
        };
        var input = new TextBox
        {
            Text = defaultValue,
            Bounds = new Rectangle(16, 56, 468, 27)
        };
        var accept = new Button
        {
            Text = "Aceptar",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(308, 105, 85, 30)
        };
        var cancel = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(399, 105, 85, 30)
        };

        dialog.Controls.AddRange(new Control[] { promptLabel, input, accept, cancel });
        dialog.AcceptButton = accept;
        dialog.CancelButton = cancel;

        while (dialog.ShowDialog(owner) == DialogResult.OK)
        {
            if (allowEmpty || !string.IsNullOrWhiteSpace(input.Text))
            {
                return input.Text.Trim();
            }

            MessageBox.Show(dialog, "Escribe un valor para continuar.", title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            input.Focus();
        }

        return null;
    }

    public static int? Choose(IWin32Window owner, string title, string label, IReadOnlyList<string> choices)
    {
        if (choices.Count == 0)
        {
            return null;
        }

        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ClientSize = new Size(540, 160),
            Font = new Font("Segoe UI", 9F),
            AutoScaleMode = AutoScaleMode.Dpi
        };

        var promptLabel = new Label
        {
            Text = label,
            AutoSize = false,
            Bounds = new Rectangle(16, 14, 508, 38)
        };
        var input = new ComboBox
        {
            Bounds = new Rectangle(16, 56, 508, 28),
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        input.Items.AddRange(choices.Cast<object>().ToArray());
        input.SelectedIndex = 0;

        var accept = new Button
        {
            Text = "Aceptar",
            DialogResult = DialogResult.OK,
            Bounds = new Rectangle(348, 108, 85, 30)
        };
        var cancel = new Button
        {
            Text = "Cancelar",
            DialogResult = DialogResult.Cancel,
            Bounds = new Rectangle(439, 108, 85, 30)
        };

        dialog.Controls.AddRange(new Control[] { promptLabel, input, accept, cancel });
        dialog.AcceptButton = accept;
        dialog.CancelButton = cancel;
        return dialog.ShowDialog(owner) == DialogResult.OK ? input.SelectedIndex : null;
    }

    public static string? PickFile(IWin32Window owner, string title, string filter, string? initialPath)
    {
        using var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath))
        {
            var fullPath = Path.GetFullPath(initialPath);
            if (File.Exists(fullPath))
            {
                dialog.FileName = fullPath;
            }
            else if (Directory.Exists(fullPath))
            {
                dialog.InitialDirectory = fullPath;
            }
        }

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }

    public static string? PickFolder(IWin32Window owner, string description, string? initialPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.SelectedPath = Path.GetFullPath(initialPath);
        }

        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
