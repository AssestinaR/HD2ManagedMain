using System;
using System.Globalization;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace ManagedMain.Converters
{
    public class LabelTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = ExtractText(value);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            int idx = s.IndexOf(' ');
            if (idx < 0) return string.Empty; // only icon
            return s.Substring(idx + 1).TrimStart();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private static string ExtractText(object value)
        {
            if (value is string str) return str;
            if (value is TextBlock tb)
            {
                if (!string.IsNullOrWhiteSpace(tb.Text)) return tb.Text;
                var sb = new StringBuilder();
                foreach (var inline in tb.Inlines)
                {
                    if (inline is Run r) sb.Append(r.Text);
                }
                return sb.ToString();
            }
            return value?.ToString() ?? string.Empty;
        }
    }
}
