using Microsoft.UI.Xaml.Controls;
using WDigger.Core;
using WDigger.Modules;
using WDigger.UI;

namespace WDigger.Views;

/// <summary>
/// A module screen: header plus one card per action. Cards execute through
/// the shell so the busy guard, confirmation and output pane stay centralized.
/// </summary>
public sealed partial class ModulePage : UserControl
{
    private List<ActionCardViewModel> _actions = new();
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
            _actions = actions
                .Select(action => new ActionCardViewModel(action, shell.RunActionAsync, shell.TraceUi))
                .ToList();
            ApplySearch();
        }
        catch (Exception exception)
        {
            // A module that fails to build its action list must not crash the app.
            ModuleBadge.Text = "Error al cargar acciones";
            _actions = new List<ActionCardViewModel>
            {
                new ActionCardViewModel(
                    new ModuleAction(
                        "Error del módulo",
                        $"No se pudieron cargar las acciones de {entry.Module.Name}: {exception.Message}",
                        (_, _) => Task.CompletedTask),
                    _ => Task.CompletedTask)
            };
            ApplySearch();
            shell.AppendOutput($"Módulo '{entry.Module.Name}' falló al cargar: {exception.Message}", OutputKind.Error);
        }
    }
    internal void SetSearch(string text) => ActionSearch.Text = text;

    private void ActionSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplySearch();

    private void ApplySearch()
    {
        if (ActionCards is null || ActionSearch is null) return;
        var query = ActionSearch.Text.Trim();
        var matches = _actions.Where(item => WDigger.Core.ActionSearch.Matches(query, item.Title, item.Description)).ToList();
        ActionCards.ItemsSource = matches;
        ModuleBadge.Text = $"{matches.Count} de {_actions.Count} acciones";
    }
}
