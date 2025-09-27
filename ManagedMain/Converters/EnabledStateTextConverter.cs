using System;
using System.Globalization;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class EnabledStateTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                int v = value is int i ? i : System.Convert.ToInt32(value);
                return v switch
                {
                    0 => "未启用",
                    1 => "已启用",
                    2 => "部分启用",
                    _ => value?.ToString() ?? string.Empty
                };
            }
            catch { return value?.ToString() ?? string.Empty; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
