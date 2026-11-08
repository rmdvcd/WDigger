using WDigger.UI;

namespace WDigger.Views;

/// <summary>
/// View-model for one action card: the action plus presentation state.
/// </summary>
public sealed class ActionCardViewModel
{
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DangerBackground = new(
        Microsoft.UI.ColorHelper.FromArgb(0x26, 0xC4, 0x2B, 0x1C));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush DangerForeground = new(
        Microsoft.UI.ColorHelper.FromArgb(0xFF, 0xE06C60));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SafeBackground = new(
        Microsoft.UI.ColorHelper.FromArgb(0x26, 0x10, 0x7C, 0x10));
    private static readonly Microsoft.UI.Xaml.Media.SolidColorBrush SafeForeground = new(
        Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x4CC26A));

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

    public ModuleAction Action { get; }
    public string Title { get; }
    public string Description { get; }
    public bool ChangesSystem { get; }
    public string BadgeText { get; }
    public Microsoft.UI.Xaml.Media.Brush BadgeBackground { get; }
    public Microsoft.UI.Xaml.Media.Brush BadgeForeground { get; }

    public Task ExecuteAsync() => _run(Action);
}
