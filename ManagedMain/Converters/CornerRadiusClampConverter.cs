using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class CornerRadiusClampConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                double w = values.Length > 0 && values[0] is double dw ? dw : double.NaN;
                double h = values.Length > 1 && values[1] is double dh ? dh : double.NaN;
                double desired = 20.0;
                if (parameter != null && double.TryParse(parameter.ToString(), out var p)) desired = p;

                double minSide = double.IsNaN(w) || double.IsNaN(h) || w <= 0 || h <= 0 ? desired * 2 : Math.Min(w, h);
                double maxRadius = minSide / 2.0;
                double r = Math.Min(desired, maxRadius);
                return new CornerRadius(r);
            }
            catch
            {
                return new CornerRadius(0);
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
