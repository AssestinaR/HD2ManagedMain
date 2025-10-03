using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;
using WpfColor = System.Windows.Media.Color;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfApp = System.Windows.Application;

namespace ManagedMain.Views
{
    public static class LogDrawerHost
    {
        public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.RegisterAttached(
            "ItemsSource", typeof(IEnumerable), typeof(LogDrawerHost), new PropertyMetadata(null, OnItemsSourceChanged));
        public static void SetItemsSource(DependencyObject d, IEnumerable? value) => d.SetValue(ItemsSourceProperty, value);
        public static IEnumerable? GetItemsSource(DependencyObject d) => (IEnumerable?)d.GetValue(ItemsSourceProperty);

        public static readonly DependencyProperty ToggleButtonProperty = DependencyProperty.RegisterAttached(
            "ToggleButton", typeof(WpfButton), typeof(LogDrawerHost), new PropertyMetadata(null, OnToggleButtonChanged));
        public static void SetToggleButton(DependencyObject d, WpfButton? value) => d.SetValue(ToggleButtonProperty, value);
        public static WpfButton? GetToggleButton(DependencyObject d) => (WpfButton?)d.GetValue(ToggleButtonProperty);

        public static readonly DependencyProperty HeightProperty = DependencyProperty.RegisterAttached(
            "Height", typeof(double), typeof(LogDrawerHost), new PropertyMetadata(220.0, OnDimensionChanged));
        public static void SetHeight(DependencyObject d, double value) => d.SetValue(HeightProperty, value);
        public static double GetHeight(DependencyObject d) => (double)d.GetValue(HeightProperty);

        public static readonly DependencyProperty AutoCloseSecondsProperty = DependencyProperty.RegisterAttached(
            "AutoCloseSeconds", typeof(int), typeof(LogDrawerHost), new PropertyMetadata(5, OnAutoCloseChanged));
        public static void SetAutoCloseSeconds(DependencyObject d, int value) => d.SetValue(AutoCloseSecondsProperty, value);
        public static int GetAutoCloseSeconds(DependencyObject d) => (int)d.GetValue(AutoCloseSecondsProperty);

        public static readonly DependencyProperty EnableBreathingProperty = DependencyProperty.RegisterAttached(
            "EnableBreathing", typeof(bool), typeof(LogDrawerHost), new PropertyMetadata(true));
        public static void SetEnableBreathing(DependencyObject d, bool value) => d.SetValue(EnableBreathingProperty, value);
        public static bool GetEnableBreathing(DependencyObject d) => (bool)d.GetValue(EnableBreathingProperty);

        public static readonly DependencyProperty TitleProperty = DependencyProperty.RegisterAttached(
            "Title", typeof(string), typeof(LogDrawerHost), new PropertyMetadata("日志"));
        public static void SetTitle(DependencyObject d, string value) => d.SetValue(TitleProperty, value);
        public static string GetTitle(DependencyObject d) => (string)d.GetValue(TitleProperty);

        public static readonly DependencyProperty AutoOpenEnabledProperty = DependencyProperty.RegisterAttached(
            "AutoOpenEnabled", typeof(bool), typeof(LogDrawerHost), new PropertyMetadata(true));
        public static void SetAutoOpenEnabled(DependencyObject d, bool value) => d.SetValue(AutoOpenEnabledProperty, value);
        public static bool GetAutoOpenEnabled(DependencyObject d) => (bool)d.GetValue(AutoOpenEnabledProperty);

        private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "_State", typeof(State), typeof(LogDrawerHost), new PropertyMetadata(null));
        private static void SetState(DependencyObject d, State? value) => d.SetValue(StateProperty, value);
        private static State? GetState(DependencyObject d) => (State?)d.GetValue(StateProperty);

        private class State
        {
            public FrameworkElement? HostElement;
            public Border? Drawer;
            public ScrollViewer? Scroll;
            public TranslateTransform? Transform;
            public bool Open;
            public double DrawerHeight;
            public WpfButton? Toggle;
            public DispatcherTimer AutoTimer = new() { Interval = TimeSpan.FromSeconds(5) };
            public DateTime LastLogUtc = DateTime.MinValue;
            public INotifyCollectionChanged? ObservableLines;
            public ItemsControl? Items;
            public bool LoadedHooked;
        }

