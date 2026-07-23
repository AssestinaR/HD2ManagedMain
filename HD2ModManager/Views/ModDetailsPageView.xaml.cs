using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：显示单个 Mod 的详情信息与派生文件组概览。
    public partial class ModDetailsPageView : UserControl
    {
        public ModDetailsPageView()
        {
            InitializeComponent();
        }

        private void OnSelectImageClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 Mod 图像",
                Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|所有文件|*.*",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true && DataContext is ModDetailsPageViewModel details)
            {
                details.UpdateImageCommand.Execute(dialog.FileName);
            }
        }

        private void OnEditNameClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModDetailsPageViewModel details) return;
            if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
                shell.BeginBottomBarNameEdit(details.ModId, details.Name);
        }

        private void OnEditDescriptionClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ModDetailsPageViewModel details) return;
            if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
                shell.BeginBottomBarDescriptionEdit(details.ModId, details.Mod?.Description ?? string.Empty);
        }
    }
}
