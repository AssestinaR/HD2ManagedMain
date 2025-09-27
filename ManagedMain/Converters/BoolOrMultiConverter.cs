using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class BoolOrMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                return values?.OfType<bool>().Any(b => b) == true;
            }
            catch { return false; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
