using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace HD2ModManager.Views
{
    public class ImagePlaceholderConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value as string;
            if (!string.IsNullOrWhiteSpace(s))
            {
                try { return LoadImageWithoutLock(s); } catch { }
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

        private static BitmapImage LoadImageWithoutLock(string path)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (File.Exists(path))
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            else
            {
                bitmap.UriSource = new Uri(path, UriKind.RelativeOrAbsolute);
                bitmap.EndInit();
            }

            bitmap.Freeze();
            return bitmap;
        }
    }
}
