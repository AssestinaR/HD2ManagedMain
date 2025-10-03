using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace ManagedMain.Views
{
    // Simple adaptive layout: keep all buttons visible by shrinking proportionally when space is tight.
    // No hover expansion; always aim for max width (S/M/L) within the available budget; never below Min.
    public static class AdaptiveToolbarLayout
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(AdaptiveToolbarLayout), new PropertyMetadata(false, OnIsEnabledChanged));
        public static void SetIsEnabled(DependencyObject d, bool value) => d.SetValue(IsEnabledProperty, value);
        public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);

        private sealed class State
        {
            public ToolBar Bar = null!;
            public List<System.Windows.Controls.Button> Buttons = new();
            public double Min = 36;
            public double S = 84;
            public double M = 112;
            public double L = 150;
            public Dictionary<System.Windows.Controls.Button, long> AnimToken = new();
            public long NextToken = 0;
            public DateTime LastSizeChanged;
            public DispatcherTimer? ResizeTimer;
            public bool SuppressAnimation;
        }

        private static readonly Dictionary<ToolBar, State> _states = new();

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ToolBar bar) return;
            if (true.Equals(e.NewValue)) Enable(bar); else Disable(bar);
        }

        private static void Enable(ToolBar bar)
        {
            if (_states.ContainsKey(bar)) return;
            var st = new State { Bar = bar };
            _states[bar] = st;
            bar.Loaded += Bar_Loaded;
            bar.SizeChanged += Bar_SizeChanged;
            // Listen to parent container size changes to reflow
            try
            {
                var parent = System.Windows.Media.VisualTreeHelper.GetParent(bar) as FrameworkElement;
                if (parent != null)
                {
                    parent.SizeChanged += Parent_SizeChanged;
                }
            }
            catch { }
        }

        private static void Disable(ToolBar bar)
        {
            if (!_states.TryGetValue(bar, out var st)) return;
            bar.Loaded -= Bar_Loaded;
            bar.SizeChanged -= Bar_SizeChanged;
            try { var parent = System.Windows.Media.VisualTreeHelper.GetParent(bar) as FrameworkElement; if (parent != null) parent.SizeChanged -= Parent_SizeChanged; } catch { }
            _states.Remove(bar);
        }

        private static void Bar_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is ToolBar bar && _states.TryGetValue(bar, out var st))
            {
                TryGetDouble(bar, "BtnWidthS", ref st.S);
                TryGetDouble(bar, "BtnWidthM", ref st.M);
                TryGetDouble(bar, "BtnWidthL", ref st.L);
                TryGetDouble(bar, "CollapsedBtnWidth", ref st.Min);
                RefreshButtons(st);
                ApplyLayout(st, animate: false);
                try { bar.Dispatcher.BeginInvoke(new Action(() => ApplyLayout(st, animate: false)), System.Windows.Threading.DispatcherPriority.Background); } catch { }
            }
        }

        private static void Bar_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is ToolBar bar && _states.TryGetValue(bar, out var st))
            {
                st.LastSizeChanged = DateTime.UtcNow;
                // During continuous resize, suppress animations for snappier response
                st.SuppressAnimation = true;
                ApplyLayout(st, animate: false);
                // debounce to re-enable animations shortly after resizing stops
                if (st.ResizeTimer == null)
                {
                    st.ResizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
                    st.ResizeTimer.Tick += (_, __) =>
                    {
                        // if no resize for the timer interval, re-enable animations and apply once
                        if ((DateTime.UtcNow - st.LastSizeChanged).TotalMilliseconds >= 140)
                        {
                            st.ResizeTimer!.Stop();
                            st.SuppressAnimation = false;
                            ApplyLayout(st, animate: true);
                        }
                    };
                }
                st.ResizeTimer.Stop();
                st.ResizeTimer.Start();
            }
        }

        private static void Parent_SizeChanged(object? sender, SizeChangedEventArgs e)
        {
            foreach (var kv in _states)
            {
                if (System.Windows.Media.VisualTreeHelper.GetParent(kv.Key) == sender) { ApplyLayout(kv.Value, animate: true); }
            }
        }

        private static void RefreshButtons(State st)
        {
            st.Buttons.Clear();
            foreach (var obj in st.Bar.Items)
            {
                var container = st.Bar.ItemContainerGenerator.ContainerFromItem(obj) as FrameworkElement;
                var btn = container as System.Windows.Controls.Button ?? obj as System.Windows.Controls.Button;
                if (btn == null) continue;
                st.Buttons.Add(btn);
                try { ToolBar.SetOverflowMode(btn, OverflowMode.Never); } catch { }
            }
        }

        private static void ApplyLayout(State st, bool animate)
        {
            if (st.Bar.ActualWidth <= 0) { RefreshButtons(st); return; }
            RefreshButtons(st);
            var buttons = st.Buttons.Where(b => b.IsVisible).ToList(); if (buttons.Count == 0) return;

            double toolbarPad = 0; try { var p = st.Bar.Padding; toolbarPad = p.Left + p.Right; } catch { }
            double toolbarMargin = 0; try { var m = st.Bar.Margin; toolbarMargin = m.Left + m.Right; } catch { }
            double otherWidth = 0; double buttonMargins = 0;
            var gen = st.Bar.ItemContainerGenerator;
            for (int i = 0; i < st.Bar.Items.Count; i++)
            {
                var item = st.Bar.Items[i];
                var container = gen.ContainerFromIndex(i) as FrameworkElement;
                var fe = container ?? item as FrameworkElement;
                if (fe == null || !fe.IsVisible) continue;
                double marginLR = 0; try { var m = fe.Margin; marginLR = m.Left + m.Right; } catch { }
                if (fe is System.Windows.Controls.Button b && buttons.Contains(b)) buttonMargins += marginLR; else otherWidth += SafeActualWidth(fe) + marginLR;
            }

            double avail = Math.Max(0, st.Bar.ActualWidth - toolbarPad - toolbarMargin - otherWidth - 6);
            avail *= 0.9; // small safety margin
            double budget = Math.Max(0, avail - buttonMargins);

            // Max width per button
            double GetMax(System.Windows.Controls.Button b)
            {
                var tag = (b.Tag as string)?.ToUpperInvariant();
                if (tag == "S") return st.S; if (tag == "M") return st.M; if (tag == "L") return st.L;
                return (double.IsNaN(b.Width) || b.Width <= 0) ? st.S : b.Width;
            }

            var targets = new Dictionary<System.Windows.Controls.Button, double>();
            double desired = 0;
            foreach (var b in buttons)
            {
                double w = GetMax(b);
                if (w < st.Min) w = st.Min;
                targets[b] = w; desired += w;
            }

            if (desired > budget && budget > 0)
            {
                double excess = desired - budget;
                // Shrink proportionally but clamp to Min
                double totalRoom = buttons.Sum(b => Math.Max(0, targets[b] - st.Min));
                if (totalRoom > 0)
                {
                    foreach (var b in buttons)
                    {
                        double room = Math.Max(0, targets[b] - st.Min);
                        double take = excess * (room / totalRoom);
                        double newW = Math.Max(st.Min, targets[b] - take);
                        targets[b] = newW;
                    }
                }
                else
                {
                    // Already at Min; nothing else to do
                    foreach (var b in buttons) targets[b] = st.Min;
                }
            }

            foreach (var b in buttons)
            {
                double to = targets[b];
                // Use ActualWidth as the true current visual width because Width may still hold the pre-animation base value
                double current = b.ActualWidth > 0 ? b.ActualWidth : (double.IsNaN(b.Width) || b.Width <= 0 ? to : b.Width);
                if (st.SuppressAnimation || !animate || Math.Abs(current - to) < 0.5) { b.Width = to; continue; }
                // assign a new animation token for this button
                long token = ++st.NextToken;
                st.AnimToken[b] = token;
                 var anim = new DoubleAnimation
                 {
                     From = current,
                     To = to,
                     Duration = TimeSpan.FromMilliseconds(180),
                     EasingFunction = new CubicEase { EasingMode = current < to ? EasingMode.EaseOut : EasingMode.EaseIn },
                     FillBehavior = FillBehavior.Stop
                 };
                anim.Completed += (_, __) =>
                {
                    try
                    {
                        // only commit if this completion belongs to the latest scheduled animation
                        if (st.AnimToken.TryGetValue(b, out var cur) && cur == token)
                        {
                            b.BeginAnimation(FrameworkElement.WidthProperty, null);
                            b.Width = to;
                        }
                    }
                    catch { }
                };
                b.BeginAnimation(FrameworkElement.WidthProperty, anim, HandoffBehavior.SnapshotAndReplace);
            }
        }

        private static void TryGetDouble(FrameworkElement fe, string key, ref double dst)
        {
            try { if (fe.TryFindResource(key) is double d) dst = d; } catch { }
        }
        private static double SafeActualWidth(FrameworkElement fe)
        {
            try
            {
                var aw = fe.ActualWidth;
                if (aw > 0) return aw;
                if (!double.IsNaN(fe.Width) && fe.Width > 0) return fe.Width;
                if (fe.MinWidth > 0) return fe.MinWidth;
            }
            catch { }
            return 0;
        }
    }
}
