using System.Windows;
using System.Windows.Controls;
using System.Linq;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

public partial class ExportPackagePageView : UserControl
{
    public ExportPackagePageView() => InitializeComponent();

    private void OnBrowseOutputClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportPackagePageViewModel vm) return;
        var dialog = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = vm.OutputDirectory };
        if (dialog.ShowDialog() == true) vm.OutputDirectory = dialog.FolderName;
    }

    private void OnEditImageClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ExportPackagePageViewModel || sender is not Button { Tag: ExportPackageEntry entry }) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.webp" };
        if (dialog.ShowDialog() == true) entry.ImagePath = dialog.FileName;
    }

    private void OnCandidateSelectionRequested(object? sender, ModListSelectionRequestEventArgs e)
    {
        if (DataContext is ExportPackagePageViewModel vm && e.SelectedKeys.Count > 0)
            vm.SelectCandidate(vm.Candidates.FirstOrDefault(candidate => candidate.SelectionKey.Equals(e.SelectedKeys[0], StringComparison.OrdinalIgnoreCase)));
    }
}
