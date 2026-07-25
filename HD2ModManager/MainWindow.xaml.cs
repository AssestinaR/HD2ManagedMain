using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using HD2ModManager.Enums;
using HD2ModManager.Services;
using HD2ModManager.ViewModels;

namespace HD2ModManager
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private double _bottomBarAnimationStartWidth;
        private bool _bottomBarWidthUpdateQueued;
        private int _bottomBarAnimationVersion;
        private bool _pageTransitionQueued;
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

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        const int DWMWA_NCRENDERING_POLICY = 2; // DWMNCRP_ENABLED
        const int WM_GETMINMAXINFO = 0x0024;
        const int WM_NCHITTEST = 0x0084;
        const int HTTOP = 12;
        const uint MONITOR_DEFAULTTONEAREST = 2;

        public MainWindow()
        {
            DataContext = new ShellViewModel();
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            DataContextChanged += MainWindow_DataContextChanged;
            Closed += MainWindow_Closed;
            SourceInitialized += MainWindow_SourceInitialized;
            StateChanged += MainWindow_StateChanged; // 处理最大化时的阴影切换
            Title = HD2ModManager.Resources.Strings.App_Title;
            AllowDrop = true;
            DragOver += MainWindow_DragOver;
            Drop += MainWindow_Drop;
            PreviewKeyDown += MainWindow_PreviewKeyDown;
            PreviewMouseDown += MainWindow_PreviewMouseDown;
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
                RestoreFromMaximizedForDrag(e.GetPosition(this));
                DragMove();
            }
        }

        private void RestoreFromMaximizedForDrag(Point pointerPosition)
        {
            if (WindowState != WindowState.Maximized) return;

            var restoredBounds = RestoreBounds;
            var horizontalRatio = ActualWidth > 0 ? pointerPosition.X / ActualWidth : 0.5;
            var screenPointer = PointToScreen(pointerPosition);
            var dpi = VisualTreeHelper.GetDpi(this);

            WindowState = WindowState.Normal;
            Left = screenPointer.X / dpi.DpiScaleX - restoredBounds.Width * horizontalRatio;
            Top = screenPointer.Y / dpi.DpiScaleY - 16;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            SubscribeToShell(DataContext as ShellViewModel);
            ShowInitialPage(LeftCurrentPageHost, (DataContext as ShellViewModel)?.LeftPage);
            ShowInitialPage(RightCurrentPageHost, (DataContext as ShellViewModel)?.RightPage);
            Dispatcher.BeginInvoke(UpdateWorkspaceNavigationIndicator, System.Windows.Threading.DispatcherPriority.Loaded);
            RequestBottomContextBarWidthUpdate();
        }

        private void MainWindow_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SubscribeToShell(e.NewValue as ShellViewModel);
        }

        private async void MainWindow_Closed(object? sender, EventArgs e)
        {
            if (DataContext is ShellViewModel shell)
            {
                shell.PropertyChanged -= Shell_PropertyChanged;
                await shell.DisposeAsync();
            }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel { IsMessagePanelOpen: true } messageShell
                && e.OriginalSource is DependencyObject messageSource
                && !IsDescendantOf(messageSource, MessageCenterPanel)
                && !IsDescendantOf(messageSource, MessageNavigationButton))
            {
                messageShell.CloseMessagePanel();
            }
            if (DataContext is not ShellViewModel { BottomBar.HasTemporaryEditor: true } shell) return;
            if (e.OriginalSource is DependencyObject source && IsDescendantOf(source, BottomContextBar)) return;
            shell.CancelBottomBarEdit();
        }

        private static bool IsDescendantOf(DependencyObject source, DependencyObject ancestor)
        {
            while (source != null)
            {
                if (ReferenceEquals(source, ancestor)) return true;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        private void SubscribeToShell(ShellViewModel? shell)
        {
            if (shell == null) return;
            shell.PropertyChanged -= Shell_PropertyChanged;
            shell.PropertyChanged += Shell_PropertyChanged;
            shell.BottomBar.PropertyChanged -= BottomBar_PropertyChanged;
            shell.BottomBar.PropertyChanged += BottomBar_PropertyChanged;
            shell.Selection.SelectionChanged -= Selection_SelectionChanged;
            shell.Selection.SelectionChanged += Selection_SelectionChanged;
        }

        private void Selection_SelectionChanged(object? sender, EventArgs e)
        {
            RequestBottomContextBarWidthUpdate();
        }

        private void BottomBar_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(BottomBarCoordinator.HasContent)
                or nameof(BottomBarCoordinator.HasSelection)
                or nameof(BottomBarCoordinator.HasTemporaryEditor)
                or nameof(BottomBarCoordinator.ShowAddToProfile)
                or nameof(BottomBarCoordinator.ShowDelete)
                or nameof(BottomBarCoordinator.ShowRemove))
            {
                RequestBottomContextBarWidthUpdate();
            }
        }

        private void RequestBottomContextBarWidthUpdate()
        {
            if (BottomContextBar.Visibility == Visibility.Visible && BottomContextBar.ActualWidth > 0)
            {
                _bottomBarAnimationStartWidth = BottomContextBar.ActualWidth;
                BottomContextBar.BeginAnimation(FrameworkElement.WidthProperty, null);
                BottomContextBar.Width = _bottomBarAnimationStartWidth;
            }

            if (_bottomBarWidthUpdateQueued) return;
            _bottomBarWidthUpdateQueued = true;
            Dispatcher.BeginInvoke(UpdateBottomContextBar, System.Windows.Threading.DispatcherPriority.Render);
        }

        private async void UpdateBottomContextBar()
        {
            _bottomBarWidthUpdateQueued = false;
            var version = ++_bottomBarAnimationVersion;
            if (DataContext is not ShellViewModel { BottomBar.HasContent: true })
            {
                if (BottomContextBar.Visibility != Visibility.Visible) return;
                await AnimateBottomBarAsync(BottomContextBarContent, OpacityProperty, BottomContextBarContent.Opacity, 0, 110);
                if (version != _bottomBarAnimationVersion) return;
                var currentWidth = BottomContextBar.ActualWidth;
                await AnimateBottomBarAsync(BottomContextBar, FrameworkElement.WidthProperty, currentWidth, 0, 180);
                if (version != _bottomBarAnimationVersion) return;
                BottomContextBar.BeginAnimation(FrameworkElement.WidthProperty, null);
                BottomContextBar.Width = double.NaN;
                BottomContextBar.Visibility = Visibility.Collapsed;
                BottomContextBarContent.Opacity = 1;
                _bottomBarAnimationStartWidth = 0;
                return;
            }

            var isAppearing = BottomContextBar.Visibility != Visibility.Visible;
            if (isAppearing)
            {
                BottomContextBar.Visibility = Visibility.Visible;
                BottomContextBarContent.Opacity = 0;
                BottomContextBar.Width = double.NaN;
                BottomContextBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var appearanceTargetWidth = BottomContextBar.DesiredSize.Width;
                if (appearanceTargetWidth <= 0) return;
                BottomContextBar.Width = 0;
                await AnimateBottomBarAsync(BottomContextBar, FrameworkElement.WidthProperty, 0, appearanceTargetWidth, 190);
                if (version != _bottomBarAnimationVersion) return;
                BottomContextBar.Width = double.NaN;
                _bottomBarAnimationStartWidth = appearanceTargetWidth;
                await AnimateBottomBarAsync(BottomContextBarContent, OpacityProperty, 0, 1, 130);
                return;
            }

            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            if (version != _bottomBarAnimationVersion || BottomContextBar.Visibility != Visibility.Visible) return;

            var startWidth = _bottomBarAnimationStartWidth > 0 ? _bottomBarAnimationStartWidth : BottomContextBar.ActualWidth;
            BottomContextBar.Width = double.NaN;
            BottomContextBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var targetWidth = BottomContextBar.DesiredSize.Width;
            if (targetWidth <= 0) return;

            BottomContextBar.Width = startWidth;
            await AnimateBottomBarAsync(BottomContextBar, FrameworkElement.WidthProperty, startWidth, targetWidth, 220);
            if (version != _bottomBarAnimationVersion) return;

            _bottomBarAnimationStartWidth = targetWidth;
            BottomContextBar.BeginAnimation(FrameworkElement.WidthProperty, null);
            BottomContextBar.Width = double.NaN;
        }

        private static Task AnimateBottomBarAsync(DependencyObject target, DependencyProperty property, double from, double to, int milliseconds)
        {
            if (milliseconds <= 0 || Math.Abs(from - to) < 0.01)
            {
                target.SetValue(property, to);
                return Task.CompletedTask;
            }

            var completion = new TaskCompletionSource();
            var animation = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            animation.Completed += (_, _) => completion.TrySetResult();
            if (target is IAnimatable animatable)
            {
                animatable.BeginAnimation(property, animation);
            }
            else
            {
                target.SetValue(property, to);
                completion.TrySetResult();
            }
            return completion.Task;
        }


        private void Shell_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ShellViewModel shell) return;
            if (e.PropertyName == nameof(ShellViewModel.IsMessagePanelOpen))
            {
                AnimateMessagePanel(MessageCenterPanel, shell.IsMessagePanelOpen, 100, () => shell.IsMessagePanelOpen);
                return;
            }
            if (e.PropertyName == nameof(ShellViewModel.IsMessagePreviewOpen))
            {
                AnimateMessagePanel(MessagePreviewPanel, shell.IsMessagePreviewOpen, 50, () => shell.IsMessagePreviewOpen);
                return;
            }
            if (e.PropertyName == nameof(ShellViewModel.BottomBarStateVersion))
            {
                RequestBottomContextBarWidthUpdate();
                return;
            }
            if (e.PropertyName == nameof(ShellViewModel.LeftPage))
            {
                QueuePageTransitions();
            }
            else if (e.PropertyName == nameof(ShellViewModel.RightPage))
            {
                QueuePageTransitions();
            }
            else if (e.PropertyName is nameof(ShellViewModel.CurrentMode)
                or nameof(ShellViewModel.IsHomeActive)
                or nameof(ShellViewModel.IsProfileActive)
                or nameof(ShellViewModel.IsLibraryActive)
                or nameof(ShellViewModel.IsSplitActive)
                or nameof(ShellViewModel.IsSettingsActive))
            {
                Dispatcher.BeginInvoke(UpdateWorkspaceNavigationIndicator, System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void AnimateMessagePanel(FrameworkElement panel, bool isOpen, double offset, Func<bool> isStillOpen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (panel.RenderTransform is not TranslateTransform translate) return;
                panel.BeginAnimation(OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);

                if (isOpen)
                {
                    panel.Visibility = Visibility.Visible;
                    panel.Opacity = 0;
                    translate.Y = -offset;
                    translate.BeginAnimation(TranslateTransform.YProperty, CreateMessageAnimation(-offset, 0, 190));
                    panel.BeginAnimation(OpacityProperty, CreateMessageAnimation(0, 1, 150));
                    return;
                }

                if (panel.Visibility != Visibility.Visible) return;
                var close = CreateMessageAnimation(panel.Opacity, 0, 150);
                close.Completed += (_, _) =>
                {
                    if (!isStillOpen()) panel.Visibility = Visibility.Collapsed;
                };
                translate.BeginAnimation(TranslateTransform.YProperty, CreateMessageAnimation(translate.Y, -offset, 190));
                panel.BeginAnimation(OpacityProperty, close);
            }, System.Windows.Threading.DispatcherPriority.Render);
        }

        private static DoubleAnimation CreateMessageAnimation(double from, double to, int milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        private void OnMessageListLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListBox listBox) return;
            listBox.Items.CurrentChanged += (_, _) => ScrollMessagesToEnd(listBox);
            ScrollMessagesToEnd(listBox);
        }

        private void OnMessagePreviewClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is ShellViewModel shell) shell.OpenMessagePanel();
            e.Handled = true;
        }

        private void OnCopySelectedMessagesClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ShellViewModel shell) shell.CopySelectedMessagesCommand.Execute(MessageList.SelectedItems);
            e.Handled = true;
        }

        private static void ScrollMessagesToEnd(ListBox listBox)
        {
            if (listBox.Items.Count == 0) return;
            listBox.Dispatcher.BeginInvoke(() => listBox.ScrollIntoView(listBox.Items[listBox.Items.Count - 1]), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void UpdateWorkspaceNavigationIndicator()
        {
            if (DataContext is not ShellViewModel shell || !WorkspaceNavigationHost.IsLoaded) return;

            FrameworkElement? target = shell.CurrentMode switch
            {
                WorkspaceMode.Home => HomeNavigationButton,
                WorkspaceMode.Settings => SettingsNavigationButton,
                WorkspaceMode.ProfileOnly or WorkspaceMode.LibraryOnly or WorkspaceMode.ProfileLibrarySplit => WorkspaceCapsuleGroup.GetActiveButton(),
                _ => null
            };
            if (target is null || target.ActualWidth <= 0 || target.ActualHeight <= 0) return;

            var targetPoint = target.TransformToAncestor(WorkspaceNavigationHost).Transform(new Point(0, 0));
            var targetHeight = target.ActualHeight;
            var targetRadius = targetHeight / 2d;
            var isInitialPlacement = WorkspaceNavigationIndicator.ActualWidth <= 0;

            var currentPoint = WorkspaceNavigationIndicator.TransformToAncestor(WorkspaceNavigationHost).Transform(new Point(0, 0));
            var currentWidth = WorkspaceNavigationIndicator.ActualWidth;
            var currentHeight = WorkspaceNavigationIndicator.ActualHeight;

            WorkspaceNavigationIndicator.BeginAnimation(WidthProperty, null);
            WorkspaceNavigationIndicator.BeginAnimation(HeightProperty, null);
            WorkspaceNavigationIndicator.BeginAnimation(Canvas.LeftProperty, null);
            WorkspaceNavigationIndicator.BeginAnimation(Canvas.TopProperty, null);
            WorkspaceNavigationIndicator.BeginAnimation(OpacityProperty, null);

            if (!isInitialPlacement)
            {
                WorkspaceNavigationIndicator.Width = currentWidth;
                WorkspaceNavigationIndicator.Height = currentHeight;
                Canvas.SetLeft(WorkspaceNavigationIndicator, currentPoint.X);
                Canvas.SetTop(WorkspaceNavigationIndicator, currentPoint.Y);
            }

            if (isInitialPlacement)
            {
                WorkspaceNavigationIndicator.Width = target.ActualWidth;
                WorkspaceNavigationIndicator.Height = targetHeight;
                WorkspaceNavigationIndicator.CornerRadius = new CornerRadius(targetRadius);
                Canvas.SetLeft(WorkspaceNavigationIndicator, targetPoint.X);
                Canvas.SetTop(WorkspaceNavigationIndicator, targetPoint.Y);
                WorkspaceNavigationIndicator.Opacity = 1;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(240);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            WorkspaceNavigationIndicator.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(targetPoint.X, duration) { EasingFunction = easing });
            WorkspaceNavigationIndicator.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(targetPoint.Y, duration) { EasingFunction = easing });
            WorkspaceNavigationIndicator.BeginAnimation(WidthProperty, new DoubleAnimation(target.ActualWidth, duration) { EasingFunction = easing });
            WorkspaceNavigationIndicator.BeginAnimation(HeightProperty, new DoubleAnimation(targetHeight, duration) { EasingFunction = easing });
            WorkspaceNavigationIndicator.CornerRadius = new CornerRadius(targetRadius);
            WorkspaceNavigationIndicator.Opacity = 1;
        }

        private static void ShowInitialPage(ContentControl host, object? page)
        {
            host.Content = page;
            host.Opacity = 1;
        }

        private void QueuePageTransitions()
        {
            if (_pageTransitionQueued) return;
            _pageTransitionQueued = true;
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
            {
                _pageTransitionQueued = false;
                if (DataContext is not ShellViewModel shell) return;
                TransitionPage(LeftCurrentPageHost, LeftPreviousPageHost, shell.LeftPage);
                TransitionPage(RightCurrentPageHost, RightPreviousPageHost, shell.RightPage);
            }));
        }

        private void TransitionPage(ContentControl currentHost, ContentControl previousHost, object? nextPage)
        {
            if (ReferenceEquals(currentHost.Content, nextPage)) return;

            var transitionStopwatch = Stopwatch.StartNew();
            var previousMetrics = CaptureVisualMetrics(currentHost);
            var currentMetricsBefore = CaptureVisualMetrics(currentHost);
            var pageName = nextPage?.GetType().Name ?? "空页面";
            var firstLayoutLogged = false;
            var firstRenderLogged = false;

            currentHost.BeginAnimation(OpacityProperty, null);
            previousHost.BeginAnimation(OpacityProperty, null);
            previousHost.Content = currentHost.Content;
            previousHost.Opacity = previousHost.Content is null ? 0 : 1;
            currentHost.Content = nextPage;
            currentHost.Opacity = 0;

            EventHandler? layoutUpdated = null;
            layoutUpdated = (_, _) =>
            {
                if (firstLayoutLogged || !ReferenceEquals(currentHost.Content, nextPage)) return;
                firstLayoutLogged = true;
                var metrics = CaptureVisualMetrics(currentHost);
                LogService.Info($"UI 观测：{pageName} 首次布局，耗时 {transitionStopwatch.ElapsedMilliseconds}ms；新页视觉={metrics.VisualCount}，ListBox={metrics.ListBoxCount}，列表项={metrics.ListItemCount}，图片={metrics.ImageCount}；旧页视觉={previousMetrics.VisualCount}，旧页列表项={previousMetrics.ListItemCount}，替换前视觉={currentMetricsBefore.VisualCount}。 ");
                currentHost.LayoutUpdated -= layoutUpdated;
            };
            currentHost.LayoutUpdated += layoutUpdated;

            EventHandler? rendering = null;
            rendering = (_, _) =>
            {
                if (firstRenderLogged || !ReferenceEquals(currentHost.Content, nextPage)) return;
                firstRenderLogged = true;
                var metrics = CaptureVisualMetrics(currentHost);
                LogService.Info($"UI 观测：{pageName} 首次 Rendering，耗时 {transitionStopwatch.ElapsedMilliseconds}ms；新页视觉={metrics.VisualCount}，ListBox={metrics.ListBoxCount}，列表项={metrics.ListItemCount}，图片={metrics.ImageCount}。 ");
                CompositionTarget.Rendering -= rendering;
            };
            CompositionTarget.Rendering += rendering;

            _ = currentHost.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                if (!ReferenceEquals(currentHost.Content, nextPage))
                {
                    currentHost.LayoutUpdated -= layoutUpdated;
                    CompositionTarget.Rendering -= rendering;
                    return;
                }

                var metrics = CaptureVisualMetrics(currentHost);
                LogService.Info($"UI 性能：{pageName} 完成首帧布局后开始真实页面转场，等待耗时 {transitionStopwatch.ElapsedMilliseconds}ms；新页视觉={metrics.VisualCount}，ListBox={metrics.ListBoxCount}，列表项={metrics.ListItemCount}，图片={metrics.ImageCount}；旧页视觉={previousMetrics.VisualCount}，旧页列表项={previousMetrics.ListItemCount}。");
                var duration = TimeSpan.FromMilliseconds(200);
                var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
                currentHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, duration) { EasingFunction = easing });
                if (previousHost.Content is null) return;
                var fadeOut = new DoubleAnimation(0, duration) { EasingFunction = easing };
                fadeOut.Completed += (_, _) =>
                {
                    if (ReferenceEquals(currentHost.Content, nextPage)) previousHost.Content = null;
                };
                previousHost.BeginAnimation(OpacityProperty, fadeOut);
            }));
        }

        private static VisualMetrics CaptureVisualMetrics(DependencyObject root)
        {
            var metrics = new VisualMetrics();
            CountVisuals(root, metrics);
            return metrics;
        }

        private static void CountVisuals(DependencyObject current, VisualMetrics metrics)
        {
            if (current is null) return;
            metrics.VisualCount++;
            if (current is ListBox listBox)
            {
                metrics.ListBoxCount++;
                metrics.ListItemCount += listBox.Items.Count;
            }
            else if (current is Image)
            {
                metrics.ImageCount++;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = 0; index < childCount; index++) CountVisuals(VisualTreeHelper.GetChild(current, index), metrics);
        }

        private sealed class VisualMetrics
        {
            public int VisualCount { get; set; }
            public int ListBoxCount { get; set; }
            public int ListItemCount { get; set; }
            public int ImageCount { get; set; }
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
            if (msg == WM_NCHITTEST && hwnd != IntPtr.Zero && GetCursorPos(out var cursor) && GetWindowRect(hwnd, out var windowRect))
            {
                // 顶部标题栏覆盖了 WindowChrome 的默认命中区域时，仍保留 6px 原生拖拽调整边框。
                if (cursor.y >= windowRect.top && cursor.y < windowRect.top + 6)
                {
                    handled = true;
                    return new IntPtr(HTTOP);
                }
            }

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
            var window = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
            if (window != null)
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                maxInfo.ptMinTrackSize = new POINT
                {
                    x = (int)Math.Ceiling(window.MinWidth * dpi.DpiScaleX),
                    y = (int)Math.Ceiling(window.MinHeight * dpi.DpiScaleY)
                };
            }
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
                    if (shell.BottomBar.HasTemporaryEditor)
                    {
                        shell.CancelBottomBarEdit();
                    }
                    else if (shell.HasSelection)
                    {
                        shell.ClearTransientSelection();
                    }
                    else
                    {
                        shell.Navigate(WorkspaceMode.Home);
                    }
                    e.Handled = true;
                }
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}