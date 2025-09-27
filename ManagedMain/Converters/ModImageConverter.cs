using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using ManagedMain.Models;
using ManagedMain.ViewModels;

namespace ManagedMain.Converters
{
    public class ModImageConverter : IMultiValueConverter
    {
        public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // Support both 3-value (rel, ctx, item) and 4-value (image, iconPath, ctx, item)
                string? relPrimary = values.Length > 0 ? values[0] as string : null;
                string? relAlt = values.Length > 1 && values.Length >= 4 ? values[1] as string : null; // alt only when 4 args present

                object? ctx;
                object? item;
                if (values.Length >= 4)
                {
                    ctx = values[2];
                    item = values[3];
                }
                else
                {
                    ctx = values.Length > 1 ? values[1] : null;
                    item = values.Length > 2 ? values[2] : null;
                }

                string DefaultPack() => "pack://application:,,,/ManagedMain;component/helldivers2.png";

                BitmapImage LoadBitmap(string uriOrPath, bool isAbsolutePath)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = isAbsolutePath ? new Uri(uriOrPath, UriKind.Absolute) : new Uri(uriOrPath, UriKind.RelativeOrAbsolute);
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }

                // Prefer explicit Image, then IconPath
                string? candidateRel = !string.IsNullOrWhiteSpace(relPrimary) ? relPrimary : (!string.IsNullOrWhiteSpace(relAlt) ? relAlt : null);

                // If looks like an absolute file path, load directly
                if (!string.IsNullOrWhiteSpace(candidateRel) && Path.IsPathRooted(candidateRel!) && File.Exists(candidateRel!))
                {
                    return LoadBitmap(candidateRel!, true);
                }

                // Determine profile/context
                string profileRoot = string.Empty;
                System.Collections.ObjectModel.ObservableCollection<MainModItem>? mods = null;
                switch (ctx)
                {
                    case ProfileModsViewModel vm:
                        profileRoot = vm.Profile.RootPath;
                        mods = vm.Mods;
                        break;
                    case ProfileEntry p:
                        profileRoot = p.RootPath;
                        mods = p.Mods;
                        break;
                }
                if (string.IsNullOrWhiteSpace(profileRoot) || mods == null || item == null)
                {
                    return LoadBitmap(DefaultPack(), false);
                }

                // Determine main mod name for base directory
                string mainName = item switch
                {
                    MainModItem m => m.Name,
                    OptionItem o => mods.FirstOrDefault(mm => mm.Options.Contains(o))?.Name ?? string.Empty,
                    SubOptionItem s => (
                        from mm in mods
                        from oo in mm.Options
                        where oo.SubOptions.Contains(s)
                        select mm.Name).FirstOrDefault() ?? string.Empty,
                    _ => string.Empty
                };
                if (string.IsNullOrEmpty(mainName)) return LoadBitmap(DefaultPack(), false);

                // If still empty, fallback to object's own fields
                if (string.IsNullOrWhiteSpace(candidateRel))
                {
                    switch (item)
                    {
                        case MainModItem m:
                            candidateRel = !string.IsNullOrWhiteSpace(m.Image) ? m.Image : m.IconPath;
                            break;
                        case OptionItem o:
                            candidateRel = !string.IsNullOrWhiteSpace(o.Image) ? o.Image : o.IconPath;
                            break;
                        case SubOptionItem s:
                            candidateRel = !string.IsNullOrWhiteSpace(s.Image) ? s.Image : s.IconPath;
                            break;
                    }
                }

                if (string.IsNullOrWhiteSpace(candidateRel))
                {
                    return LoadBitmap(DefaultPack(), false);
                }

                // Try new layout first
                var absNew = Path.Combine(Path.Combine(profileRoot, mainName), candidateRel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absNew)) return LoadBitmap(absNew, true);

                // Try old layout fallback
                var absOld = Path.Combine(Path.Combine(profileRoot, "mod", mainName), candidateRel.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(absOld)) return LoadBitmap(absOld, true);

                return LoadBitmap(DefaultPack(), false);
            }
            catch
            {
                try { return new BitmapImage(new Uri("pack://application:,,,/ManagedMain;component/helldivers2.png", UriKind.Absolute)); }
                catch { return null; }
            }
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