        private static void EnsureState(DependencyObject host)
        {
            if (GetState(host) != null) return;
            SetState(host, new State());
            if (host is FrameworkElement fe && !GetState(host)!.LoadedHooked)
            {
                GetState(host)!.LoadedHooked = true;
                fe.Loaded += (_, __) => { EnsureState(host); TryBuild(host); };
                fe.Unloaded += (_, __) => Cleanup(host);
            }
        }

        private static bool TryBuild(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return false;
            if (st.Drawer != null) return true;

            var grid = FindDescendantGrid(host, skipToolBar: true);
            FrameworkElement? hostElement = grid ?? FindDescendantPanel(host, skipToolBar: true) as FrameworkElement ?? FindAncestorPanel(host) as FrameworkElement;
            if (hostElement == null) return false;
            st.HostElement = hostElement;

            var drawer = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = TryBrush("CardBrush") ?? new SolidColorBrush(WpfColor.FromArgb(255, 255, 255, 255)),
                BorderBrush = TryBrush("CardBorderBrush") ?? new SolidColorBrush(WpfColor.FromArgb(255, 220, 220, 220)),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Visibility = Visibility.Collapsed,
                Focusable = true,
                Margin = new Thickness(5),
                RenderTransform = new TranslateTransform()
            };
            st.Transform = (TranslateTransform)drawer.RenderTransform;

