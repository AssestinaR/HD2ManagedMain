using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class DecorationPlanPageView : UserControl
{
    public DecorationPlanPageView() => InitializeComponent();

    private void OnSelectionRequested(object? sender, ModListSelectionRequestEventArgs e)
    {
        if (DataContext is DecorationPlanPageViewModel vm)
            vm.ApplyTargetSelection(e.SelectedKeys);
    }

    private void OnToggleOptionFilterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is DecorationPlanPageViewModel vm)
            vm.ShowOptions = !vm.ShowOptions;
    }
}
