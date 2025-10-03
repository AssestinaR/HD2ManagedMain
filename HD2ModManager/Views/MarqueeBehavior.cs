using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace HD2ModManager.Views
{
    public static class MarqueeBehavior
    {
        public static readonly DependencyProperty EnableProperty = DependencyProperty.RegisterAttached(
            "Enable", typeof(bool), typeof(MarqueeBehavior), new PropertyMetadata(false, OnEnableChanged));

        public static void SetEnable(DependencyObject d, bool value) => d.SetValue(EnableProperty, value);
        public static bool GetEnable(DependencyObject d) => (bool)d.GetValue(EnableProperty);

        public static readonly DependencyProperty SpeedProperty = DependencyProperty.RegisterAttached(
            "Speed", typeof(double), typeof(MarqueeBehavior), new PropertyMetadata(30.0));

        public static void SetSpeed(DependencyObject d, double value) => d.SetValue(SpeedProperty, value);
        public static double GetSpeed(DependencyObject d) => (double)d.GetValue(SpeedProperty);

        // When true (default), only run marquee while the ScrollViewer is hovered or has focus, reducing global animation load
        public static readonly DependencyProperty ActivateOnHoverProperty = DependencyProperty.RegisterAttached(
            "ActivateOnHover", typeof(bool), typeof(MarqueeBehavior), new PropertyMetadata(true));
        public static void SetActivateOnHover(DependencyObject d, bool value) => d.SetValue(ActivateOnHoverProperty, value);
        public static bool GetActivateOnHover(DependencyObject d) => (bool)d.GetValue(ActivateOnHoverProperty);
        // Container hover: when enabled on a parent container, hovering the card will start/stop all child marquees
        public static readonly DependencyProperty ContainerHoverEnableProperty = DependencyProperty.RegisterAttached(
            "ContainerHoverEnable", typeof(bool), typeof(MarqueeBehavior), new PropertyMetadata(false, OnContainerHoverEnableChanged));
        public static void SetContainerHoverEnable(DependencyObject d, bool value) => d.SetValue(ContainerHoverEnableProperty, value);
        public static bool GetContainerHoverEnable(DependencyObject d) => (bool)d.GetValue(ContainerHoverEnableProperty);

        private static void OnContainerHoverEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement fe && (bool)e.NewValue)
            {
                fe.Loaded += (s, _) =>
                {
                    fe.MouseEnter += (_, __) => ToggleChildMarquees(fe, true);
                    fe.MouseLeave += (_, __) => ToggleChildMarquees(fe, false);
                };
                fe.Unloaded += (s, _) => ToggleChildMarquees(fe, false);
            }
        }

        private static void ToggleChildMarquees(FrameworkElement root, bool start)
        {
            try
            {
                foreach (var sv in FindDescendants<ScrollViewer>(root))
                {
                    if (!GetEnable(sv)) continue;
                    if (start) StartMarquee(sv); else StopMarquee(sv);
                }
            }
            catch { }
        }

        private static IEnumerable<T> FindDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            var queue = new Queue<DependencyObject>();
            if (root != null) queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                int count = VisualTreeHelper.GetChildrenCount(current);
                for (int i = 0; i < count; i++)
                {
                    var child = VisualTreeHelper.GetChild(current, i);
                    if (child is T t) yield return t;
                    queue.Enqueue(child);
                }
            }
        }

        private static void OnEnableChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                if ((bool)e.NewValue)
                {
                    sv.Loaded += (s, _) =>
                    {
                        if (GetActivateOnHover(sv))
                        {
                            // defer start until hovered
                            // subscribe to hover/focus
                            sv.MouseEnter += (_, __) => StartMarquee(sv);
                            sv.MouseLeave += (_, __) => StopMarquee(sv);
                            sv.IsKeyboardFocusWithinChanged += (_, __) =>
                            {
                                if (sv.IsKeyboardFocusWithin) StartMarquee(sv); else StopMarquee(sv);
                            };
                        }
                        else
                        {
                            StartMarquee(sv);
                        }
                    };
                    sv.Unloaded += (s, _) => StopMarquee(sv);
                }
            }
        }

        private static void StartMarquee(ScrollViewer sv)
        {
            try
            {
                sv.ScrollToHorizontalOffset(0);
                var content = sv.Content as FrameworkElement;
                if (content == null) return;
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double contentWidth = content.DesiredSize.Width;
                double viewportWidth = sv.ViewportWidth;
                if (contentWidth <= viewportWidth + 1) return; // no need to scroll
                double duration = Math.Max(1, contentWidth / Math.Max(1, GetSpeed(sv)));
                var anim = new DoubleAnimation
                {
                    From = 0,
                    To = contentWidth - viewportWidth,
                    Duration = TimeSpan.FromSeconds(duration),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                };
                var clock = anim.CreateClock();
                sv.ApplyAnimationClock(HorizontalOffsetProperty, clock);
                clock.Completed += (_, __) => sv.ScrollToHorizontalOffset(0);
            }
            catch { }
        }

        private static void StopMarquee(ScrollViewer sv)
        {
            try
            {
                sv.ApplyAnimationClock(HorizontalOffsetProperty, null);
                sv.ScrollToHorizontalOffset(0);
            }
            catch { }
        }

        // Proxy DP to allow animating horizontal offset via clock
        private static readonly DependencyProperty HorizontalOffsetProperty = DependencyProperty.RegisterAttached(
            "HorizontalOffset", typeof(double), typeof(MarqueeBehavior), new PropertyMetadata(0.0, OnHorizontalOffsetChanged));
        private static void OnHorizontalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
            {
                sv.ScrollToHorizontalOffset((double)e.NewValue);
            }
        }
    }
}
