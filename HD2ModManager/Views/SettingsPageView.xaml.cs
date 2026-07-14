using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：设置页视图，仅承载需要 UI 对话框的轻量桥接。
    public partial class SettingsPageView : UserControl
    {
        public SettingsPageView()
        {
            InitializeComponent();
            Loaded += SettingsPageView_Loaded;
        }

        private void SettingsPageView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;
            vm.PromptLanguageIfMissing();
            vm.Refresh();
        }

        private void BrowseModFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;
            var selected = BrowseFolder("选择 Mod 库文件夹", vm.ModLibraryFolder);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                vm.ModLibraryFolder = selected;
            }
        }

        private void BrowseGameDataFolder_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;
            var selected = BrowseFolder("选择 Helldivers 2 data 文件夹", vm.GameDataFolder);
            if (!string.IsNullOrWhiteSpace(selected))
            {
                vm.GameDataFolder = selected;
            }
        }

        private static string? BrowseFolder(string title, string currentPath)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = title,
                Multiselect = false,
            };

            if (!string.IsNullOrWhiteSpace(currentPath) && System.IO.Directory.Exists(currentPath))
            {
                dialog.InitialDirectory = currentPath;
            }

            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        }
    }
}
