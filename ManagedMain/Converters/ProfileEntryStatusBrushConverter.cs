using System;
using System.Globalization;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    // Profiles tree: Green = enabled, Blue = open but not enabled, Black = default
    public class ProfileEntryStatusBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                bool enabled = values.Length > 0 && values[0] is bool b1 && b1;
                bool open = values.Length > 1 && values[1] is bool b2 && b2;
                if (enabled) return System.Windows.Media.Brushes.Green;
                if (open) return System.Windows.Media.Brushes.DodgerBlue;
                return System.Windows.Media.Brushes.Black;
            }
            catch { return System.Windows.Media.Brushes.Black; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
