using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WDigger.UI;

/// <summary>
/// Builds a themed, read-only grid from tabular data. WinUI 3 has no built-in
/// DataGrid, so tables are materialized as a Grid of TextBlocks on demand.
/// Tables are capped to keep the layout responsive.
/// </summary>
public static class TableRenderer
{
    private const int MaxRows = 400;

    public static FrameworkElement Create(string title, IReadOnlyList<string> columns, IEnumerable<string[]> rows)
    {
        var materialized = rows.Take(MaxRows + 1).ToList();
        var truncated = materialized.Count > MaxRows;
        if (truncated) materialized = materialized.Take(MaxRows).ToList();

        var headerBackground = ResolveBrush("AppAccentSoftBrush");
        var rowBackground = ResolveBrush("LayerFillColorDefaultBrush");
        var stroke = ResolveBrush("CardStrokeColorDefaultBrush");
        var header = new TextBlock
        {
            Text = truncated ? $"{title} · {MaxRows}+ filas (truncada)" : $"{title} · {materialized.Count} filas",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize = 13,
            Margin = new Thickness(2, 0, 0, 8)
        };
        var grid = new Grid();

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        for (var rowIndex = 0; rowIndex <= materialized.Count; rowIndex++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        void AddCell(string text, int row, int column, bool isHeader)
        {
            var cell = new Border
            {
                Background = isHeader ? headerBackground : row % 2 == 0 ? rowBackground : null,
                Padding = new Thickness(10, 7, 10, 7)
            };
            cell.Child = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                FontWeight = isHeader ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal
            };
            Grid.SetRow(cell, row);
            Grid.SetColumn(cell, column);
            grid.Children.Add(cell);
        }

        for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++) AddCell(columns[columnIndex], 0, columnIndex, isHeader: true);
        for (var rowIndex = 0; rowIndex < materialized.Count; rowIndex++)
        {
            var row = materialized[rowIndex];
            for (var columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                AddCell(columnIndex < row.Length ? row[columnIndex] : string.Empty, rowIndex + 1, columnIndex, isHeader: false);
            }
        }

        var border = new Border
        {
            BorderBrush = stroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
        var panel = new StackPanel { Spacing = 0 };
        panel.Children.Add(header);
        panel.Children.Add(border);
        return panel;
    }

    private static Brush? ResolveBrush(string key)
    {
        try { return Application.Current.Resources[key] as Brush; }
        catch { return null; }
    }
}
