using System.Windows.Controls;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views
{
    // 作用：展示并维护当前 Profile 中已启用的模组列表。
    public partial class ProfilePageView : UserControl
    {
        public ProfilePageView()
        {
            InitializeComponent();
        }

        private void OnProfileRowClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Button>(source) != null) return;
            if (DataContext is ProfilePageViewModel vm && (sender as FrameworkElement)?.DataContext is ProfileListItemViewModel item)
            {
                vm.SelectRow(item.Guid, System.Windows.Input.Keyboard.Modifiers);
                e.Handled = true;
            }
        }

        private void OnOpenDetailsClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ProfilePageViewModel vm) return;
            if ((sender as FrameworkElement)?.DataContext is not ProfileListItemViewModel item) return;
            var shell = (Application.Current?.MainWindow as MainWindow)?.DataContext as ShellViewModel;
            shell?.OpenModDetailsFromPage(vm, item.Guid);
            e.Handled = true;
        }

        private void OnRowActionMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ClearTransientSelection();
        }

        private void OnListBackgroundMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindAncestor<Border>(source) is { DataContext: ProfileListItemViewModel }) return;
            ClearTransientSelection();
        }

        private static void ClearTransientSelection()
        {
            if (Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
                shell.ClearTransientSelection();
        }

        private void OnToggleSearchClick(object sender, RoutedEventArgs e)
        {
            if (HeaderSearchBox.Visibility == Visibility.Visible)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                fadeOut.Completed += (_, _) =>
                {
                    HeaderSearchBox.Visibility = Visibility.Collapsed;
                    HeaderTitle.Visibility = Visibility.Visible;
                    HeaderSummary.Visibility = Visibility.Visible;
                    HeaderTitle.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                    HeaderSummary.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
                };
                HeaderSearchBox.BeginAnimation(OpacityProperty, fadeOut);
                var actionFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
                actionFadeOut.Completed += (_, _) => HeaderActionPanel.Visibility = Visibility.Collapsed;
                HeaderActionPanel.BeginAnimation(OpacityProperty, actionFadeOut);
                return;
            }

            var titleFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            titleFadeOut.Completed += (_, _) => HeaderTitle.Visibility = Visibility.Collapsed;
            HeaderTitle.BeginAnimation(OpacityProperty, titleFadeOut);
            var summaryFadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            summaryFadeOut.Completed += (_, _) => HeaderSummary.Visibility = Visibility.Collapsed;
            HeaderSummary.BeginAnimation(OpacityProperty, summaryFadeOut);
            HeaderActionPanel.Visibility = Visibility.Visible;
            HeaderActionPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            HeaderSearchBox.Visibility = Visibility.Visible;
            HeaderSearchBox.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            HeaderSearchBox.Focus();
        }

        private static T? FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
    }
}
