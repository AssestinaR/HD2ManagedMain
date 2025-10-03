using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    public class NotificationBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var level = value is NotificationLevel nl ? nl : NotificationLevel.Info;
            return level switch
            {
                NotificationLevel.Info => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2196F3")),
                NotificationLevel.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FB8C00")),
                NotificationLevel.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E53935")),
                _ => Brushes.Gray
            };
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
