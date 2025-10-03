using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace ManagedMain.Converters
{
    // Extracts leading token (emoji/icon) before first space from a content object
    public class LeadingTokenConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = ExtractText(value);
            if (string.IsNullOrWhiteSpace(s)) return string.Empty;
            int idx = s.IndexOf(' ');
            return idx > 0 ? s.Substring(0, idx) : s;
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
