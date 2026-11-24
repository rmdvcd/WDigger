using Microsoft.UI.Xaml.Controls;
using WDigger.Core;
using WDigger.Modules;
using WDigger.UI;

namespace WDigger.Views;

public sealed partial class SearchPage : UserControl
{
    private readonly ShellWindow _shell;
    private readonly List<SearchResult> _results = new();
    private int _failedModules;

    public SearchPage(ShellWindow shell, IEnumerable<ModuleCatalog.Entry> entries)
    {
        _shell = shell;
        InitializeComponent();
        foreach (var entry in entries)
        {
            try
            {
                _results.AddRange(entry.Module.CreateActions(shell.Host).Select(action =>
                    new SearchResult(entry.Tag, entry.Module.Name, action.Title, action.Description)));
            }
            catch (Exception exception)
            {
                _failedModules++;
                shell.AppendOutput($"No se pudo indexar {entry.Module.Name}: {exception.Message}", OutputKind.Warning);
            }
        }
        RefreshResults();
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => RefreshResults();

    private void RefreshResults()
    {
        if (Query is null || Results is null || CountLabel is null) return;
        var matches = _results.Where(item => ActionSearch.Matches(Query.Text, item.Category, item.Title, item.Description)).ToList();
        Results.ItemsSource = matches;
        CountLabel.Text = $"{matches.Count} de {_results.Count} acciones" +
            (_failedModules > 0 ? $" · {_failedModules} categoría(s) no disponible(s) para búsqueda" : "");
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SearchResult result) _shell.NavigateToAction(result.Tag, result.Title);
    }

    public sealed record SearchResult(string Tag, string Category, string Title, string Description);
}
