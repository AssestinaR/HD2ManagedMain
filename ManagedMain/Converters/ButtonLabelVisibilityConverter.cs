using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class ButtonLabelVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                if (values == null || values.Length < 2) return Visibility.Collapsed;
                double width = ToDouble(values[0]);
                string tag = values.Length >= 2 ? values[1]?.ToString() ?? "S" : "S";

                const double min = 36.0;
                double max = GetMaxWidth(tag);
                if (max <= 0) max = 84; // fallback
                double threshold = (max - min) / 2.0 + min;
                return width >= threshold ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { return Visibility.Collapsed; }
        }

        private static double ToDouble(object o)
        {
            try { return System.Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0.0; }
        }

        private static double GetMaxWidth(string tag)
        {
            try
            {
                var res = System.Windows.Application.Current?.Resources;
                switch (tag?.ToUpperInvariant())
                {
                    case "L": return res?["BtnWidthL"] is double dl ? dl : 150;
                    case "M": return res?["BtnWidthM"] is double dm ? dm : 112;
                    default: return res?["BtnWidthS"] is double ds ? ds : 84;
                }
            }
            catch { return 84; }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
