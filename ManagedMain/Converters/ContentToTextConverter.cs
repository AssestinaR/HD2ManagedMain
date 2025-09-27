using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace ManagedMain.Converters
{
    public sealed class ContentToTextConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value == null) return null;
            // Direct string
            if (value is string s) return s;
            // TextBlock with Text/Inlines
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
            // Other common content types: return ToString()
            return value.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
