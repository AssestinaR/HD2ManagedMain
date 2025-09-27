using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ManagedMain.Views
{
    public static class BreathingEffect
    {
        public static readonly DependencyProperty IsBreathingProperty = DependencyProperty.RegisterAttached(
            "IsBreathing", typeof(bool), typeof(BreathingEffect), new PropertyMetadata(false, OnIsBreathingChanged));
        public static void SetIsBreathing(DependencyObject d, bool value) => d.SetValue(IsBreathingProperty, value);
        public static bool GetIsBreathing(DependencyObject d) => (bool)d.GetValue(IsBreathingProperty);

        private static readonly DependencyProperty StoryboardProperty = DependencyProperty.RegisterAttached(
            "_BreathingStoryboard", typeof(Storyboard), typeof(BreathingEffect), new PropertyMetadata(null));
        private static void SetStoryboard(DependencyObject d, Storyboard? sb) => d.SetValue(StoryboardProperty, sb);
        private static Storyboard? GetStoryboard(DependencyObject d) => (Storyboard?)d.GetValue(StoryboardProperty);

        public static readonly DependencyProperty PeriodSecondsProperty = DependencyProperty.RegisterAttached(
            "PeriodSeconds", typeof(double), typeof(BreathingEffect), new PropertyMetadata(1.2));
        public static void SetPeriodSeconds(DependencyObject d, double value) => d.SetValue(PeriodSecondsProperty, value);
        public static double GetPeriodSeconds(DependencyObject d) => (double)d.GetValue(PeriodSecondsProperty);

        // Save/restore original brushes on Button so we can replace with animatable clones
        private static readonly DependencyProperty SavedBackgroundProperty = DependencyProperty.RegisterAttached(
            "_SavedBackground", typeof(System.Windows.Media.Brush), typeof(BreathingEffect), new PropertyMetadata(null));
        private static void SetSavedBackground(DependencyObject d, System.Windows.Media.Brush? value) => d.SetValue(SavedBackgroundProperty, value);
        private static System.Windows.Media.Brush? GetSavedBackground(DependencyObject d) => (System.Windows.Media.Brush?)d.GetValue(SavedBackgroundProperty);
        private static readonly DependencyProperty SavedBorderBrushProperty = DependencyProperty.RegisterAttached(
            "_SavedBorderBrush", typeof(System.Windows.Media.Brush), typeof(BreathingEffect), new PropertyMetadata(null));
        private static void SetSavedBorderBrush(DependencyObject d, System.Windows.Media.Brush? value) => d.SetValue(SavedBorderBrushProperty, value);
        private static System.Windows.Media.Brush? GetSavedBorderBrush(DependencyObject d) => (System.Windows.Media.Brush?)d.GetValue(SavedBorderBrushProperty);
        private static readonly DependencyProperty HadLocalBackgroundProperty = DependencyProperty.RegisterAttached(
            "_HadLocalBackground", typeof(bool), typeof(BreathingEffect), new PropertyMetadata(false));
        private static void SetHadLocalBackground(DependencyObject d, bool value) => d.SetValue(HadLocalBackgroundProperty, value);
        private static bool GetHadLocalBackground(DependencyObject d) => (bool)d.GetValue(HadLocalBackgroundProperty);
        private static readonly DependencyProperty HadLocalBorderBrushProperty = DependencyProperty.RegisterAttached(
            "_HadLocalBorderBrush", typeof(bool), typeof(BreathingEffect), new PropertyMetadata(false));
        private static void SetHadLocalBorderBrush(DependencyObject d, bool value) => d.SetValue(HadLocalBorderBrushProperty, value);
        private static bool GetHadLocalBorderBrush(DependencyObject d) => (bool)d.GetValue(HadLocalBorderBrushProperty);

        private static void OnIsBreathingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not System.Windows.Controls.Button btn) { ToggleUI(d as UIElement, (bool)e.NewValue); return; }
            bool on = (bool)e.NewValue;
            if (on) StartButton(btn); else StopButton(btn);
        }

        private static void ToggleUI(UIElement? ui, bool on)
        {
            if (ui == null) return;
            if (on) StartUI(ui); else StopUI(ui);
        }

        private static void StartUI(UIElement ui)
        {
            try
            {
                StopUI(ui);
                var dur = TimeSpan.FromSeconds(Math.Max(0.3, GetPeriodSeconds(ui)));
                var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };
                // Stronger opacity span for visibility
                var op = new DoubleAnimation { From = 0.7, To = 1.0, Duration = dur };
                Storyboard.SetTarget(op, ui);
                Storyboard.SetTargetProperty(op, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(op);
                SetStoryboard(ui, sb);
                sb.Begin();
            }
            catch { }
        }

        private static void StopUI(UIElement ui)
        {
            try
            {
                var sb = GetStoryboard(ui);
                sb?.Stop();
                SetStoryboard(ui, null);
                ui.BeginAnimation(UIElement.OpacityProperty, null);
                ui.Opacity = 1.0;
            }
            catch { }
        }

        private static void StartButton(System.Windows.Controls.Button btn)
        {
            try
            {
                StopButton(btn);
                // Save previous local values
                SetHadLocalBackground(btn, btn.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue);
                SetHadLocalBorderBrush(btn, btn.ReadLocalValue(System.Windows.Controls.Control.BorderBrushProperty) != DependencyProperty.UnsetValue);
                SetSavedBackground(btn, btn.Background);
                SetSavedBorderBrush(btn, btn.BorderBrush);

                // Clone animatable brushes from theme
                var bgBase = (System.Windows.Application.Current?.Resources["ButtonAccentWeakBrush"] as SolidColorBrush)?.Color ?? Colors.LightSkyBlue;
                var bgPeak = (System.Windows.Application.Current?.Resources["ButtonAccentMutedBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                var bdBase = (System.Windows.Application.Current?.Resources["ButtonAccentMutedBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                var bdPeak = (System.Windows.Application.Current?.Resources["ButtonAccentBrush"] as SolidColorBrush)?.Color ?? Colors.RoyalBlue;

                var bg = new System.Windows.Media.SolidColorBrush(bgBase);
                var bd = new System.Windows.Media.SolidColorBrush(bdBase);
                btn.Background = bg;
                btn.BorderBrush = bd;

                var dur = TimeSpan.FromSeconds(Math.Max(0.3, GetPeriodSeconds(btn)));
                var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever, AutoReverse = true };

                var op = new DoubleAnimation { From = 0.8, To = 1.0, Duration = dur };
                Storyboard.SetTarget(op, btn);
                Storyboard.SetTargetProperty(op, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(op);

                var bgAnim = new ColorAnimation { From = bgBase, To = bgPeak, Duration = dur };
                Storyboard.SetTarget(bgAnim, btn);
                Storyboard.SetTargetProperty(bgAnim, new PropertyPath("(Control.Background).(SolidColorBrush.Color)"));
                sb.Children.Add(bgAnim);

                var bdAnim = new ColorAnimation { From = bdBase, To = bdPeak, Duration = dur };
                Storyboard.SetTarget(bdAnim, btn);
                Storyboard.SetTargetProperty(bdAnim, new PropertyPath("(Control.BorderBrush).(SolidColorBrush.Color)"));
                sb.Children.Add(bdAnim);

                SetStoryboard(btn, sb);
                sb.Begin();
            }
            catch { }
        }

        private static void StopButton(System.Windows.Controls.Button btn)
        {
            try
            {
                var sb = GetStoryboard(btn);
                sb?.Stop();
                SetStoryboard(btn, null);
                btn.BeginAnimation(UIElement.OpacityProperty, null);
                // Restore brushes
                bool hadBg = GetHadLocalBackground(btn);
                bool hadBd = GetHadLocalBorderBrush(btn);
                var savedBg = GetSavedBackground(btn);
                var savedBd = GetSavedBorderBrush(btn);
                if (hadBg)
                {
                    if (savedBg != null) btn.Background = savedBg;
                }
                else { btn.ClearValue(System.Windows.Controls.Control.BackgroundProperty); }
                if (hadBd)
                {
                    if (savedBd != null) btn.BorderBrush = savedBd;
                }
                else { btn.ClearValue(System.Windows.Controls.Control.BorderBrushProperty); }
                SetSavedBackground(btn, null);
                SetSavedBorderBrush(btn, null);
                SetHadLocalBackground(btn, false);
                SetHadLocalBorderBrush(btn, false);
                btn.Opacity = 1.0;
            }
            catch { }
        }
    }
}
