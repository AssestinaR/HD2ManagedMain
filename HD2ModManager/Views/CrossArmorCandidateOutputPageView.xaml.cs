using Microsoft.Win32;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：仅通过系统目录选择器为跨护甲候选输出选择独立空目录。
    public partial class CrossArmorCandidateOutputPageView : UserControl
    {
        public CrossArmorCandidateOutputPageView() => InitializeComponent();
        private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择跨护甲验证候选输出文件夹", Multiselect = false };
            if (dialog.ShowDialog() == true && DataContext is CrossArmorCandidateOutputPageViewModel viewModel) viewModel.SetOutputDirectory(dialog.FolderName);
        }
    }
}