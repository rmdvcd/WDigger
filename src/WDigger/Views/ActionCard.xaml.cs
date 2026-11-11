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

    /// <summary>
    /// Dependency property for the card's view-model. Assigning the view-model
    /// through this property (instead of setting DataContext in the
    /// DataTemplate) avoids the WinUI 3 generated-bindings DataContext
    /// recursion that causes a stack overflow crash (0xc00000fd).
    /// </summary>
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ActionCardViewModel), typeof(ActionCard), new PropertyMetadata(null, OnViewModelChanged));

    public ActionCardViewModel? ViewModel
    {
        get => (ActionCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ActionCard card)
        {
            return;
        }

        if (card._viewModel is not null)
        {
            card._viewModel.PropertyChanged -= card.ViewModel_PropertyChanged;
        }

        card._viewModel = e.NewValue as ActionCardViewModel;

        if (card._viewModel is not null)
        {
            card._viewModel.PropertyChanged += card.ViewModel_PropertyChanged;
        }

        card.UpdateRunButtonState();
    }

    public ActionCard()
    {
        InitializeComponent();
    }

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        await ExecuteAsync();
    }

    private async void Card_Tapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        if (IsDescendantOf(e.OriginalSource as DependencyObject, RunButton))
        {
            return;
        }

        await ExecuteAsync();
    }

    private async Task ExecuteAsync()
    {
        if (ViewModel is not ActionCardViewModel viewModel || viewModel.IsRunning)
        {
            return;
        }

        _viewModel = viewModel;
        try
        {
            UpdateRunButtonState();
            await viewModel.ExecuteAsync();
        }
        finally
        {
            _viewModel = null;
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
            RunButton.IsEnabled = _viewModel.IsAvailable && !_viewModel.IsRunning;
            RunButton.Content = _viewModel.IsRunning ? "Ejecutando…" : _viewModel.IsAvailable ? "Ejecutar" : "No disponible";
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
