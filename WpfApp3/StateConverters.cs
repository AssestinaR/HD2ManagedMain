using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace LiberTeaManager
{
    public class EnabledStateTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EnabledState v)
            {
                return v switch
                {
                    EnabledState.Enabled => "已启用",
                    EnabledState.Partial => "部分启用",
                    _ => "未启用"
                };
            }
            if (value is int legacy)
            {
                return legacy switch { 1 => "已启用", 2 => "部分启用", _ => "未启用" };
            }
            return "未启用";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    public class EnabledStateBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is EnabledState v)
            {
                return v switch
                {
                    EnabledState.Enabled => Brushes.Green,
                    EnabledState.Partial => Brushes.DodgerBlue,
                    _ => Brushes.Black
                };
            }
            if (value is int legacy)
            {
                return legacy switch { 1 => Brushes.Green, 2 => Brushes.DodgerBlue, _ => Brushes.Black };
            }
            return Brushes.Black;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
