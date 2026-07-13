using System.Windows.Controls;
using System.Windows.Input;
using System.Windows;

namespace HD2ModManager.Views
{
    // 作用：展示新版工作区的运行状态与路径信息。
    public partial class StatusPageView : UserControl
    {
        public StatusPageView()
        {
            InitializeComponent();
        }
        private void OnShowAllTasksClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.StatusPageViewModel viewModel)
            {
                viewModel.ShowAllTasksCommand.Execute(null);
            }
        }

        private void OnCancelTaskClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: Services.BackgroundTaskItem task })
            {
                task.Cancel();
            }
        }
    }
}
