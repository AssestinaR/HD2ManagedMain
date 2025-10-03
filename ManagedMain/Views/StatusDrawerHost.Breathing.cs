using System;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ManagedMain.Views
{
    public static partial class StatusDrawerHost
    {
        private static void StartBreathing(State st)
        {
            try
            {
                if (st.Toggle is null) return; if (!GetEnableBreathing(st.Toggle)) return;
                var weak = (System.Windows.Application.Current?.Resources["ButtonAccentWeakBrush"] as SolidColorBrush)?.Color ?? Colors.SkyBlue;
                var accent = (System.Windows.Application.Current?.Resources["ButtonAccentBrush"] as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
                var borderMuted = (System.Windows.Application.Current?.Resources["ButtonAccentMutedBrush"] as SolidColorBrush)?.Color ?? Colors.SteelBlue;
                var bg = new SolidColorBrush(weak); var bb = new SolidColorBrush(borderMuted); st.Toggle.Background = bg; st.Toggle.BorderBrush = bb;
                var ease = new SineEase { EasingMode = EasingMode.EaseInOut };
                var bgAnim = new ColorAnimation { From = weak, To = accent, Duration = TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                var bdAnim = new ColorAnimation { From = borderMuted, To = accent, Duration = TimeSpan.FromMilliseconds(1200), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = ease };
                bg.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim); bb.BeginAnimation(SolidColorBrush.ColorProperty, bdAnim);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusDrawerHost] StartBreathing Ê§°Ü: {ex.Message}");
            }
        }

        private static void StopBreathing(State st)
        {
            try
            {
                if (st.Toggle is null) return;
                if (st.Toggle.Background is SolidColorBrush bg) bg.BeginAnimation(SolidColorBrush.ColorProperty, null);
                if (st.Toggle.BorderBrush is SolidColorBrush bb) bb.BeginAnimation(SolidColorBrush.ColorProperty, null);
                st.Toggle.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
                st.Toggle.ClearValue(System.Windows.Controls.Button.BorderBrushProperty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusDrawerHost] StopBreathing Ê§°Ü: {ex.Message}");
            }
        }
    }
}
