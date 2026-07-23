using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace HD2ModManager.Services
{
    // 作用：在后台生成并提供库列表使用的小尺寸图片缓存，避免 UI 线程读取原始大图。
    public static class ThumbnailService
    {
        private static readonly string CacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache", "thumbs");
        private static readonly object SyncRoot = new();
        private static CancellationTokenSource _generationCancellation = new();

        public static void PreGenerateAll(ModLibraryService library, int[] sizes)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var mods = library.All().ToList();
                var token = GetGenerationToken();
                _ = Task.Run(() =>
                {
                    foreach (var m in mods)
                    {
                        if (token.IsCancellationRequested) break;
                        var path = m.Image;
                        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                        foreach (var size in sizes)
                        {
                            if (token.IsCancellationRequested) break;
                            try { EnsureThumb(path!, size); } catch { }
                        }
                    }
                });
            }
            catch { }
        }

        public static void CancelPendingGeneration()
        {
            lock (SyncRoot)
            {
                _generationCancellation.Cancel();
                _generationCancellation.Dispose();
                _generationCancellation = new CancellationTokenSource();
            }
        }

        public static string? GetExistingThumbnailPath(string? originalPath, int decode)
        {
            if (string.IsNullOrWhiteSpace(originalPath) || !File.Exists(originalPath)) return null;
            var thumbnail = GetThumbPath(originalPath, decode);
            return File.Exists(thumbnail) ? thumbnail : null;
        }

        public static Task<bool> EnsureThumbnailsAsync(IEnumerable<string?> imagePaths, int decode, CancellationToken cancellationToken = default)
        {
            var paths = imagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                .Select(path => path!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return Task.Run(() =>
            {
                var generated = false;
                foreach (var path in paths)
                {
                    if (cancellationToken.IsCancellationRequested) return generated;
                    var thumbnail = GetThumbPath(path, decode);
                    if (File.Exists(thumbnail)) continue;
                    EnsureThumb(path, decode);
                    generated = true;
                }
                return generated;
            }, cancellationToken);
        }

        public static Task RegenerateThumbnailAsync(string? imagePath, int decode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath)) return Task.CompletedTask;

            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thumbnail = GetThumbPath(imagePath, decode);
                if (File.Exists(thumbnail)) File.Delete(thumbnail);
                cancellationToken.ThrowIfCancellationRequested();
                EnsureThumb(imagePath, decode);
            }, cancellationToken);
        }

        private static void EnsureThumb(string originalPath, int decode)
        {
            lock (SyncRoot)
            {
                var thumb = GetThumbPath(originalPath, decode);
                if (File.Exists(thumb)) return;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                using (var input = new FileStream(originalPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bmp.StreamSource = input;
                    if (decode > 0) bmp.DecodePixelWidth = decode;
                    bmp.EndInit();
                }

                bmp.Freeze();
                using var fs = new FileStream(thumb, FileMode.Create, FileAccess.Write, FileShare.None);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bmp));
                encoder.Save(fs);
            }
        }

        private static CancellationToken GetGenerationToken()
        {
            lock (SyncRoot)
            {
                return _generationCancellation.Token;
            }
        }

        private static string GetThumbPath(string originalPath, int decode)
        {
            string abs;
            try { abs = Path.GetFullPath(originalPath); } catch { abs = originalPath; }
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(abs);
            var hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
            var ext = ".png";
            var sizeTag = decode > 0 ? $"_{decode}" : "";
            Directory.CreateDirectory(CacheDir);
            return Path.Combine(CacheDir, hash + sizeTag + ext);
        }
    }
}
