using System.ComponentModel;
using System.Runtime.CompilerServices;
using WDigger.UI;

namespace WDigger.Views;

/// <summary>
/// View-model for one action card: the action plus presentation state.
/// </summary>
public sealed class ActionCardViewModel : INotifyPropertyChanged
{
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DangerBackground = new(
        Microsoft.UI.ColorHelper.FromArgb(0x26, 0xC4, 0x2B, 0x1C));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DangerForeground = new(
        Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE0, 0x6C, 0x60));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SafeBackground = new(
        Microsoft.UI.ColorHelper.FromArgb(0x26, 0x10, 0x7C, 0x10));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SafeForeground = new(
        Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4C, 0xC2, 0x6A));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RunningBackground = new(
        Microsoft.UI.ColorHelper.FromArgb(0x26, 0x0F, 0x6C, 0xBD));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush RunningForeground = new(
        Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x8A, 0xC9, 0xFF));

    public ActionCardViewModel(ModuleAction action, Func<ModuleAction, Task> run)
    {
        Action = action;
        Title = action.Title;
        Description = action.Description;
        ChangesSystem = action.ChangesSystem;
        BadgeText = action.ChangesSystem ? "Modifica el sistema" : "Solo lectura";
        BadgeBackground = action.ChangesSystem ? DangerBackground : SafeBackground;
        BadgeForeground = action.ChangesSystem ? DangerForeground : SafeForeground;
        _run = run;
    }

    private readonly Func<ModuleAction, Task> _run;

    private bool _isRunning;

    public ModuleAction Action { get; }
    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public string BadgeText { get; }
    public Microsoft.UI.Xaml.Media.Brush BadgeBackground { get; }
    public Microsoft.UI.Xaml.Media.Brush BadgeForeground { get; }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (_isRunning != value)
            {
                _isRunning = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(RunningBadgeBackground));
                OnPropertyChanged(nameof(RunningBadgeText));
            }
        }
    }

    public Microsoft.UI.Xaml.Media.Brush RunningBadgeBackground => IsRunning ? RunningBackground : BadgeBackground;
    public string RunningBadgeText => IsRunning ? "Ejecutando…" : BadgeText;

    public async Task ExecuteAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        try
        {
            await _run(Action);
        }
        finally
        {
            IsRunning = false;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
