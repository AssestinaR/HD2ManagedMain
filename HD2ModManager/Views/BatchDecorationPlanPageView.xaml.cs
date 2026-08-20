using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class BatchDecorationPlanPageView : UserControl
{
    public BatchDecorationPlanPageView() => InitializeComponent();

    private void OnSelectionRequested(object? sender, ModListSelectionRequestEventArgs e)
    {
        if (DataContext is BatchDecorationPlanPageViewModel viewModel)
            viewModel.ApplySourceSelection(e.SelectedKeys);
    }

    private void OnToggleOptionFilterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is BatchDecorationPlanPageViewModel viewModel)
            viewModel.ShowOptions = !viewModel.ShowOptions;
    }

    private void OnSourceUnitsPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var current = sender as DependencyObject;
        while (current is not null && current is not AnimatedPlanList)
            current = VisualTreeHelper.GetParent(current);
        if (current is not AnimatedPlanList list) return;
        list.ScrollByMouseWheelDelta(e.Delta);
        e.Handled = true;
    }
}
