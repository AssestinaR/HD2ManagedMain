using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HD2ModManager.Views;

public sealed class IndentMarginConverter : IValueConverter
{
    public static IndentMarginConverter Instance { get; } = new();
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => new Thickness(value is double indent ? indent : 0, 0, 0, 0);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
