using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using InputMouseEventArgs = System.Windows.Input.MouseEventArgs;
using System.Windows.Threading;

namespace ManagedMain.UI.Behaviors
{
    public static class HoverPopupBehavior
    {
        public static readonly DependencyProperty TargetPopupProperty = DependencyProperty.RegisterAttached(
            "TargetPopup", typeof(Popup), typeof(HoverPopupBehavior), new PropertyMetadata(null, OnTargetPopupChanged));
        public static void SetTargetPopup(DependencyObject element, Popup? value) => element.SetValue(TargetPopupProperty, value);
        public static Popup? GetTargetPopup(DependencyObject element) => (Popup?)element.GetValue(TargetPopupProperty);

        public static readonly DependencyProperty DelayProperty = DependencyProperty.RegisterAttached(
            "Delay", typeof(int), typeof(HoverPopupBehavior), new PropertyMetadata(350));
        public static void SetDelay(DependencyObject element, int value) => element.SetValue(DelayProperty, value);
        public static int GetDelay(DependencyObject element) => (int)element.GetValue(DelayProperty);

        private static readonly DependencyProperty OpenTimerProperty = DependencyProperty.RegisterAttached(
            "OpenTimer", typeof(DispatcherTimer), typeof(HoverPopupBehavior));
        private static void SetOpenTimer(DependencyObject element, DispatcherTimer? value) => element.SetValue(OpenTimerProperty, value);
        private static DispatcherTimer? GetOpenTimer(DependencyObject element) => (DispatcherTimer?)element.GetValue(OpenTimerProperty);

        private static void OnTargetPopupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;
            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;

            if (e.NewValue is Popup)
            {
                fe.MouseEnter += OnMouseEnter;
                fe.MouseLeave += OnMouseLeave;
                fe.Unloaded += OnUnloaded;
            }
            else
            {
                var t = GetOpenTimer(fe);
                if (t != null)
                {
                    t.Stop();
                    t.Tick -= OnTimerTick;
                    SetOpenTimer(fe, null);
                }
            }
        }

        private static void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var t = GetOpenTimer(fe);
            if (t != null)
            {
                t.Stop();
                t.Tick -= OnTimerTick;
                SetOpenTimer(fe, null);
            }
            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;
        }

        private static void OnMouseEnter(object sender, InputMouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var popup = GetTargetPopup(fe); if (popup == null) return;
            var delay = GetDelay(fe);
            var timer = GetOpenTimer(fe);
            if (timer == null)
            {
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay), Tag = fe };
                timer.Tick += OnTimerTick;
                SetOpenTimer(fe, timer);
            }
            else
            {
                timer.Interval = TimeSpan.FromMilliseconds(delay);
            }
            timer.Stop();
            timer.Start();
        }

        private static void OnMouseLeave(object sender, InputMouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var timer = GetOpenTimer(fe);
            if (timer != null) timer.Stop();
            var popup = GetTargetPopup(fe);
            if (popup != null) popup.IsOpen = false;
        }

        private static void OnTimerTick(object? sender, EventArgs e)
        {
            if (sender is not DispatcherTimer timer) return;
            timer.Stop();
            if (timer.Tag is not FrameworkElement fe) return;
            var popup = GetTargetPopup(fe); if (popup == null) return;
            // only open if still hovered
            if (fe.IsMouseOver)
            {
                popup.PlacementTarget ??= fe;
                popup.IsOpen = true;
            }
        }
    }
}
