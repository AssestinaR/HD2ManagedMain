using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HD2ModManager.Views
{
    // 作用：根据 bool 状态把 slot 列宽转换为指定 GridLength。
    public sealed class BoolToStarLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var isVisible = value is bool b && b;
            if (isVisible)
            {
                if (parameter is not null && double.TryParse(parameter.ToString(), NumberStyles.Float, culture, out var fixedWidth))
                {
                    return new GridLength(fixedWidth);
                }

                return new GridLength(1, GridUnitType.Star);
            }

            return new GridLength(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}