            var dock = new DockPanel();
            var header = new TextBlock { Text = GetTitle(host) ?? "日志", Margin = new Thickness(0), Foreground = TryBrush("TextPrimaryBrush") ?? WpfBrushes.Black };
            // Apply SectionTitle style if exists
            if (WpfApp.Current?.Resources["SectionTitle"] is Style stl && header.CanApplyStyle()) header.Style = stl;
            DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
            // Divider
            dock.Children.Add(new Separator { Height = 1, Background = TryBrush("DividerBrush") ?? new SolidColorBrush(WpfColor.FromArgb(51, 46, 42, 50)) });
            // Scroll + items
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 220, Margin = new Thickness(0, 8, 0, 0), Focusable = true };
            var items = new ItemsControl();
            items.ItemsSource = GetItemsSource(host);
            // Ensure TextBlock binds to the string item
            var fef = new FrameworkElementFactory(typeof(TextBlock));
            fef.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
            items.ItemTemplate = new DataTemplate(typeof(string)) { VisualTree = fef };
            scroll.Content = items; dock.Children.Add(scroll); drawer.Child = dock;
            st.Scroll = scroll; st.Items = items;

            if (hostElement is Grid g)
            {
                int rows = Math.Max(1, g.RowDefinitions.Count); int cols = Math.Max(1, g.ColumnDefinitions.Count);
                Grid.SetRow(drawer, 0); Grid.SetRowSpan(drawer, rows); Grid.SetColumn(drawer, 0); Grid.SetColumnSpan(drawer, cols);
                g.Children.Add(drawer);
            }
            else if (hostElement is WpfPanel p) { p.Children.Add(drawer); }
            System.Windows.Controls.Panel.SetZIndex(drawer, 1000);

            st.Drawer = drawer;
            st.AutoTimer.Tick += (s, e) => OnAutoTimer(host);
            st.DrawerHeight = GetHeight(host); st.Transform.Y = st.DrawerHeight; if (st.Scroll != null) st.Scroll.Height = st.DrawerHeight - 20;

            WireItemsSource(host, st);
            WireToggle(host, st);
            return true;
        }

        private static void Cleanup(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return;
            try
            {
                if (st.Toggle is not null) st.Toggle.Click -= Toggle_Click;
                if (st.ObservableLines is not null) st.ObservableLines.CollectionChanged -= Lines_CollectionChanged;
                st.AutoTimer.Stop();
                if (st.HostElement is Grid g && st.Drawer is not null) g.Children.Remove(st.Drawer);
                else if (st.HostElement is WpfPanel p && st.Drawer is not null) p.Children.Remove(st.Drawer);
                StopBreathing(st);
            }
            catch { }
            SetState(host, null);
        }

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EnsureState(d); TryBuild(d);
            var st = GetState(d); if (st == null) return;
            WireItemsSource(d, st);
        }

        private static void OnToggleButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            EnsureState(d); TryBuild(d);
            var st = GetState(d); if (st == null) return;
            // Unwire old
            if (e.OldValue is WpfButton oldBtn) oldBtn.Click -= Toggle_Click;
            WireToggle(d, st);
        }

        private static void OnDimensionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var st = GetState(d); if (st == null) return;
            st.DrawerHeight = GetHeight(d);
            if (!st.Open && st.Transform != null) st.Transform.Y = st.DrawerHeight;
            if (st.Scroll != null) st.Scroll.Height = st.DrawerHeight - 20;
        }

        private static void OnAutoCloseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var st = GetState(d); if (st == null) return;
            st.AutoTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, GetAutoCloseSeconds(d)));
        }

        private static void Lines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems == null || e.NewItems.Count == 0) return;
            var hosts = WpfApp.Current?.Windows?.Cast<System.Windows.Window>()
                .SelectMany(w => FindVisualChildren<FrameworkElement>(w))
                .Where(fe => GetState(fe) != null)
                .ToList();
            if (hosts == null) return;
            foreach (var host in hosts)
            {
                var st = GetState(host)!;
                // Only affect hosts bound to this ItemsSource
                if (!ReferenceEquals(st.Items?.ItemsSource, sender)) continue;
                st.LastLogUtc = DateTime.UtcNow;
                if (!GetAutoOpenEnabled(host)) continue; // respect setting
                Open(host); TryScrollToEnd(st); RestartTimer(st, host);
            }
        }

        private static void Toggle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not WpfButton btn) return;
            var host = FindHostWithToggle(btn); if (host == null) return; TryBuild(host);
            var st = GetState(host)!; if (st.Open) Close(host); else { Open(host); st.LastLogUtc = DateTime.UtcNow; RestartTimer(st, host); }
        }

        private static FrameworkElement? FindHostWithToggle(WpfButton btn)
        {
            for (DependencyObject? cur = btn; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (cur is FrameworkElement fe)
                { var st = GetState(fe); if (st != null && st.Toggle == btn) return fe; }
            }
            return null;
        }

        private static void OnAutoTimer(DependencyObject host)
        {
            var st = GetState(host); if (st == null) return;
            if (!st.Open) { st.AutoTimer.Stop(); return; }
            var since = DateTime.UtcNow - st.LastLogUtc;
            if (since < TimeSpan.FromSeconds(Math.Max(1, GetAutoCloseSeconds(host)))) return;
            if (st.Drawer != null)
            {
                if (st.Drawer.IsMouseOver) return;
                var focused = Keyboard.FocusedElement as DependencyObject;
                if (focused != null && IsDescendantOf(focused, st.Drawer)) return;
            }
            Close(host);
        }

        private static void RestartTimer(State st, DependencyObject host)
        { st.AutoTimer.Stop(); st.AutoTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, GetAutoCloseSeconds(host))); st.AutoTimer.Start(); }

        private static void Open(DependencyObject host)
        {
            var st = GetState(host); if (st == null || st.Drawer == null || st.Transform == null) return;
            if (st.Open) { TryScrollToEnd(st); return; }
            BringToFront(st);
            st.Open = true; st.Drawer.Visibility = Visibility.Visible; st.Drawer.Focus();
            var anim = new DoubleAnimation { From = st.DrawerHeight, To = 0, Duration = TimeSpan.FromMilliseconds(220), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            st.Transform.BeginAnimation(TranslateTransform.YProperty, anim); StartBreathing(st);
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
            var st = GetState(host); if (st == null || st.Drawer == null || st.Transform == null) return;
            if (!st.Open) return; st.Open = false;
            var anim = new DoubleAnimation { From = 0, To = st.DrawerHeight, Duration = TimeSpan.FromMilliseconds(180), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            anim.Completed += (_, __) => { if (!st.Open) st.Drawer.Visibility = Visibility.Collapsed; };
            st.Transform.BeginAnimation(TranslateTransform.YProperty, anim); st.AutoTimer.Stop(); StopBreathing(st);
        }

        private static void TryScrollToEnd(State st) { try { st.Scroll?.ScrollToEnd(); } catch { } }

        private static void WireItemsSource(DependencyObject host, State st)
        {
            if (st.ObservableLines is not null) st.ObservableLines.CollectionChanged -= Lines_CollectionChanged;
            var src = GetItemsSource(host) as IEnumerable;
            if (st.Items != null) st.Items.ItemsSource = src;
            st.ObservableLines = src as INotifyCollectionChanged;
            if (st.ObservableLines is not null) st.ObservableLines.CollectionChanged += Lines_CollectionChanged;
        }

        private static void WireToggle(DependencyObject host, State st)
        {
            if (st.Toggle is not null) st.Toggle.Click -= Toggle_Click;
            var btn = GetToggleButton(host);
            if (btn is not null) { st.Toggle = btn; btn.Click += Toggle_Click; }
        }

        private static WpfPanel? FindAncestorPanel(DependencyObject start)
        { for (DependencyObject? cur = start; cur != null; cur = VisualTreeHelper.GetParent(cur)) if (cur is WpfPanel p) return p; return null; }
        private static WpfPanel? FindDescendantPanel(DependencyObject root, bool skipToolBar)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ToolBar) continue;
                if (child is WpfPanel p && !HasAncestor<ToolBar>(p)) return p;
                var found = FindDescendantPanel(child, skipToolBar);
                if (found != null) return found;
            }
            return null;
        }
        private static Grid? FindDescendantGrid(DependencyObject root, bool skipToolBar)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ToolBar) continue;
                if (child is Grid g && !HasAncestor<ToolBar>(g)) return g;
                var found = FindDescendantGrid(child, skipToolBar);
                if (found != null) return found;
            }
            return null;
        }
        private static bool HasAncestor<T>(DependencyObject node) where T : DependencyObject
        { for (DependencyObject? cur = VisualTreeHelper.GetParent(node); cur != null; cur = VisualTreeHelper.GetParent(cur)) if (cur is T) return true; return false; }

        private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
        { for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur)) if (cur == ancestor) return true; return false; }

        private static SolidColorBrush? TryBrush(string key) { try { return WpfApp.Current?.Resources[key] as SolidColorBrush; } catch { return null; } }

        private static void StartBreathing(State st)
        {
            try
            {
                if (st.Toggle is null) return; if (!GetEnableBreathing(st.Toggle)) return;
                var weak = (WpfApp.Current?.Resources["ButtonAccentWeakBrush"] as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SkyBlue;
                var accent = (WpfApp.Current?.Resources["ButtonAccentBrush"] as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.DodgerBlue;
                var borderMuted = (WpfApp.Current?.Resources["ButtonAccentMutedBrush"] as SolidColorBrush)?.Color ?? System.Windows.Media.Colors.SteelBlue;
                var bg = new SolidColorBrush(weak); var bb = new SolidColorBrush(borderMuted); st.Toggle.Background = bg; st.Toggle.BorderBrush = bb;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var bgAnim = new ColorAnimation { From = weak, To = accent, Duration = TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                var bdAnim = new ColorAnimation { From = borderMuted, To = accent, Duration = TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                bg.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim); bb.BeginAnimation(SolidColorBrush.ColorProperty, bdAnim);
            }
            catch { }
        }
        private static void StopBreathing(State st)
        { try { if (st.Toggle is null) return; if (st.Toggle.Background is SolidColorBrush bg) bg.BeginAnimation(SolidColorBrush.ColorProperty, null); if (st.Toggle.BorderBrush is SolidColorBrush bb) bb.BeginAnimation(SolidColorBrush.ColorProperty, null); st.Toggle.ClearValue(System.Windows.Controls.Button.BackgroundProperty); st.Toggle.ClearValue(System.Windows.Controls.Button.BorderBrushProperty); } catch { } }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
        { if (root == null) yield break; int count = VisualTreeHelper.GetChildrenCount(root); for (int i = 0; i < count; i++) { var child = VisualTreeHelper.GetChild(root, i); if (child is T t) yield return t; foreach (var c in FindVisualChildren<T>(child)) yield return c; } }
        private static bool CanApplyStyle(this FrameworkElement fe) => true;
    }
}
