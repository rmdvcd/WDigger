using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WDigger.Views;

/// <summary>
/// A single module action rendered as a card. Clicking anywhere on the card
/// runs the action; taps that land on the Ejecutar button are ignored here so
/// the action runs exactly once.
/// </summary>
public sealed partial class ActionCard : UserControl
{
    public ActionCard()
    {
        InitializeComponent();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ActionCardViewModel viewModel)
        {
            await viewModel.ExecuteAsync();
        }
    }

    private async void Card_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (IsDescendantOf(e.OriginalSource as DependencyObject, RunButton))
        {
            return;
        }

        if (DataContext is ActionCardViewModel viewModel)
        {
            await viewModel.ExecuteAsync();
        }
    }

    private static bool IsDescendantOf(DependencyObject? child, DependencyObject ancestor)
    {
        var current = child;
        while (current is not null)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }

            current = current is UIElement
                ? VisualTreeHelper.GetParent(current)
                : null;
        }

        return false;
    }
}
