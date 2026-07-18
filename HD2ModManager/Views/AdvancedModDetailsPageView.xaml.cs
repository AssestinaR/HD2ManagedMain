using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：在高级详情全宽页面显示时启动按需读取，并在离开页面时取消读取。
    public partial class AdvancedModDetailsPageView : UserControl
    {
        public AdvancedModDetailsPageView() => InitializeComponent();

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdvancedModDetailsPageViewModel viewModel)
            {
                await viewModel.RefreshAdvancedDetailsAsync();
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdvancedModDetailsPageViewModel viewModel)
            {
                viewModel.CancelAdvancedDetails();
            }
        }
    }
}