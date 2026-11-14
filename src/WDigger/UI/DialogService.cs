using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using System.Runtime.InteropServices;

namespace WDigger.UI;

/// <summary>
/// ContentDialog and file-picker primitives used by the modules. Every helper
/// is asynchronous: WinUI dialogs cannot be pumped synchronously.
/// </summary>
public sealed class DialogService
{
    private static Brush? DangerHintBrush
    {
        get
        {
            try { return Application.Current.Resources["DangerBrush"] as Brush; }
            catch { return null; }
        }
    }

    private readonly Window _window;

    public DialogService(Window window) => _window = window;

    private XamlRoot Root => _window.Content?.XamlRoot
        ?? throw new InvalidOperationException("La ventana todavía no está preparada para mostrar diálogos.");

    private IntPtr WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(_window);

    public async Task<string?> PromptAsync(string title, string label, string defaultValue = "", bool allowEmpty = false)
    {
        var input = new TextBox
        {
            Text = defaultValue,
            Width = 340,
            PlaceholderText = "Escribe un valor..."
        };
        var hint = new TextBlock
        {
            Text = "Escribe un valor para continuar.",
            Foreground = DangerHintBrush,
            FontSize = 12,
            Visibility = Visibility.Collapsed
        };
        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 400 });
        panel.Children.Add(input);
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.Opened += (_, _) => input.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (!allowEmpty && string.IsNullOrWhiteSpace(input.Text))
            {
                hint.Visibility = Visibility.Visible;
                args.Cancel = true;
            }
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? input.Text.Trim() : null;
    }

    public async Task<T?> ChooseAsync<T>(string title, string label, IEnumerable<T> choices, Func<T, string> formatter)
        where T : class
    {
        var items = choices.ToList();
        if (items.Count == 0)
        {
            return null;
        }

        var list = new ListView
        {
            MinWidth = 380,
            MaxHeight = 340,
            ItemsSource = items.Select(formatter).ToList(),
            SelectionMode = ListViewSelectionMode.Single
        };
        list.SelectedIndex = 0;

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 430 });
        panel.Children.Add(list);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = panel,
            PrimaryButtonText = "Aceptar",
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || list.SelectedIndex < 0 || list.SelectedIndex >= items.Count)
        {
            return null;
        }

        return items[list.SelectedIndex];
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText = "Continuar", bool requireAcknowledgement = false)
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 430 };
        panel.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        var acknowledgement = new CheckBox { Content = "Entiendo las consecuencias", Visibility = requireAcknowledgement ? Visibility.Visible : Visibility.Collapsed };
        panel.Children.Add(acknowledgement);

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close,
            IsPrimaryButtonEnabled = !requireAcknowledgement
        };
        if (requireAcknowledgement)
        {
            acknowledgement.Checked += (_, _) => dialog.IsPrimaryButtonEnabled = true;
            acknowledgement.Unchecked += (_, _) => dialog.IsPrimaryButtonEnabled = false;
        }

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    public async Task ShowMessageAsync(string title, string message)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 430 },
            CloseButtonText = "Aceptar",
            DefaultButton = ContentDialogButton.Close
        };
        await dialog.ShowAsync();
    }

    public async Task<string?> PickFileAsync(string title, string filter, string? initialPath = null)
    {
        _ = initialPath; // Folder start locations are not supported by the WinRT picker.
        _ = title;

        var extensions = ParseExtensions(filter);
        return await RunOnUiThreadAsync(async () =>
        {
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            foreach (var extension in extensions)
            {
                if (!picker.FileTypeFilter.Contains(extension))
                {
                    picker.FileTypeFilter.Add(extension);
                }
            }

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        });
    }

    public async Task<string?> PickSaveFileAsync(string suggestedName, string extension)
    {
        return await RunOnUiThreadAsync(async () =>
        {
            var picker = new FileSavePicker { SuggestedFileName = suggestedName, SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeChoices.Add("Documento de texto", new List<string> { extension });
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
            var file = await picker.PickSaveFileAsync();
            return file?.Path;
        });
    }

    public async Task<string?> PickFolderAsync(string description, string? initialPath = null)
    {
        _ = description;
        _ = initialPath; // Folder start locations are not supported by the WinRT picker.

        return await RunOnUiThreadAsync(async () =>
        {
            var picker = new FolderPicker
            {
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };
            picker.FileTypeFilter.Add("*");

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        });
    }

    /// <summary>
    /// Runs a Windows shell picker on the UI thread. Pickers fail with
    /// COMException (0x80004005) when they are invoked from a worker thread
    /// without a dispatcher, which is what happens after an awaited command
    /// resumes the action outside the UI thread.
    /// </summary>
    private async Task<string?> RunOnUiThreadAsync(Func<Task<string?>> operation)
    {
        var dispatcher = _window.DispatcherQueue;
        if (dispatcher.HasThreadAccess)
        {
            return await InvokePickerAsync(operation);
        }

        var completion = new TaskCompletionSource<string?>();
        if (!dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.SetResult(await InvokePickerAsync(operation));
                }
                catch (Exception exception)
                {
                    completion.SetException(exception);
                }
            }))
        {
            completion.SetException(new InvalidOperationException("No se pudo mostrar el selector en el hilo de la interfaz."));
        }

        return await completion.Task;
    }

    private static async Task<string?> InvokePickerAsync(Func<Task<string?>> operation)
    {
        try
        {
            return await operation();
        }
        catch (COMException exception)
        {
            throw new InvalidOperationException(
                "Windows no pudo mostrar el selector de archivos o carpetas. Cierra cualquier diálogo abierto y vuelve a intentarlo.",
                exception);
        }
    }

    private static IReadOnlyList<string> ParseExtensions(string filter)
    {
        // WinForms-style filter: "INF (*.inf)|*.inf|All files (*.*)|*.*"
        var extensions = new List<string>();
        var parts = filter.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var index = 1; index < parts.Length; index += 2)
        {
            foreach (var rawToken in parts[index].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var token = rawToken.Trim();
                if (token is "*.*" or "*")
                {
                    extensions.Add("*");
                    continue;
                }

                var starIndex = token.LastIndexOf('*');
                var extension = starIndex >= 0 ? token[(starIndex + 1)..] : token;
                if (!extension.StartsWith('.'))
                {
                    extension = "." + extension;
                }

                extensions.Add(extension);
            }
        }

        if (extensions.Count == 0)
        {
            extensions.Add("*");
        }

        return extensions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
