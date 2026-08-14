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

    private void OnRowActionInvoked(object? sender, ModListRowActionEventArgs e)
    {
        if (DataContext is not ProfilePageViewModel vm || e.Item is not ProfileListItemViewModel item) return;
        ClearTransientSelection();
        var shell = (Application.Current?.MainWindow as MainWindow)?.DataContext as ShellViewModel;
        switch (e.Action)
        {
            case ModListRowAction.Details:
                shell?.OpenModDetailsFromPage(vm, item.Guid);
                break;
            case ModListRowAction.MoveUp:
                vm.MoveUpCommand.Execute(item.Guid);
                break;
            case ModListRowAction.MoveDown:
                vm.MoveDownCommand.Execute(item.Guid);
                break;
            case ModListRowAction.RemoveFromProfile:
                vm.RemoveModCommand.Execute(item.Guid);
                break;
            case ModListRowAction.DeleteFromLibrary:
                vm.DeleteModFromLibraryCommand.Execute(item.Guid);
                break;
        }
    }

    private void OnOpenDetailsOnRightClick(object sender, ModListRowEventArgs e)
    {
        if (DataContext is not ProfilePageViewModel vm || e.Item is not ProfileListItemViewModel item) return;
        var shell = (Application.Current?.MainWindow as MainWindow)?.DataContext as ShellViewModel;
        shell?.OpenModDetailsFromPage(vm, item.Guid);
    }

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
