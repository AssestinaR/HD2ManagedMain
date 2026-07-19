using System.Windows.Controls;
using Microsoft.Win32;

namespace HD2ModManager.Views
{
    // 作用：承载跨护甲左侧计划、来源和目标预测数据表。
    public partial class CrossArmorPlanPageView : UserControl
    {
        public CrossArmorPlanPageView() => InitializeComponent();

        private void OnBrowseOutputClick(object sender, System.Windows.RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Title = "选择跨护甲验证候选输出文件夹", Multiselect = false };
            if (dialog.ShowDialog() == true && DataContext is CrossArmorTransferPlanWindowViewModel { CandidateOutput: { } output }) output.SetOutputDirectory(dialog.FolderName);
        }
    }
}