using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace HD2ModManager.Views
{
    public class ImagePlaceholderConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (!string.IsNullOrWhiteSpace(s))
            {
                try { return new BitmapImage(new Uri(s, UriKind.RelativeOrAbsolute)); } catch { }
            }
            // fallback to embedded resource
            try
            {
                var uri = new Uri("pack://application:,,,/HD2ModManager;component/Resources/helldivers2.png", UriKind.Absolute);
                return new BitmapImage(uri);
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
