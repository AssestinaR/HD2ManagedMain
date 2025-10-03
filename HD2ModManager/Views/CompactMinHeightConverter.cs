using System;
using System.Globalization;
using System.Windows.Data;

namespace HD2ModManager.Views
{
    public class CompactMinHeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool compact = value is bool b && b;
            // Reduce compact mode height by ~1/4; adjust to 60 for 48px image
            return compact ? 60.0 : 180.0; // default large min height
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is double d && d <= 72.0);
        }
    }
}
