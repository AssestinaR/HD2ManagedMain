using System;
using System.Globalization;
using System.Windows.Data;

namespace ManagedMain.Converters
{
    public class BoolToChoiceTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                bool single = value is bool b && b;
                return single ? ManagedMain.Resources.Strings.SR_Choice_Single : ManagedMain.Resources.Strings.SR_Choice_Multiple;
            }
            catch { return ManagedMain.Resources.Strings.SR_Choice_Multiple; }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
