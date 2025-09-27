using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using ManagedMain.Models;
using ManagedMain.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ManagedMain.Converters
{
    /// <summary>
    /// 根据游戏目录中是否存在对应 HexPrefix.patch_* 文件，返回启用高亮画刷；若未启用返回 null 使用默认前景。
    /// 为避免阻塞 UI，目录扫描在后台进行并使用缓存。
    /// </summary>
    public class ModFileGroupEnabledBrushConverter : IValueConverter
    {
        private static readonly Regex FileNamePattern = new Regex(
            @"^(?<hex>[a-fA-F0-9]{16})\.patch_\d+(?:\.stream|\.gpu_resources)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try
            {
                // 解析 hex 前缀
                string? hex = null;
                if (value is ModFileGroup g) hex = g.HexPrefix;
                else if (value is string s && s.Length == 16) hex = s;
                if (string.IsNullOrWhiteSpace(hex)) return null;

                var gameFolder = new OptionStore().LoadOrCreate().GameFolder;
                if (string.IsNullOrWhiteSpace(gameFolder) || !System.IO.Directory.Exists(gameFolder)) return null;

                // 异步确保缓存刷新，避免在 UI 线程枚举磁盘
                GamePatchHexCache.EnsureScan(gameFolder);

                // 直接从缓存读取是否存在
                if (GamePatchHexCache.IsHexPresent(hex))
                {
                    return System.Windows.Media.Brushes.ForestGreen;
                }
            }
            catch { }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static class GamePatchHexCache
        {
            private static readonly object _gate = new object();
            private static string? _folder;
            private static HashSet<string> _hexes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            private static System.DateTime _lastScanUtc = System.DateTime.MinValue;
            private static bool _scanning;
            private static readonly System.TimeSpan ScanCooldown = System.TimeSpan.FromSeconds(5);

            public static bool IsHexPresent(string hex)
            {
                lock (_gate) { return _hexes.Contains(hex); }
            }

            public static void EnsureScan(string folder)
            {
                bool needStart = false;
                lock (_gate)
                {
                    if (!System.StringComparer.OrdinalIgnoreCase.Equals(_folder, folder))
                    {
                        _folder = folder;
                        _hexes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                        _lastScanUtc = System.DateTime.MinValue;
                    }
                    if (!_scanning && (System.DateTime.UtcNow - _lastScanUtc) > ScanCooldown)
                    {
                        _scanning = true;
                        needStart = true;
                    }
                }
                if (needStart)
                {
                    _ = System.Threading.Tasks.Task.Run(() => Scan(folder));
                }
            }

            private static void Scan(string folder)
            {
                try
                {
                    var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                    foreach (var path in System.IO.Directory.EnumerateFiles(folder, "*.patch_*", System.IO.SearchOption.TopDirectoryOnly))
                    {
                        var name = System.IO.Path.GetFileName(path);
                        var m = FileNamePattern.Match(name);
                        if (m.Success)
                        {
                            var hex = m.Groups["hex"].Value;
                            if (!string.IsNullOrWhiteSpace(hex)) set.Add(hex);
                        }
                    }
                    lock (_gate)
                    {
                        _hexes = set;
                        _lastScanUtc = System.DateTime.UtcNow;
                    }
                }
                catch
                {
                    lock (_gate)
                    {
                        _hexes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                        _lastScanUtc = System.DateTime.UtcNow;
                    }
                }
                finally
                {
                    lock (_gate) { _scanning = false; }
                }
            }
        }
    }
}
