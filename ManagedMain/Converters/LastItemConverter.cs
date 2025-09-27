using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class LastItemConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is IEnumerable enumerable)
            {
                object? last = null;
                foreach (var item in enumerable) last = item;
                return last ?? string.Empty;
            }
            return string.Empty;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
