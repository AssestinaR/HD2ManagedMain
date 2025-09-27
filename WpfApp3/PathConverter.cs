using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using LiberTeaManager.Services;

namespace LiberTeaManager
{
    internal static class ImageCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static string MakeKey(string path, int width, long ticks) => path + "|" + width.ToString(CultureInfo.InvariantCulture) + "|" + ticks.ToString(CultureInfo.InvariantCulture);

        public static BitmapImage? LoadFile(string absPath, int decodePixelWidth)
        {
            try
            {
                if (!File.Exists(absPath)) return null;
                var info = new FileInfo(absPath);
                long ticks = info.Exists ? info.LastWriteTimeUtc.Ticks : 0;
                var key = MakeKey(absPath, decodePixelWidth, ticks);
                if (_cache.TryGetValue(key, out var cached)) return cached;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.UriSource = new Uri(absPath, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _cache[key] = bmp;
                return bmp;
            }
            catch { return null; }
        }

        public static BitmapImage? LoadResource(string packUri, int decodePixelWidth)
        {
            try
            {
                var key = MakeKey(packUri, decodePixelWidth, 0);
                if (_cache.TryGetValue(key, out var cached)) return cached;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                if (decodePixelWidth > 0) bmp.DecodePixelWidth = decodePixelWidth;
                bmp.UriSource = new Uri(packUri, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                _cache[key] = bmp;
                return bmp;
            }
            catch { return null; }
        }
    }

    public static class PathConverter
    {
        /// <summary>
        /// 获取指定mod的根文件夹绝对路径
        /// </summary>
        public static string GetModFolder(string modName)
        {
            var modFolder = SettingsContext.ModFolder;
            if (string.IsNullOrWhiteSpace(modFolder))
                modFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod");
            return Path.Combine(modFolder, modName);
        }

        /// <summary>
        /// 合并mod文件夹与相对路径，返回绝对路径
        /// </summary>
        public static string CombineModPath(string modName, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(modName) || string.IsNullOrWhiteSpace(relativePath))
                return string.Empty;
            return Path.Combine(GetModFolder(modName), relativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>
        /// 批量合并mod文件夹与路径列表，返回绝对路径列表
        /// </summary>
        public static List<string> CombineModPaths(string modName, IEnumerable<string> relativePaths)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(modName) || relativePaths == null)
                return result;
            foreach (var rel in relativePaths)
            {
                if (!string.IsNullOrWhiteSpace(rel))
                    result.Add(CombineModPath(modName, rel));
            }
            return result;
        }

        /// <summary>
        /// 获取mod的图片绝对路径（Image或IconPath字段）
        /// </summary>
        public static string GetModImagePath(MainModItem mod, string imageField)
        {
            if (mod == null || string.IsNullOrWhiteSpace(imageField))
                return string.Empty;
            var absPath = CombineModPath(mod.Name, imageField);
            return absPath;
        }

        /// <summary>
        /// 获取mod的文件组所有文件的绝对路径
        /// </summary>
        public static List<string> GetModFileGroupFiles(MainModItem mod, List<ModFileGroup> fileGroups)
        {
            var result = new List<string>();
            if (mod == null || fileGroups == null)
                return result;
            foreach (var group in fileGroups)
            {
                if (group?.Files != null)
                {
                    foreach (var rel in group.Files)
                    {
                        if (!string.IsNullOrWhiteSpace(rel))
                            result.Add(CombineModPath(mod.Name, rel));
                    }
                }
            }
            return result;
        }

        public static BitmapImage? LoadModBitmap(string modName, string imageField, int decodePixelWidth = 256)
        {
            var absPath = CombineModPath(modName, imageField);
            return ImageCache.LoadFile(absPath, decodePixelWidth);
        }
    }
    /// <summary>
    /// 将modlist中的Image/IconPath字段与mod文件夹路径合并为绝对路径并返回BitmapImage
    /// </summary>
    public class ModImageConverter : IValueConverter
    {
        public int DecodePixelWidth { get; set; } = 256;
        // parameter: mod名
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string imageField && !string.IsNullOrWhiteSpace(imageField) && parameter is string modName && !string.IsNullOrWhiteSpace(modName))
            {
                return PathConverter.LoadModBitmap(modName, imageField, DecodePixelWidth);
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// 将modlist中的Image/IconPath字段与mod文件夹路径合并为绝对路径并返回BitmapImage（支持多值转换）
    /// </summary>
    public class ModImageMultiConverter : IMultiValueConverter
    {
        public int DecodePixelWidth { get; set; } = 256;
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length >= 2 &&
                values[0] is string imageField && !string.IsNullOrWhiteSpace(imageField) &&
                values[1] is string modName && !string.IsNullOrWhiteSpace(modName))
            {
                return PathConverter.LoadModBitmap(modName, imageField, DecodePixelWidth);
            }
            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// 字符串(路径) -> 文件名 提取转换
    /// </summary>
    public class FileNameConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                return Path.GetFileName(s);
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    /// <summary>
    /// 带默认图标的modlist中Image/IconPath字段多值转换
    /// </summary>
    public class ModListImageMultiConverter : IMultiValueConverter
    {
        public int DecodePixelWidth { get; set; } = 128; // 列表缩略图更小
        private const string FallbackPackUri = "pack://application:,,,/helldivers2.png";
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            string? imageField = values.Length > 0 ? values[0] as string : null;
            string? modName = values.Length > 1 ? values[1] as string : null;
            BitmapImage? result = null;
            if (!string.IsNullOrWhiteSpace(imageField) && !string.IsNullOrWhiteSpace(modName))
            {
                var abs = PathConverter.CombineModPath(modName, imageField);
                result = ImageCache.LoadFile(abs, DecodePixelWidth);
            }
            if (result != null) return result;
            return ImageCache.LoadResource(FallbackPackUri, DecodePixelWidth) ?? null;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}