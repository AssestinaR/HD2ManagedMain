using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：在高级详情全宽页面显示时启动按需读取；明确离开页面时由外部负责取消读取。
    public partial class AdvancedModDetailsPageView : UserControl
    {
        public AdvancedModDetailsPageView() => InitializeComponent();

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is AdvancedModDetailsPageViewModel viewModel)
            {
                HideLegacyAdvancedAnalysisButton(viewModel);
                await viewModel.RefreshAdvancedDetailsAsync();
            }
        }

        private void HideLegacyAdvancedAnalysisButton(AdvancedModDetailsPageViewModel viewModel)
        {
            foreach (var button in FindDescendants<Button>(this))
            {
                if (ReferenceEquals(button.Command, viewModel.RunAdvancedAnalysisCommand))
                {
                    button.Visibility = Visibility.Collapsed;
                    return;
                }
            }
        }

        private void OnTablePreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DataGrid table || FindInnerScrollViewer(table) is not ScrollViewer tableScrollViewer)
            {
                return;
            }

            var scrollingUp = e.Delta > 0;
            var isAtTop = tableScrollViewer.VerticalOffset <= 0.5;
            var isAtBottom = tableScrollViewer.VerticalOffset >= tableScrollViewer.ScrollableHeight - 0.5;
            if ((!scrollingUp || !isAtTop) && (scrollingUp || !isAtBottom))
            {
                return;
            }

            e.Handled = true;
            PageScrollViewer.ScrollToVerticalOffset(Math.Max(0, PageScrollViewer.VerticalOffset - e.Delta / 3d));
        }

        private static ScrollViewer? FindInnerScrollViewer(DependencyObject current)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is ScrollViewer scrollViewer)
                {
                    return scrollViewer;
                }

                var nestedScrollViewer = FindInnerScrollViewer(child);
                if (nestedScrollViewer is not null)
                {
                    return nestedScrollViewer;
                }
            }

            return null;
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject current) where T : DependencyObject
        {
            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++)
            {
                var child = VisualTreeHelper.GetChild(current, index);
                if (child is T typed) yield return typed;
                foreach (var nested in FindDescendants<T>(child)) yield return nested;
            }
        }
    }
}
