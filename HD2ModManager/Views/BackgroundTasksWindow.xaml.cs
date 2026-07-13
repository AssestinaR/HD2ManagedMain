using System.Windows;
using System.Windows.Controls;
using HD2ModManager.Services;

namespace HD2ModManager.Views
{
    // 作用：显示全部后台任务记录，使用虚拟化列表承载较长任务历史。
    public partial class BackgroundTasksWindow : Window
    {
        public BackgroundTasksWindow(BackgroundTaskService tasks)
        {
            InitializeComponent();
            DataContext = tasks;
        }

        private void OnCancelTaskClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: BackgroundTaskItem task })
            {
                task.Cancel();
            }
        }
    }
}
