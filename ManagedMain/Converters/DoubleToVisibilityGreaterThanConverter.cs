using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class DoubleToVisibilityGreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double v = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                double threshold = parameter == null ? 60 : System.Convert.ToDouble(parameter, CultureInfo.InvariantCulture);
                return v >= threshold ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { return Visibility.Collapsed; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
