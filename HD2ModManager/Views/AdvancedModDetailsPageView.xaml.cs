using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

        private void OnTablePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid table || FindScrollViewer(table) is not ScrollViewer tableScrollViewer)
            {
                return;
            }

            var scrollingUp = e.Delta > 0;
            var isAtTop = tableScrollViewer.VerticalOffset <= 0;
            var isAtBottom = tableScrollViewer.VerticalOffset >= tableScrollViewer.ScrollableHeight;
            if ((!scrollingUp || !isAtTop) && (scrollingUp || !isAtBottom))
            {
                return;
            }

            e.Handled = true;
            PageScrollViewer.ScrollToVerticalOffset(Math.Max(0, PageScrollViewer.VerticalOffset - e.Delta));
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject current)
        {
            for (var parent = VisualTreeHelper.GetParent(current); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            {
                if (parent is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }
            }

            return null;
        }
    }
}