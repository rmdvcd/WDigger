using Microsoft.UI.Xaml.Controls;
using WDigger.Modules;
using WDigger.UI;

namespace WDigger.Views;

/// <summary>
/// A module screen: header plus one card per action. Cards execute through
/// the shell so the busy guard, confirmation and output pane stay centralized.
/// </summary>
public sealed partial class ModulePage : UserControl
{
    public ModulePage(ShellWindow shell, ModuleCatalog.Entry entry)
    {
        InitializeComponent();

        ModuleIcon.Glyph = entry.Glyph;
        ModuleTitle.Text = entry.Module.Name;
        ModuleDescription.Text = entry.Module.Description;

        var actions = entry.Module.CreateActions(shell.Host);
        ModuleBadge.Text = $"{actions.Count} acciones";
        ActionCards.ItemsSource = actions
            .Select(action => new ActionCardViewModel(action, shell.RunActionAsync))
            .ToList();
    }
}
