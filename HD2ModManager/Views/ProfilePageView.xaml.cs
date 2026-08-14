using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class ProfilePageView : UserControl
{
    public ProfilePageView() => InitializeComponent();

    private void OnProfileRowClick(object sender, ModListRowEventArgs e)
    {
        if (DataContext is ProfilePageViewModel vm && e.Item is ProfileListItemViewModel item)
            vm.SelectRow(item.Guid, e.Modifiers);
    }

    private void OnOpenDetailsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ProfilePageViewModel vm) return;
        if ((sender as FrameworkElement)?.DataContext is not ProfileListItemViewModel item) return;
        var shell = (Application.Current?.MainWindow as MainWindow)?.DataContext as ShellViewModel;
        shell?.OpenModDetailsFromPage(vm, item.Guid);
        e.Handled = true;
    }

    private void OnOpenDetailsOnRightClick(object sender, ModListRowEventArgs e)
    {
        if (DataContext is not ProfilePageViewModel vm || e.Item is not ProfileListItemViewModel item) return;
        var shell = (Application.Current?.MainWindow as MainWindow)?.DataContext as ShellViewModel;
        shell?.OpenModDetailsFromPage(vm, item.Guid);
    }

    private void OnRowActionMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => ClearTransientSelection();

    private void OnListBackgroundClick(object? sender, EventArgs e) => ClearTransientSelection();

    private void OnToggleOutdatedFilterClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is ProfilePageViewModel vm) vm.ShowOnlyOutdated = !vm.ShowOnlyOutdated;
    }

    private static void ClearTransientSelection()
    {
        if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
            shell.ClearTransientSelection();
    }
}
