using System.Windows;
using System.Windows.Input;
using ManagedMain.ViewModels;

namespace ManagedMain
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new ShellViewModel();
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (DataContext is ShellViewModel svm && svm.SelectedTab?.Content is ManagedMain.Views.ManagedMainView view && view.DataContext is ManagedMain.ViewModels.ManagedMainViewModel mm)
            {
                mm.Save();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                MaxButton_Click(sender, e);
                return;
            }
            try { DragMove(); } catch { }
        }

        private void MinButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaxButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal; else WindowState = WindowState.Maximized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}