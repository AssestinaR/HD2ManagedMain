using System;
using System.Globalization;
using System.Windows.Data;

namespace HD2ModManager.Views
{
    public class CompactWidthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool compact = value is bool b && b;
            // Both compact and large card width: 240
            return 240.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value is double d && d <= 240.0);
        }
    }
}
