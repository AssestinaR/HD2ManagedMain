using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics; // added for logging

using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;

namespace ManagedMain.Views
{
    public static partial class StatusDrawerHost
    {
        public static readonly DependencyProperty ModsSourceProperty = DependencyProperty.RegisterAttached(
            "ModsSource", typeof(IEnumerable), typeof(StatusDrawerHost), new PropertyMetadata(null, OnContextChanged));
        public static void SetModsSource(DependencyObject d, IEnumerable? value) => d.SetValue(ModsSourceProperty, value);
        public static IEnumerable? GetModsSource(DependencyObject d) => (IEnumerable?)d.GetValue(ModsSourceProperty);

        public static readonly DependencyProperty ProfileRootProperty = DependencyProperty.RegisterAttached(
            "ProfileRoot", typeof(string), typeof(StatusDrawerHost), new PropertyMetadata(string.Empty, OnContextChanged));
        public static void SetProfileRoot(DependencyObject d, string value) => d.SetValue(ProfileRootProperty, value);
        public static string GetProfileRoot(DependencyObject d) => (string)d.GetValue(ProfileRootProperty);

        public static readonly DependencyProperty GameFolderProperty = DependencyProperty.RegisterAttached(
            "GameFolder", typeof(string), typeof(StatusDrawerHost), new PropertyMetadata(string.Empty, OnContextChanged));
        public static void SetGameFolder(DependencyObject d, string value) => d.SetValue(GameFolderProperty, value);
        public static string GetGameFolder(DependencyObject d) => (string)d.GetValue(GameFolderProperty);

        public static readonly DependencyProperty ToggleButtonProperty = DependencyProperty.RegisterAttached(
            "ToggleButton", typeof(WpfButton), typeof(StatusDrawerHost), new PropertyMetadata(null, OnToggleButtonChanged));
        public static void SetToggleButton(DependencyObject d, WpfButton? value) => d.SetValue(ToggleButtonProperty, value);
        public static WpfButton? GetToggleButton(DependencyObject d) => (WpfButton?)d.GetValue(ToggleButtonProperty);

        // Responsive height properties
        public static readonly DependencyProperty HeightRatioProperty = DependencyProperty.RegisterAttached(
            "HeightRatio", typeof(double), typeof(StatusDrawerHost), new PropertyMetadata(0.6, OnContextChanged));
        public static void SetHeightRatio(DependencyObject d, double value) => d.SetValue(HeightRatioProperty, value);
        public static double GetHeightRatio(DependencyObject d) => (double)d.GetValue(HeightRatioProperty);

        public static readonly DependencyProperty MinHeightProperty = DependencyProperty.RegisterAttached(
            "MinHeight", typeof(double), typeof(StatusDrawerHost), new PropertyMetadata(240.0, OnContextChanged));
        public static void SetMinHeight(DependencyObject d, double value) => d.SetValue(MinHeightProperty, value);
        public static double GetMinHeight(DependencyObject d) => (double)d.GetValue(MinHeightProperty);

        public static readonly DependencyProperty EnableBreathingProperty = DependencyProperty.RegisterAttached(
            "EnableBreathing", typeof(bool), typeof(StatusDrawerHost), new PropertyMetadata(true));
        public static void SetEnableBreathing(DependencyObject d, bool value) => d.SetValue(EnableBreathingProperty, value);
        public static bool GetEnableBreathing(DependencyObject d) => (bool)d.GetValue(EnableBreathingProperty);

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "_State", typeof(State), typeof(StatusDrawerHost), new PropertyMetadata(null));
        private static void SetState(DependencyObject d, State? value) => d.SetValue(StateProperty, value);
        private static State? GetState(DependencyObject d) => (State?)d.GetValue(StateProperty);

        private class State
        {
            public System.Windows.FrameworkElement? HostElement;
            public System.Windows.Controls.Border? Drawer;
            public System.Windows.Media.TranslateTransform? Transform;
            public bool Open;
            public double DrawerHeight = 360;
            public System.Windows.Controls.DataGrid? Grid;
            public ObservableCollection<FileGroupStatus> Items = new();
            public ObservableCollection<FileGroupStatus> ViewItems = new();
            public WpfButton? Toggle;
            public System.Windows.Controls.TextBox? FilterBox;
            public System.Windows.Controls.CheckBox? OnlyIssuesBox;
            public CancellationTokenSource? ScanCts;
            public System.Windows.Controls.ProgressBar? LoadingBar;
        }

