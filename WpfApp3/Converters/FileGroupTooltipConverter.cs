using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media;

namespace LiberTeaManager
{
    public class FileGroupDisplay
    {
        public string Text { get; set; } = string.Empty;
        public bool Enabled { get; set; }
    }

    public class FileGroupTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string mode = parameter as string ?? "Own";
            var list = new List<FileGroupDisplay>();
            switch (value)
            {
                case MainModItem m:
                    if (mode.Equals("Own", StringComparison.OrdinalIgnoreCase) || mode.Equals("Total", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var g in m.FileGroups)
                            list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = m.Enabled == EnabledState.Enabled });
                    }
                    if (mode.Equals("Total", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var opt in m.Options)
                        {
                            foreach (var g in opt.FileGroups)
                                list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = opt.Enabled == EnabledState.Enabled });
                            foreach (var sub in opt.SubOptions)
                                foreach (var g in sub.FileGroups)
                                    list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = sub.Enabled == EnabledState.Enabled });
                        }
                    }
                    break;
                case OptionItem o:
                    if (mode.Equals("Own", StringComparison.OrdinalIgnoreCase) || mode.Equals("Total", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var g in o.FileGroups)
                            list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = o.Enabled == EnabledState.Enabled });
                    }
                    if (mode.Equals("Total", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var sub in o.SubOptions)
                            foreach (var g in sub.FileGroups)
                                list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = sub.Enabled == EnabledState.Enabled });
                    }
                    break;
                case SubOptionItem s:
                    foreach (var g in s.FileGroups)
                        list.Add(new FileGroupDisplay { Text = $"{g.HexPrefix}.patch_{g.PatchN}", Enabled = s.Enabled == EnabledState.Enabled });
                    break;
            }
            return list;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public Brush EnabledBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0x00, 0x80, 0x00));
        public Brush DisabledBrush { get; set; } = Brushes.Black;
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        { bool b = value is bool vb && vb; return b ? EnabledBrush : DisabledBrush; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
