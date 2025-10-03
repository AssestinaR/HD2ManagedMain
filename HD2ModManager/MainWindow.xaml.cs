using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace HD2ModManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [StructLayout(LayoutKind.Sequential)]
        struct MARGINS
        {
            public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        const int DWMWA_NCRENDERING_POLICY = 2; // DWMNCRP_ENABLED

        public MainWindow()
        {
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += MainWindow_StateChanged; // 处理最大化时的阴影切换
            DataContext = new HD2ModManager.ViewModels.ShellViewModel();
            Title = HD2ModManager.Resources.Strings.App_Title;
            AllowDrop = true;
            DragOver += MainWindow_DragOver;
            Drop += MainWindow_Drop;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Ignore drag when the click originates from interactive controls (tab headers or caption buttons)
            if (e.OriginalSource is DependencyObject d)
            {
                if (FindAncestor<TabControl>(d) != null || FindAncestor<Button>(d) != null)
                {
                    return; // let Tab/Buttons handle the click
                }
            }
            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
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

        private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void MaxButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void MainWindow_SourceInitialized(object? sender, EventArgs e)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // 启用非客户区渲染（让DWM参与绘制，从而出现系统阴影）
            int val = 2; // DWMNCRP_ENABLED
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref val, sizeof(int));

            ApplyDwmShadowMargins(isMaximized: WindowState == WindowState.Maximized);
        }

        // 根据最大化状态设置边距：正常时给极小边距，最大化时清零避免闪烁
        private void ApplyDwmShadowMargins(bool isMaximized)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            MARGINS m;
            if (isMaximized)
            {
                m = new MARGINS { cxLeftWidth = 0, cxRightWidth = 0, cyTopHeight = 0, cyBottomHeight = 0 };
            }
            else
            {
                // 给1像素，让DWM在非客户区绘制从而出现系统阴影
                m = new MARGINS { cxLeftWidth = 1, cxRightWidth = 1, cyTopHeight = 0, cyBottomHeight = 1 };
            }
            DwmExtendFrameIntoClientArea(hwnd, ref m);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            ApplyDwmShadowMargins(isMaximized: WindowState == WindowState.Maximized);
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (DataContext is HD2ModManager.ViewModels.ShellViewModel shell)
            {
                await shell.ProcessImportQueueAsync(files);
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                if (DataContext is HD2ModManager.ViewModels.ShellViewModel shell)
                {
                    var crumbs = shell.Breadcrumbs;
                    var targetIndex = crumbs.Count - 2; // go to previous level
                    if (targetIndex >= 0)
                    {
                        shell.GoBackToIndexCommand.Execute(targetIndex);
                        e.Handled = true;
                    }
                }
            }
        }
    }
}