        private static void OnContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            TryBuild(d);
            UpdateHeights(d);
        }

        private static void OnToggleButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is System.Windows.FrameworkElement fe)
            {
                fe.Loaded -= Host_Loaded;
                fe.Loaded += Host_Loaded;
                fe.SizeChanged -= Host_SizeChanged;
                fe.SizeChanged += Host_SizeChanged;
            }
            TryBuild(d);
            var st = GetState(d); if (st == null) return;
            if (e.NewValue is WpfButton newBtn)
            {
                st.Toggle = newBtn;
                newBtn.Click += (s, args) => Toggle(d);
            }
        }

        private static void Host_Loaded(object? sender, RoutedEventArgs e)
        {
            if (sender is DependencyObject d)
            {
                TryBuild(d);
                UpdateHeights(d);
            }
        }
        private static void Host_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            if (sender is DependencyObject d) UpdateHeights(d);
        }

        public static void Toggle(DependencyObject host)
        {
            TryBuild(host);
            var st = GetState(host); if (st == null) return;
            if (st.Open) Close(host); else { Open(host); Refresh(host); }
        }

        public static void RefreshNow(DependencyObject host)
        {
            TryBuild(host);
            Refresh(host);
        }

        private static bool TryBuild(DependencyObject host)
        {
            var st = GetState(host); if (st == null) { st = new State(); SetState(host, st); }
            if (st.Drawer != null) return true;

            System.Windows.FrameworkElement? hostElement = FindRootGrid(host) ?? FindRootPanel(host);
            if (hostElement == null) return false;
            st.HostElement = hostElement;

            var drawer = new System.Windows.Controls.Border
            {
                CornerRadius = new System.Windows.CornerRadius(6),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(255, 220, 220, 220)),
                BorderThickness = new System.Windows.Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Visibility = System.Windows.Visibility.Collapsed,
                Focusable = true,
                Margin = new System.Windows.Thickness(5),
                RenderTransform = new System.Windows.Media.TranslateTransform()
            };
            st.Transform = (System.Windows.Media.TranslateTransform)drawer.RenderTransform;

            var dock = new System.Windows.Controls.DockPanel();
            // Header toolbar: Filter + Only Issues + Refresh + hint
            var header = new System.Windows.Controls.DockPanel { LastChildFill = false, Margin = new System.Windows.Thickness(4,2,4,2) };
            System.Windows.Controls.DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
            var filterBox = new System.Windows.Controls.TextBox { Width = 180, Margin = new System.Windows.Thickness(0,0,8,0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            filterBox.TextChanged += (_, __) => ApplyFilter(host);
            st.FilterBox = filterBox; header.Children.Add(filterBox);
            var onlyIssues = new System.Windows.Controls.CheckBox { Content = ManagedMain.Resources.Strings.SR_Status_OnlyIssues, Margin = new System.Windows.Thickness(0,0,8,0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            onlyIssues.Checked += (_, __) => ApplyFilter(host);
            onlyIssues.Unchecked += (_, __) => ApplyFilter(host);
            st.OnlyIssuesBox = onlyIssues; header.Children.Add(onlyIssues);
            var refresh = new System.Windows.Controls.Button { Content = ManagedMain.Resources.Strings.SR_Btn_Refresh, Padding = new System.Windows.Thickness(10,2,10,2), Margin = new System.Windows.Thickness(4,0,0,0) };
            refresh.Click += (_, __) => { Refresh(host); };
            header.Children.Add(refresh);
            // Loading indicator (indeterminate ProgressBar)
            var loading = new System.Windows.Controls.ProgressBar { IsIndeterminate = true, Width = 120, Height = 14, Visibility = System.Windows.Visibility.Collapsed, Margin = new System.Windows.Thickness(12,0,0,0), VerticalAlignment = System.Windows.VerticalAlignment.Center };
            st.LoadingBar = loading; header.Children.Add(loading);
            header.Children.Add(new System.Windows.Controls.TextBlock { Text = ManagedMain.Resources.Strings.SR_Tip_DoubleClickCopyPath, Foreground = System.Windows.Media.Brushes.Gray, Margin = new System.Windows.Thickness(20,0,0,0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
            dock.Children.Add(header);
            dock.Children.Add(new System.Windows.Controls.Separator { Height = 1, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(51, 46, 42, 50)) });

            var grid = new System.Windows.Controls.DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                HeadersVisibility = System.Windows.Controls.DataGridHeadersVisibility.Column,
                Margin = new System.Windows.Thickness(0,8,0,0)
            };
            // Row coloring and tooltip
            var rowStyle = new System.Windows.Style(typeof(System.Windows.Controls.DataGridRow));
            rowStyle.Setters.Add(new System.Windows.Setter(System.Windows.FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding("Tooltip")));
            // Order triggers from low to high priority so later ones override earlier ones
            var trigDup = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsDuplicate"), Value = true }; // brown
            trigDup.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEA, 0xD1, 0xA3))));
            rowStyle.Triggers.Add(trigDup);
            var trigMisMatch = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsPatchNMismatch"), Value = true }; // magenta
            trigMisMatch.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD5, 0xF3))));
            rowStyle.Triggers.Add(trigMisMatch);
            var trigSeq = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsSequenceGap"), Value = true }; // purple
            trigSeq.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xD5, 0xFF))));
            rowStyle.Triggers.Add(trigSeq);
            var trigIncomplete = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsIncomplete"), Value = true }; // yellow
            trigIncomplete.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF6, 0xD5))));
            rowStyle.Triggers.Add(trigIncomplete);
            var trigMissing = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsMissing"), Value = true }; // red
            trigMissing.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE5, 0xE5))));
            rowStyle.Triggers.Add(trigMissing);
            var trigExtra = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsExtra"), Value = true }; // blue
            trigExtra.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xD5, 0xE7, 0xFF))));
            rowStyle.Triggers.Add(trigExtra);
            grid.RowStyle = rowStyle;

            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Owner, Binding = new System.Windows.Data.Binding("OwnerDisplay"), Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Hex, Binding = new System.Windows.Data.Binding("HexPrefix"), Width = new System.Windows.Controls.DataGridLength(130) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_ModList, Binding = new System.Windows.Data.Binding("PatchN_ModList"), Width = new System.Windows.Controls.DataGridLength(70) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Game, Binding = new System.Windows.Data.Binding("PatchN_Game"), Width = new System.Windows.Controls.DataGridLength(70) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Exists, Binding = new System.Windows.Data.Binding("ExistsInGame"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_AllLinked, Binding = new System.Windows.Data.Binding("FilesAllLinked"), Width = new System.Windows.Controls.DataGridLength(50) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Count, Binding = new System.Windows.Data.Binding("FileCount"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Link, Binding = new System.Windows.Data.Binding("LinkType"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_GameFile, Binding = new System.Windows.Data.Binding("GameFileName"), Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.MouseDoubleClick += (s, e) => OnGridDoubleClick(host);

            grid.ItemsSource = st.ViewItems;
            st.Grid = grid;
            dock.Children.Add(grid);
            drawer.Child = dock;

            if (hostElement is System.Windows.Controls.Grid g)
            {
                int rows = Math.Max(1, g.RowDefinitions.Count);
                int cols = Math.Max(1, g.ColumnDefinitions.Count);
                System.Windows.Controls.Grid.SetRow(drawer, 0); System.Windows.Controls.Grid.SetRowSpan(drawer, rows);
                System.Windows.Controls.Grid.SetColumn(drawer, 0); System.Windows.Controls.Grid.SetColumnSpan(drawer, cols);
                g.Children.Add(drawer);
            }
            else if (hostElement is WpfPanel p)
            {
                p.Children.Add(drawer);
            }
            System.Windows.Controls.Panel.SetZIndex(drawer, 2000);

            st.Drawer = drawer;
            UpdateHeights(host);
            st.Transform.Y = st.DrawerHeight;
            return true;
        }

        private static void UpdateHeights(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return;
            var fe = st.HostElement; if (fe == null) return;
            var ratio = Math.Max(0.1, Math.Min(1.0, GetHeightRatio(host)));
            var min = Math.Max(0.0, GetMinHeight(host));

            double avail = GetAvailableHeight(fe);
            double desired = Math.Max(min, avail * ratio);
            st.DrawerHeight = desired;
            if (st.Grid != null)
            {
                st.Grid.Height = Math.Max(60, desired - 48);
            }
            if (!st.Open && st.Transform != null)
            {
                st.Transform.Y = desired;
            }
        }

        private static double GetAvailableHeight(System.Windows.FrameworkElement fe)
        {
            if (fe is System.Windows.Controls.Grid g)
            {
                return g.ActualHeight;
            }
            var q = new Queue<DependencyObject>(); q.Enqueue(fe);
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (cur is System.Windows.Controls.Grid grid && grid.RowDefinitions.Count >= 1)
                {
                    return grid.ActualHeight;
                }
                int c = System.Windows.Media.VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < c; i++) q.Enqueue(System.Windows.Media.VisualTreeHelper.GetChild(cur, i));
            }
            return fe.ActualHeight;
        }

        private static void Open(DependencyObject host)
        {
            TryBuild(host);
            UpdateHeights(host);
            var st = GetState(host); if (st == null || st.Drawer == null || st.Transform == null) return;
            if (st.Open) return;
            st.Open = true; st.Drawer.Visibility = System.Windows.Visibility.Visible; st.Drawer.Focus();
            var anim = new System.Windows.Media.Animation.DoubleAnimation { From = st.DrawerHeight, To = 0, Duration = System.TimeSpan.FromMilliseconds(220), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            st.Transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
            StartBreathing(st);
        }

        private static void Close(DependencyObject host)
        {
            TryBuild(host);
            UpdateHeights(host);
            var st = GetState(host); if (st == null || st.Drawer == null || st.Transform == null) return;
            if (!st.Open) return; st.Open = false;
            var anim = new System.Windows.Media.Animation.DoubleAnimation { From = 0, To = st.DrawerHeight, Duration = System.TimeSpan.FromMilliseconds(180), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseIn } };
            anim.Completed += (_, __) => { if (!st.Open) st.Drawer.Visibility = System.Windows.Visibility.Collapsed; };
            st.Transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
            StopBreathing(st);
        }

        private static System.Windows.FrameworkElement? FindRootGrid(DependencyObject root)
        {
            if (root is System.Windows.Controls.Grid gg) return gg;
            int c = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < c; i++)
            {
                var ch = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (ch is System.Windows.Controls.ToolBar) continue;
                var g = FindRootGrid(ch); if (g != null) return g;
            }
            return null;
        }
        private static System.Windows.FrameworkElement? FindRootPanel(DependencyObject root)
        {
            if (root is WpfPanel p) return p;
            int c = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < c; i++)
            {
                var ch = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (ch is System.Windows.Controls.ToolBar) continue;
                var g = FindRootPanel(ch); if (g != null) return g;
            }
            return null;
        }

        private static void Refresh(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return;
            // Cancel previous scan if any
            st.ScanCts?.Cancel();
            st.ScanCts?.Dispose();
            var cts = new CancellationTokenSource();
            st.ScanCts = cts;
            var token = cts.Token;

            string profileRoot = GetProfileRoot(host);
            string gameFolder = GetGameFolder(host);
            try
            {
                // Always prefer the latest saved value from OptionStore
                var opt = new ManagedMain.Services.OptionStore().LoadOrCreate();
                var latest = opt.GameFolder;
                if (!string.IsNullOrWhiteSpace(latest) && !string.Equals(latest, gameFolder, StringComparison.OrdinalIgnoreCase))
                {
                    gameFolder = latest;
                    // keep attached property in sync so UI reflects latest
                    try { SetGameFolder(host, latest); } catch { }
                }
                // If attached property empty, still use latest
                if (string.IsNullOrWhiteSpace(gameFolder)) gameFolder = latest;
            }
            catch { }
             var modsEnum = GetModsSource(host) ?? TryGetModsFromDataContext(host);
            if (modsEnum == null)
            {
                // Clear view and hide loading
                if (st.Grid != null)
                {
                    st.Grid.Dispatcher?.Invoke(() => { if (st.LoadingBar != null) st.LoadingBar.Visibility = System.Windows.Visibility.Collapsed; });
                    var empty = new ObservableCollection<FileGroupStatus>();
                    st.Items = empty; st.ViewItems = empty; st.Grid.ItemsSource = st.ViewItems;
                }
                return;
            }
            // Snapshot mods to avoid concurrent changes during scan
            var mods = modsEnum.Cast<object>().ToList();

            // Optional immediate feedback: clear view once and show loading
            if (st.Grid != null)
            {
                var dispatcher = st.Grid.Dispatcher;
                dispatcher?.Invoke(() => { if (st.LoadingBar != null) st.LoadingBar.Visibility = System.Windows.Visibility.Visible; });
                var empty = new ObservableCollection<FileGroupStatus>();
                st.Items = empty; st.ViewItems = empty; st.Grid.ItemsSource = st.ViewItems;
            }

            Task.Run(() =>
            {
                try
                {
                    var list = new List<FileGroupStatus>();
                    foreach (var item in StatusScanner.Scan(profileRoot, gameFolder, mods))
                    {
                        if (token.IsCancellationRequested) return;
                        list.Add(item);
                    }
                    if (token.IsCancellationRequested) return;

                    // Apply on UI thread in bulk
                    var dispatcher = st.Grid?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                    dispatcher?.Invoke(() =>
                    {
                        if (token.IsCancellationRequested) return;
                        st.Items = new ObservableCollection<FileGroupStatus>(list);
                        ApplyFilter(host); // will rebuild st.ViewItems and set ItemsSource
                        if (st.LoadingBar != null) st.LoadingBar.Visibility = System.Windows.Visibility.Collapsed;
                    });
                }
                catch (Exception ex)
                {
                    var dispatcher = st.Grid?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                    dispatcher?.Invoke(() => { if (st.LoadingBar != null) st.LoadingBar.Visibility = System.Windows.Visibility.Collapsed; });
                    Debug.WriteLine($"[StatusDrawerHost] …®√ËÀ¢–¬ ß∞‹: {ex.Message}");
                }
            }, token);
        }

        private static void ApplyFilter(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return;
            void Apply()
            {
                var text = st.FilterBox?.Text?.Trim() ?? string.Empty;
                bool onlyIssues = st.OnlyIssuesBox?.IsChecked == true;
                IEnumerable<FileGroupStatus> baseList = st.Items ?? new ObservableCollection<FileGroupStatus>();
                var filtered = baseList.Where(it =>
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        bool hit = (it.HexPrefix?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                   (it.OwnerDisplay?.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
                        if (!hit) return false;
                    }
                    if (onlyIssues)
                    {
                        bool normal = it.ExistsInGame && !it.IsPatchNMismatch && it.FilesAllLinked && !it.IsMissing && !it.IsIncomplete && !it.IsDuplicate && !it.IsExtra && !it.IsSequenceGap;
                        if (normal) return false;
                    }
                    return true;
                }).ToList();

                st.ViewItems = new ObservableCollection<FileGroupStatus>(filtered);
                if (st.Grid != null) st.Grid.ItemsSource = st.ViewItems;
            }

            var dispatcher = st.Grid?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess()) dispatcher.Invoke(Apply); else Apply();
        }

        private static void OnGridDoubleClick(DependencyObject host)
        {
            var st = GetState(host); if (st?.Grid?.SelectedItem is FileGroupStatus it && !string.IsNullOrEmpty(it.GameFileName))
            {
                try
                {
                    string gameFolder = GetGameFolder(host);
                    if (string.IsNullOrWhiteSpace(gameFolder))
                    {
                        try { var opt = new ManagedMain.Services.OptionStore().LoadOrCreate(); gameFolder = opt.GameFolder; } catch { }
                    }
                    var full = System.IO.Path.Combine(gameFolder ?? string.Empty, it.GameFileName);
                    if (File.Exists(full)) { System.Windows.Clipboard.SetText(full); System.Windows.MessageBox.Show(string.Format(ManagedMain.Resources.Strings.SR_Msg_CopiedPath, full)); }
                }
                catch { }
            }
        }

        private static IEnumerable? TryGetModsFromDataContext(DependencyObject host)
        {
            if (host is System.Windows.FrameworkElement fe && fe.DataContext != null)
            {
                try
                {
                    var prop = fe.DataContext.GetType().GetProperty("Mods");
                    return prop?.GetValue(fe.DataContext) as IEnumerable;
                }
                catch { }
            }
            return null;
        }

        public class FileGroupStatus
        {
            public string OwnerDisplay { get; set; } = string.Empty;
            public string HexPrefix { get; set; } = string.Empty;
            public int PatchN_ModList { get; set; }
            public int PatchN_Game { get; set; } = -1;
            public string GameFileName { get; set; } = string.Empty;
            public bool ExistsInGame { get; set; }
            public bool FilesAllLinked { get; set; }
            public int FileCount { get; set; }
            public string LinkType { get; set; } = string.Empty;
            public string Tooltip { get; set; } = string.Empty;
            // New flags for extended statuses
            public bool IsMissing { get; set; }
            public bool IsIncomplete { get; set; }
            public bool IsSequenceGap { get; set; }
            public bool IsDuplicate { get; set; }
            public bool IsExtra { get; set; }
            public bool IsPatchNMismatch { get; set; }
        }

        private static class StatusScanner
        {
            private static readonly System.Text.RegularExpressions.Regex PatchRegex = new("^([a-fA-F0-9]{16})\\.patch_(\\d+)(?:\\.stream|\\.gpu_resources)?$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            private sealed class GameEntry
            {
                public string Hex = string.Empty;
                public int N;
                public string FullPath = string.Empty;
                public string FileName = string.Empty;
                public long Length;
                public long WriteTicks;
                public string Tail = string.Empty;
                public string LinkType = string.Empty;
                public bool Used;
                public string Sha256 = string.Empty; // renamed from Md5
            }

            private sealed class ExpectedEntry
            {
                public string Tail = string.Empty;
                public long Length;
                public string AbsPath = string.Empty;
                public string Sha256 = string.Empty; // renamed from Md5
            }

            public static IEnumerable<FileGroupStatus> Scan(string profileRoot, string gameFolder, IEnumerable mods)
            {
                if (string.IsNullOrWhiteSpace(profileRoot) || string.IsNullOrWhiteSpace(gameFolder)) yield break;

                var gameMap = new Dictionary<string, Dictionary<string, List<GameEntry>>>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.EnumerateFiles(gameFolder, "*.patch_*", SearchOption.TopDirectoryOnly))
                {
                    var name = Path.GetFileName(file)!;
                    var m = PatchRegex.Match(name);
                    if (!m.Success) continue;
                    string hex = m.Groups[1].Value;
                    int n = int.TryParse(m.Groups[2].Value, out var pn) ? pn : -1;
                    string tail = ExtractTail(name);
                    long len = 0; long wt = 0; string linkType = "Unknown";
                    try
                    {
                        using var fs = File.OpenRead(file); len = fs.Length; wt = File.GetLastWriteTimeUtc(file).Ticks;
                        var attr = File.GetAttributes(file);
                        linkType = (attr & FileAttributes.ReparsePoint) != 0 ? "Sym" : "Hard/Copy";
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[StatusScanner] ∂¡»°”Œœ∑Œƒº˛ Ù–‘ ß∞‹ {file}: {ex.Message}");
                    }
                    if (!gameMap.TryGetValue(hex, out var tailMap)) { tailMap = new Dictionary<string, List<GameEntry>>(StringComparer.OrdinalIgnoreCase); gameMap[hex] = tailMap; }
                    if (!tailMap.TryGetValue(tail, out var list)) { list = new List<GameEntry>(); tailMap[tail] = list; }
                    list.Add(new GameEntry { Hex = hex, N = n, FullPath = file, FileName = name, Length = len, WriteTicks = wt, Tail = tail, LinkType = linkType, Used = false });
                }

                var gapStart = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in gameMap)
                {
                    var ns = kv.Value.Values.SelectMany(v => v.Select(e => e.N)).Where(n => n >= 0).Distinct().OrderBy(n => n).ToList();
                    int expected = 0; int missingAt = -1;
                    foreach (var n in ns)
                    {
                        if (n != expected) { missingAt = expected; break; }
                        expected++;
                    }
                    if (missingAt < 0 && ns.Count > 0 && ns.Last() == ns.Count - 1) missingAt = -1; // contiguous
                    gapStart[kv.Key] = missingAt;
                }

                // sha256 cache (replaces MD5)
                var sha256Cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string HashOf(string path)
                {
                    try
                    {
                        try
                        {
                            var attr = File.GetAttributes(path);
                            if ((attr & FileAttributes.ReparsePoint) != 0)
                            {
                                // reparse point: fall through, still hash target content
                            }
                        }
                        catch (Exception exAttr)
                        {
                            Debug.WriteLine($"[StatusScanner] ∂¡»°Œƒº˛ Ù–‘ ß∞‹(π˛œ£«∞) {path}: {exAttr.Message}");
                        }

                        var finfo = new FileInfo(path);
                        string key = path + "|" + finfo.Length + "|" + finfo.LastWriteTimeUtc.Ticks;
                        if (sha256Cache.TryGetValue(key, out var v)) return v;
                        using var fs = File.OpenRead(path);
                        var hash = SHA256.HashData(fs);
                        var hex = BitConverter.ToString(hash).Replace("-", string.Empty);
                        sha256Cache[key] = hex; return hex;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[StatusScanner] º∆À„π˛œ£ ß∞‹ {path}: {ex.Message}");
                        return string.Empty;
                    }
                }

                var matchedGameFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var m in mods)
                {
                    if (m is not ManagedMain.Models.MainModItem main) continue;
                    if (!IsEnabled(main)) continue; // skip disabled

                    IEnumerable<FileGroupStatus> EmitForGroups(IEnumerable<ManagedMain.Models.ModFileGroup> groups, string ownerPrefix)
                    {
                        foreach (var g in groups)
                        {
                            var owner = ownerPrefix;
                            int expectedCount = g.Files?.Count ?? 0;
                            int matchedCount = 0;
                            var matchedNs = new List<int>();
                            string firstMatchName = string.Empty;
                            string linkType = string.Empty;
                            bool isDuplicate = false;

                            var expected = new List<ExpectedEntry>();
                            if (g.Files != null)
                            {
                                foreach (var rel in g.Files)
                                {
                                    var abs = Path.Combine(profileRoot, main.Name, rel.Replace('/', Path.DirectorySeparatorChar));
                                    var ee = new ExpectedEntry { Tail = ExtractTail(Path.GetFileName(rel) ?? string.Empty), AbsPath = abs };
                                    try { using var fs = File.OpenRead(abs); ee.Length = fs.Length; } catch (Exception exLen) { Debug.WriteLine($"[StatusScanner] ∂¡»°Œƒº˛≥§∂» ß∞‹ {abs}: {exLen.Message}"); ee.Length = 0; }
                                    expected.Add(ee);
                                }
                            }

                            if (gameMap.TryGetValue(g.HexPrefix, out var tailMap))
                            {
                                foreach (var exp in expected)
                                {
                                    if (!tailMap.TryGetValue(exp.Tail, out var candidates) || candidates.Count == 0) continue;
                                    var sizeMatches = candidates.Where(c => !c.Used && c.Length == exp.Length).ToList();
                                    if (sizeMatches.Count == 0) continue;
                                    GameEntry? chosen = null;
                                    if (sizeMatches.Count == 1)
                                    {
                                        chosen = sizeMatches[0];
                                    }
                                    else
                                    {
                                        if (string.IsNullOrEmpty(exp.Sha256)) exp.Sha256 = HashOf(exp.AbsPath);
                                        var hashMatches = sizeMatches.Where(c =>
                                        {
                                            if (string.IsNullOrEmpty(c.Sha256)) c.Sha256 = HashOf(c.FullPath);
                                            return !string.IsNullOrEmpty(exp.Sha256) && !string.IsNullOrEmpty(c.Sha256) &&
                                                   string.Equals(c.Sha256, exp.Sha256, StringComparison.OrdinalIgnoreCase);
                                        }).ToList();
                                        if (hashMatches.Count >= 1)
                                        {
                                            var exact = hashMatches.FirstOrDefault(c => c.N == g.PatchN);
                                            chosen = exact ?? hashMatches.OrderBy(c => Math.Abs(c.N - g.PatchN)).First();
                                            isDuplicate = hashMatches.Count(c => c.N == (exact?.N ?? chosen.N)) > 1;
                                        }
                                        else
                                        {
                                            chosen = sizeMatches.OrderBy(c => Math.Abs(c.N - g.PatchN)).First();
                                        }
                                    }
                                    if (chosen != null)
                                    {
                                        chosen.Used = true;
                                        matchedGameFiles.Add(chosen.FullPath);
                                        matchedCount++;
                                        if (string.IsNullOrEmpty(firstMatchName)) { firstMatchName = chosen.FileName; linkType = chosen.LinkType; }
                                        if (chosen.N >= 0) matchedNs.Add(chosen.N);
                                    }
                                }
                            }

                            int matchedNForGroup = matchedNs.GroupBy(n => n).OrderByDescending(gp => gp.Count()).ThenBy(gp => gp.Key).Select(gp => gp.Key).FirstOrDefault(-1);

                            bool isMissing = matchedCount == 0;
                            bool isIncomplete = !isMissing && matchedCount < expectedCount;
                            bool isMismatch = matchedNForGroup >= 0 && matchedNForGroup != g.PatchN;
                            bool isSeqGap = false;
                            if (matchedNForGroup >= 0 && gapStart.TryGetValue(g.HexPrefix, out var gs) && gs >= 0)
                            {
                                if (matchedNForGroup > gs) isSeqGap = true;
                            }

                            var tips = new List<string>();
                            if (isMissing) tips.Add(ManagedMain.Resources.Strings.SR_Status_Missing);
                            if (isIncomplete) tips.Add(ManagedMain.Resources.Strings.SR_Status_Incomplete);
                            if (isSeqGap) tips.Add(ManagedMain.Resources.Strings.SR_Status_SeqGap);
                            if (isDuplicate) tips.Add(ManagedMain.Resources.Strings.SR_Status_Duplicate);
                            if (isMismatch) tips.Add(ManagedMain.Resources.Strings.SR_Status_PatchMismatch);
                            if (tips.Count == 0) tips.Add(ManagedMain.Resources.Strings.SR_Status_Normal);
                            var tooltipText = string.Join("\n", tips);

                            yield return new FileGroupStatus
                            {
                                OwnerDisplay = owner,
                                HexPrefix = g.HexPrefix,
                                PatchN_ModList = g.PatchN,
                                PatchN_Game = matchedNForGroup,
                                GameFileName = firstMatchName,
                                ExistsInGame = matchedCount == expectedCount,
                                FilesAllLinked = matchedCount == expectedCount,
                                FileCount = expectedCount,
                                LinkType = linkType,
                                Tooltip = tooltipText,
                                IsMissing = isMissing,
                                IsIncomplete = isIncomplete,
                                IsDuplicate = isDuplicate,
                                IsSequenceGap = isSeqGap,
                                IsPatchNMismatch = isMismatch,
                                IsExtra = false
                            };
                        }
                    }

                    foreach (var stItem in EmitForGroups(main.FileGroups, main.Name)) yield return stItem;
                    foreach (var o in main.Options)
                    {
                        if (!IsEnabled(o)) continue;
                        foreach (var stItem in EmitForGroups(o.FileGroups, main.Name + "/" + o.Name)) yield return stItem;
                        foreach (var s in o.SubOptions)
                        {
                            if (!IsEnabled(s)) continue;
                            foreach (var stItem in EmitForGroups(s.FileGroups, main.Name + "/" + o.Name + "/" + s.Name)) yield return stItem;
                        }
                    }
                }

                foreach (var kv in gameMap)
                {
                    foreach (var tailKv in kv.Value)
                    {
                        foreach (var e in tailKv.Value)
                        {
                            if (!e.Used)
                            {
                                yield return new FileGroupStatus
                                {
                                    OwnerDisplay = string.Empty,
                                    HexPrefix = e.Hex,
                                    PatchN_ModList = -1,
                                    PatchN_Game = e.N,
                                    GameFileName = e.FileName,
                                    ExistsInGame = true,
                                    FilesAllLinked = true,
                                    FileCount = 1,
                                    LinkType = e.LinkType,
                                    Tooltip = ManagedMain.Resources.Strings.SR_Status_ExtraTip,
                                    IsMissing = false,
                                    IsIncomplete = false,
                                    IsDuplicate = false,
                                    IsSequenceGap = false,
                                    IsPatchNMismatch = false,
                                    IsExtra = true
                                };
                            }
                        }
                    }
                }
            }

            private static string ExtractTail(string fileName)
            {
                if (fileName.EndsWith(".gpu_resources", StringComparison.OrdinalIgnoreCase)) return ".gpu_resources";
                if (fileName.EndsWith(".stream", StringComparison.OrdinalIgnoreCase)) return ".stream";
                return string.Empty;
            }

            private static bool IsEnabled(object o)
            {
                try
                {
                    var prop = o.GetType().GetProperty("Enabled");
                    if (prop == null) return false;
                    var v = prop.GetValue(o);
                    if (v is int i) return i != 0; // treat partial as enabled
                    if (v is bool b) return b;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[StatusScanner] ??? Enabled ???????: {ex.Message}");
                }
                return false;
            }
        }

        private static void StartBreathing(State st)
        {
            try
            {
                if (st.Toggle is null) return; if (!GetEnableBreathing(st.Toggle)) return;
                var weak = (System.Windows.Application.Current?.Resources["ButtonAccentWeakBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SkyBlue;
                var accent = (System.Windows.Application.Current?.Resources["ButtonAccentBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.DodgerBlue;
                var borderMuted = (System.Windows.Application.Current?.Resources["ButtonAccentMutedBrush"] as System.Windows.Media.SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SteelBlue;
                var bg = new System.Windows.Media.SolidColorBrush(weak); var bb = new System.Windows.Media.SolidColorBrush(borderMuted); st.Toggle.Background = bg; st.Toggle.BorderBrush = bb;
                var ease = new System.Windows.Media.Animation.SineEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut };
                var bgAnim = new System.Windows.Media.Animation.ColorAnimation { From = weak, To = accent, Duration = System.TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = ease };
                var bdAnim = new System.Windows.Media.Animation.ColorAnimation { From = borderMuted, To = accent, Duration = System.TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever, EasingFunction = ease };
                bg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, bgAnim); bb.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, bdAnim);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusDrawerHost] StartBreathing  ß∞‹: {ex.Message}");
            }
        }
        private static void StopBreathing(State st)
        {
            try
            {
                if (st.Toggle is null) return;
                if (st.Toggle.Background is System.Windows.Media.SolidColorBrush bg) bg.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
                if (st.Toggle.BorderBrush is System.Windows.Media.SolidColorBrush bb) bb.BeginAnimation(System.Windows.Media.SolidColorBrush.ColorProperty, null);
                st.Toggle.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                st.Toggle.ClearValue(System.Windows.Controls.Button.BorderBrushProperty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusDrawerHost] StopBreathing  ß∞‹: {ex.Message}");
            }
        }
    }
}
