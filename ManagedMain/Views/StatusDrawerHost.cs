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
            public System.Windows.Controls.TextBlock? SummaryText;
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
            // Summary line at top
            var summary = new System.Windows.Controls.TextBlock
             {
                 Margin = new System.Windows.Thickness(4, 2, 4, 2),
                 Foreground = System.Windows.Application.Current?.Resources["TextSecondaryBrush"] as System.Windows.Media.Brush
                              ?? System.Windows.Media.Brushes.Gray,
                 TextTrimming = System.Windows.TextTrimming.CharacterEllipsis,
                Text = ManagedMain.Resources.Strings.SR_Status_Summary_Default
             };
            st.SummaryText = summary;
            System.Windows.Controls.DockPanel.SetDock(summary, System.Windows.Controls.Dock.Top);
            dock.Children.Add(summary);
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
            header.Children.Add(new System.Windows.Controls.TextBlock { Text = ManagedMain.Resources.Strings.SR_Tip_StatusDoubleClick, Foreground = System.Windows.Media.Brushes.Gray, Margin = new System.Windows.Thickness(20,0,0,0), VerticalAlignment = System.Windows.VerticalAlignment.Center });
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
            // Order triggers from low to high severity so later ones override earlier ones
            // Info: Extra
            var trigExtra = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsExtra"), Value = true }; // light blue (#E3F2FD)
            trigExtra.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE3, 0xF2, 0xFD))));
            rowStyle.Triggers.Add(trigExtra);
            // Caution: Sequence gap
            var trigSeq = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsSequenceGap"), Value = true }; // light yellow (#FFF9C4)
            trigSeq.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xF9, 0xC4))));
            rowStyle.Triggers.Add(trigSeq);
            // Warning: Duplicate
            var trigDup = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsDuplicate"), Value = true }; // amber (#FFECB3)
            trigDup.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xEC, 0xB3))));
            rowStyle.Triggers.Add(trigDup);
            // Error: Patch N mismatch
            var trigMisMatch = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsPatchNMismatch"), Value = true }; // orange (#FFE0B2)
            trigMisMatch.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE0, 0xB2))));
            rowStyle.Triggers.Add(trigMisMatch);
            // Critical: Missing
            var trigMissing = new System.Windows.DataTrigger { Binding = new System.Windows.Data.Binding("IsMissing"), Value = true }; // red (#FFCDD2)
            trigMissing.Setters.Add(new System.Windows.Setter(System.Windows.Controls.DataGridRow.BackgroundProperty, new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xCD, 0xD2))));
            rowStyle.Triggers.Add(trigMissing);
            grid.RowStyle = rowStyle;

            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Owner, Binding = new System.Windows.Data.Binding("OwnerDisplay"), Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Auto) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Hex, Binding = new System.Windows.Data.Binding("HexPrefix"), Width = new System.Windows.Controls.DataGridLength(130) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_ModList, Binding = new System.Windows.Data.Binding("PatchN_ModList"), Width = new System.Windows.Controls.DataGridLength(70) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Game, Binding = new System.Windows.Data.Binding("PatchN_Game"), Width = new System.Windows.Controls.DataGridLength(70) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Exists, Binding = new System.Windows.Data.Binding("ExistsInGame"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Count, Binding = new System.Windows.Data.Binding("FileCount"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_Link, Binding = new System.Windows.Data.Binding("LinkType"), Width = new System.Windows.Controls.DataGridLength(60) });
            grid.Columns.Add(new System.Windows.Controls.DataGridTextColumn { Header = ManagedMain.Resources.Strings.SR_Col_GameFile, Binding = new System.Windows.Data.Binding("GameFileName"), Width = new System.Windows.Controls.DataGridLength(1, System.Windows.Controls.DataGridLengthUnitType.Star) });
            grid.MouseDoubleClick += (s, e) => { if (e.ChangedButton == System.Windows.Input.MouseButton.Left) { OnGridDoubleClick(host); e.Handled = true; } };
            grid.MouseRightButtonDown += (s, e) => { if (e.ClickCount == 2 && e.ChangedButton == System.Windows.Input.MouseButton.Right) { OnGridRightDoubleClick(host); e.Handled = true; } };

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
            BringToFront(st);
            st.Open = true; st.Drawer.Visibility = System.Windows.Visibility.Visible; st.Drawer.Focus();
            var anim = new System.Windows.Media.Animation.DoubleAnimation { From = st.DrawerHeight, To = 0, Duration = System.TimeSpan.FromMilliseconds(220), EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
            st.Transform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, anim);
            StartBreathing(st);
        }

        private static void BringToFront(State st)
        {
            try
            {
                if (st.Drawer == null) return;
                if (st.HostElement is System.Windows.Controls.Panel panel)
                {
                    int max = 0;
                    foreach (System.Windows.UIElement child in panel.Children)
                    {
                        max = Math.Max(max, System.Windows.Controls.Panel.GetZIndex(child));
                    }
                    System.Windows.Controls.Panel.SetZIndex(st.Drawer, max + 1);
                }
                else
                {
                    System.Windows.Controls.Panel.SetZIndex(st.Drawer, 3000);
                }
            }
            catch { }
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
                        UpdateSummary(host, mods, st.Items);
                    });
                }
                catch (Exception ex)
                {
                    var dispatcher = st.Grid?.Dispatcher ?? System.Windows.Application.Current?.Dispatcher;
                    dispatcher?.Invoke(() => { if (st.LoadingBar != null) st.LoadingBar.Visibility = System.Windows.Visibility.Collapsed; });
                    Debug.WriteLine($"[StatusDrawerHost] ?????????: {ex.Message}");
                }
            }, token);
        }

        private static void UpdateSummary(DependencyObject host, IList<object> mods, ObservableCollection<FileGroupStatus> items)
        {
            var st = GetState(host); if (st?.SummaryText == null) return;
            try
            {
                // enabled mods (main)
                int enabledMods = mods.OfType<ManagedMain.Models.MainModItem>().Count(mm => IsNodeEnabled(mm));
                 // groups excluding extras
                var groups = items.Where(i => !i.IsExtra).ToList();
                int totalGroups = groups.Count;
                int normal = groups.Count(it => it.ExistsInGame && it.FilesAllLinked && !it.IsMissing && !it.IsSequenceGap && !it.IsDuplicate && !it.IsPatchNMismatch);
                int abnormal = totalGroups - normal;
                st.SummaryText.Text = string.Format(ManagedMain.Resources.Strings.SR_Status_Summary_Format, enabledMods, totalGroups, normal, abnormal);
            }
            catch (Exception ex)
            {
                st.SummaryText.Text = ManagedMain.Resources.Strings.SR_Status_Summary_Default;
            }
        }

        private static bool IsNodeEnabled(object o)
        {
            try
            {
                var prop = o.GetType().GetProperty("Enabled");
                if (prop == null) return false;
                var v = prop.GetValue(o);
                if (v is int i) return i != 0;
                if (v is bool b) return b;
            }
            catch { }
            return false;
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
                        bool normal = it.ExistsInGame && !it.IsPatchNMismatch && it.FilesAllLinked && !it.IsMissing && !it.IsDuplicate && !it.IsExtra && !it.IsSequenceGap;
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
            var st = GetState(host); if (st?.Grid?.SelectedItem is not FileGroupStatus it) return;
            try
            {
                // Left double-click: open mod folder for the group
                if (!string.IsNullOrWhiteSpace(it.OwnerDisplay))
                {
                    string profileRoot = GetProfileRoot(host);
                    var parts = it.OwnerDisplay.Split('/');
                    string modName = parts.Length > 0 ? parts[0] : it.OwnerDisplay;
                    string path = System.IO.Path.Combine(profileRoot ?? string.Empty, modName);
                    if (Directory.Exists(path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private static void OnGridRightDoubleClick(DependencyObject host)
        {
            if (System.Windows.Input.Mouse.RightButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var st = GetState(host); if (st?.Grid?.SelectedItem is not FileGroupStatus it) return;
            try
            {
                string gameFolder = GetGameFolder(host);
                if (string.IsNullOrWhiteSpace(gameFolder))
                {
                    try { var opt = new ManagedMain.Services.OptionStore().LoadOrCreate(); gameFolder = opt.GameFolder; } catch { }
                }
                if (!string.IsNullOrWhiteSpace(gameFolder) && it.GameFiles != null && it.GameFiles.Length > 0)
                {
                    var targets = it.GameFiles.Select(f => System.IO.Path.Combine(gameFolder!, f)).Where(File.Exists).ToArray();
                    if (targets.Length > 0)
                    {
                        System.Windows.Clipboard.SetText(string.Join("\r\n", targets));
                        string args = "/select,\"" + targets[0] + "\"";
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
                    }
                }
            }
            catch { }
        }

        private static void OpenFolderAndSelect(string[] fullPaths)
        {
            if (fullPaths.Length == 0) return;
            string firstPath = fullPaths[0];
            // Clone file path and remove file name
            string folderPath = System.IO.Path.GetDirectoryName(firstPath);
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            // Open Explorer and select the first file
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{firstPath}\"") { UseShellExecute = true });

            // Copy all file paths to clipboard
            try
            {
                System.Windows.Clipboard.SetText(string.Join(System.Environment.NewLine, fullPaths));
            }
            catch { }
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

        // Scanner moved to StatusDrawerHost.Scanner.cs (partial class)
    }
}
