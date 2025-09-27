using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ManagedMain.Models;

namespace ManagedMain.Converters
{
    public class ProfileImageConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                var rel = values.Length > 0 ? values[0] as string : null;
                var profile = values.Length > 1 ? values[1] as ProfileEntry : null;
                var item = values.Length > 2 ? values[2] : null;
                if (string.IsNullOrWhiteSpace(rel) || profile == null || item == null) return null;

                string mainName = item switch
                {
                    MainModItem m => m.Name,
                    OptionItem o => profile.Mods.FirstOrDefault(mm => mm.Options.Contains(o))?.Name ?? string.Empty,
                    SubOptionItem s => (
                        from mm in profile.Mods
                        from oo in mm.Options
                        where oo.SubOptions.Contains(s)
                        select mm.Name).FirstOrDefault() ?? string.Empty,
                    _ => string.Empty
                };
                if (string.IsNullOrEmpty(mainName)) return null;
                var baseDir = Path.Combine(profile.RootPath, "mod", mainName);
                var abs = Path.Combine(baseDir, rel.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(abs)) return null;

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(abs, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
