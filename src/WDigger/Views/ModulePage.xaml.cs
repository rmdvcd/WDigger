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

        try
        {
            var actions = entry.Module.CreateActions(shell.Host);
            ModuleBadge.Text = $"{actions.Count} acciones";
            ActionCards.ItemsSource = actions
                .Select(action => new ActionCardViewModel(action, shell.RunActionAsync))
                .ToList();
        }
        catch (Exception exception)
        {
            // A module that fails to build its action list must not crash the app.
            ModuleBadge.Text = "Error al cargar acciones";
            ActionCards.ItemsSource = new List<ActionCardViewModel>
            {
                new ActionCardViewModel(
                    new ModuleAction(
                        "Error del módulo",
                        $"No se pudieron cargar las acciones de {entry.Module.Name}: {exception.Message}",
                        (_, _) => Task.CompletedTask),
                    _ => Task.CompletedTask)
            };
            shell.AppendOutput($"Módulo '{entry.Module.Name}' falló al cargar: {exception.Message}", OutputKind.Error);
        }
    }
}
