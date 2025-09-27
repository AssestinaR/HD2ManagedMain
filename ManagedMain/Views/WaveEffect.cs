using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ManagedMain.Views
{
    public static class WaveEffect
    {
        public static readonly DependencyProperty IsSurgingProperty = DependencyProperty.RegisterAttached(
            "IsSurging", typeof(bool), typeof(WaveEffect), new PropertyMetadata(false, OnIsSurgingChanged));
        public static void SetIsSurging(DependencyObject d, bool value) => d.SetValue(IsSurgingProperty, value);
        public static bool GetIsSurging(DependencyObject d) => (bool)d.GetValue(IsSurgingProperty);

        public static readonly DependencyProperty PeriodSecondsProperty = DependencyProperty.RegisterAttached(
            "PeriodSeconds", typeof(double), typeof(WaveEffect), new PropertyMetadata(1.8));
        public static void SetPeriodSeconds(DependencyObject d, double value) => d.SetValue(PeriodSecondsProperty, value);
        public static double GetPeriodSeconds(DependencyObject d) => (double)d.GetValue(PeriodSecondsProperty);

        public static readonly DependencyProperty CrestBrushProperty = DependencyProperty.RegisterAttached(
            "CrestBrush", typeof(System.Windows.Media.Brush), typeof(WaveEffect), new PropertyMetadata(null));
        public static void SetCrestBrush(DependencyObject d, System.Windows.Media.Brush? value) => d.SetValue(CrestBrushProperty, value);
        public static System.Windows.Media.Brush? GetCrestBrush(DependencyObject d) => (System.Windows.Media.Brush?)d.GetValue(CrestBrushProperty);

        public static readonly DependencyProperty TroughBrushProperty = DependencyProperty.RegisterAttached(
            "TroughBrush", typeof(System.Windows.Media.Brush), typeof(WaveEffect), new PropertyMetadata(null));
        public static void SetTroughBrush(DependencyObject d, System.Windows.Media.Brush? value) => d.SetValue(TroughBrushProperty, value);
        public static System.Windows.Media.Brush? GetTroughBrush(DependencyObject d) => (System.Windows.Media.Brush?)d.GetValue(TroughBrushProperty);

        private static readonly DependencyProperty SavedBackgroundProperty = DependencyProperty.RegisterAttached(
            "_SavedBackground", typeof(System.Windows.Media.Brush), typeof(WaveEffect), new PropertyMetadata(null));
        private static void SetSavedBackground(DependencyObject d, System.Windows.Media.Brush? value) => d.SetValue(SavedBackgroundProperty, value);
        private static System.Windows.Media.Brush? GetSavedBackground(DependencyObject d) => (System.Windows.Media.Brush?)d.GetValue(SavedBackgroundProperty);

        private static readonly DependencyProperty HadLocalBackgroundProperty = DependencyProperty.RegisterAttached(
            "_HadLocalBackground", typeof(bool), typeof(WaveEffect), new PropertyMetadata(false));
        private static void SetHadLocalBackground(DependencyObject d, bool value) => d.SetValue(HadLocalBackgroundProperty, value);
        private static bool GetHadLocalBackground(DependencyObject d) => (bool)d.GetValue(HadLocalBackgroundProperty);

        private static void OnIsSurgingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not System.Windows.Controls.Button btn) return;
            bool on = (bool)e.NewValue;
            if (on) Start(btn); else Stop(btn);
        }

        private static void Start(System.Windows.Controls.Button btn)
        {
            try
            {
                // Track whether Background had a local value so we can restore correctly
                bool hadLocal = btn.ReadLocalValue(System.Windows.Controls.Control.BackgroundProperty) != DependencyProperty.UnsetValue;
                SetHadLocalBackground(btn, hadLocal);
                if (GetSavedBackground(btn) is null)
                {
                    SetSavedBackground(btn, btn.Background);
                }

                // Build gradient with crest and trough colors
                var crest = (GetCrestBrush(btn) as SolidColorBrush)?.Color
                            ?? (System.Windows.Application.Current?.Resources["ButtonAccentBrush"] as SolidColorBrush)?.Color
                            ?? System.Windows.Media.Colors.DodgerBlue;
                var trough = (GetTroughBrush(btn) as SolidColorBrush)?.Color
                            ?? (System.Windows.Application.Current?.Resources["ButtonAccentWeakBrush"] as SolidColorBrush)?.Color
                            ?? System.Windows.Media.Colors.LightSkyBlue;

                var brush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0.5),
                    EndPoint = new System.Windows.Point(1, 0.5),
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    RelativeTransform = new TranslateTransform { X = -1, Y = 0 }
                };
                brush.GradientStops.Add(new GradientStop(trough, 0.0));
                brush.GradientStops.Add(new GradientStop(crest, 0.25));
                brush.GradientStops.Add(new GradientStop(trough, 0.5));
                brush.GradientStops.Add(new GradientStop(trough, 1.0));

                // Assign as local value while animating
                btn.Background = brush;

                var transform = (TranslateTransform)brush.RelativeTransform;
                var dur = TimeSpan.FromSeconds(Math.Max(0.3, GetPeriodSeconds(btn)));
                var anim = new DoubleAnimation
                {
                    From = -1,
                    To = 1,
                    Duration = dur,
                    RepeatBehavior = RepeatBehavior.Forever
                };
                transform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            catch { }
        }

        private static void Stop(System.Windows.Controls.Button btn)
        {
            try
            {
                if (btn.Background is LinearGradientBrush lgb && lgb.RelativeTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                }
                bool hadLocal = GetHadLocalBackground(btn);
                var saved = GetSavedBackground(btn);
                if (hadLocal)
                {
                    // Restore previous local value
                    if (saved is not null) btn.Background = saved;
                }
                else
                {
                    // Clear local value to return control back to style setters/triggers
                    btn.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                }
                // cleanup
                SetSavedBackground(btn, null);
                SetHadLocalBackground(btn, false);
            }
            catch { }
        }
    }
}
