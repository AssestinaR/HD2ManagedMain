using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using HD2ModManager.Enums;
using HD2ModManager.ViewModels;

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

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmExtendFrameIntoClientArea(IntPtr hWnd, ref MARGINS pMarInset);

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        const int DWMWA_NCRENDERING_POLICY = 2; // DWMNCRP_ENABLED
        const int WM_GETMINMAXINFO = 0x0024;
        const uint MONITOR_DEFAULTTONEAREST = 2;

        public MainWindow()
        {
            DataContext = new ShellViewModel();
            InitializeComponent();
            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += MainWindow_StateChanged; // 处理最大化时的阴影切换
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
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowMessageHook);
            // 启用非客户区渲染（让DWM参与绘制，从而出现系统阴影）
            int val = 2; // DWMNCRP_ENABLED
            DwmSetWindowAttribute(hwnd, DWMWA_NCRENDERING_POLICY, ref val, sizeof(int));

            ApplyWindowChromeMetrics(isMaximized: WindowState == WindowState.Maximized);
            ApplyDwmShadowMargins(isMaximized: WindowState == WindowState.Maximized);
        }

        // 无边框窗口的最大化尺寸由工作区明确约束，避免覆盖任务栏或超出屏幕边界。
        private static IntPtr WindowMessageHook(
            IntPtr hwnd,
            int msg,
            IntPtr wParam,
            IntPtr lParam,
            ref bool handled)
        {
            if (msg != WM_GETMINMAXINFO || lParam == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
            {
                return IntPtr.Zero;
            }

            var maxInfo = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            maxInfo.ptMaxPosition = new POINT
            {
                x = monitorInfo.rcWork.left - monitorInfo.rcMonitor.left,
                y = monitorInfo.rcWork.top - monitorInfo.rcMonitor.top
            };
            maxInfo.ptMaxSize = new POINT
            {
                x = monitorInfo.rcWork.right - monitorInfo.rcWork.left,
                y = monitorInfo.rcWork.bottom - monitorInfo.rcWork.top
            };
            Marshal.StructureToPtr(maxInfo, lParam, fDeleteOld: false);
            handled = true;
            return IntPtr.Zero;
        }

        // 最大化时不保留 WindowChrome 的调整边框，避免它与系统最大化边界叠加；普通状态恢复正常的拖拽边界。
        private void ApplyWindowChromeMetrics(bool isMaximized)
        {
            var chrome = WindowChrome.GetWindowChrome(this);
            if (chrome == null) return;

            chrome.ResizeBorderThickness = isMaximized
                ? new Thickness(0)
                : new Thickness(6);
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
            ApplyWindowChromeMetrics(isMaximized: WindowState == WindowState.Maximized);
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
                    shell.Navigate(WorkspaceMode.Home);
                    e.Handled = true;
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}