using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.ComponentModel;

namespace WDigger.Views;

/// <summary>
/// A single module action rendered as a card. Clicking anywhere on the card
/// runs the action; taps that land on the Ejecutar button are ignored here so
/// the action runs exactly once. The card shows an "Ejecutando…" state while
/// the action runs to prevent double execution and give visual feedback.
/// </summary>
public sealed partial class ActionCard : UserControl
{
    private ActionCardViewModel? _viewModel;

    public ActionCard()
    {
        InitializeComponent();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ActionCardViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateRunButtonState();
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
            _viewModel = viewModel;
            viewModel.PropertyChanged += ViewModel_PropertyChanged;
            UpdateRunButtonState();
            await viewModel.ExecuteAsync();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ActionCardViewModel.IsRunning))
        {
            UpdateRunButtonState();
        }
    }

    private void UpdateRunButtonState()
    {
        if (_viewModel is not null)
        {
            RunButton.IsEnabled = !_viewModel.IsRunning;
            RunButton.Content = _viewModel.IsRunning ? "Ejecutando…" : "Ejecutar";
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
