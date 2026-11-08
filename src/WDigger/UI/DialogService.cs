using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;

namespace WDigger.UI;

/// <summary>
/// ContentDialog and file-picker primitives used by the modules. Every helper
/// is asynchronous: WinUI dialogs cannot be pumped synchronously.
/// </summary>
public sealed class DialogService
{
    private static readonly SolidColorBrush DangerHintBrush = new(Microsoft.UI.ColorHelper.FromArgb(255, 196, 43, 28));

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

    public async Task<bool> ConfirmAsync(string title, string message, string primaryText = "Continuar")
    {
        var panel = new StackPanel { Spacing = 12, MaxWidth = 430 };
        panel.Children.Add(new FontIcon { Glyph = "\uE7BA", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });

        var dialog = new ContentDialog
        {
            XamlRoot = Root,
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancelar",
            DefaultButton = ContentDialogButton.Close
        };

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

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        var extensions = ParseExtensions(filter);
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
    }

    public async Task<string?> PickFolderAsync(string description, string? initialPath = null)
    {
        _ = description;
        _ = initialPath; // Folder start locations are not supported by the WinRT picker.

        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");

        WinRT.Interop.InitializeWithWindow.Initialize(picker, WindowHandle);
        var folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
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
