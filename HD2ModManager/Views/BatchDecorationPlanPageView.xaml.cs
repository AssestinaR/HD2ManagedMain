using System.Windows;
using System.Windows.Controls;
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
}
