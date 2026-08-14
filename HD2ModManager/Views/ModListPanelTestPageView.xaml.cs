using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class ModListPanelTestPageView : UserControl
{
    public ModListPanelTestPageView() => InitializeComponent();

    private void OnRowActionInvoked(object? sender, ModListRowActionEventArgs e)
    {
        if (DataContext is ModListPanelTestPageViewModel vm && e.Item is ModListPanelTestItem item)
            vm.RecordAction(e.Action, item);
    }

    private void OnSelectionRequested(object? sender, ModListSelectionRequestEventArgs e)
    {
        if (DataContext is ModListPanelTestPageViewModel vm)
            vm.ApplySelection(e.SelectedKeys);
    }
}
