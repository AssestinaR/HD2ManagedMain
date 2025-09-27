using System;
using System.Globalization;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class EnabledStateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // ManagedMain models use int: 0=Disabled, 1=Enabled, 2=Partial
            if (value is int v)
            {
                return v switch
                {
                    1 => System.Windows.Media.Brushes.Green,
                    2 => System.Windows.Media.Brushes.DodgerBlue,
                    _ => System.Windows.Media.Brushes.Black
                };
            }
            return System.Windows.Media.Brushes.Black;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
