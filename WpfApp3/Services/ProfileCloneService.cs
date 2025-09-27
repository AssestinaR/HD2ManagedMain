using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiberTeaManager.Services
{
    internal static class ProfileCloneService
    {
        private static readonly Regex PatchRegex = new Regex(@"^[a-fA-F0-9]{16}\.patch_\d+(?:\.(?:stream|gpu_resources))?$", RegexOptions.Compiled);

        public static void CloneModRoot(string sourceRoot, string targetRoot, Action<string>? log = null, Action<int,int>? progress = null)
        {
            if (string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(targetRoot)) return;
            if (!Directory.Exists(sourceRoot)) { log?.Invoke("源Mod目录不存在: " + sourceRoot); return; }
            Directory.CreateDirectory(targetRoot);
            bool sameVolume = string.Equals(Path.GetPathRoot(Path.GetFullPath(sourceRoot)), Path.GetPathRoot(Path.GetFullPath(targetRoot)), StringComparison.OrdinalIgnoreCase);

            var allDirs = Directory.GetDirectories(sourceRoot, "*", SearchOption.AllDirectories);
            foreach (var dir in allDirs)
            {
                var rel = Path.GetRelativePath(sourceRoot, dir);
                Directory.CreateDirectory(Path.Combine(targetRoot, rel));
            }

            var allFiles = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            int total = allFiles.Length;
            int index = 0;
            foreach (var file in allFiles)
            {
                index++;
                var rel = Path.GetRelativePath(sourceRoot, file);
                if (string.Equals(rel, "modlist.json", StringComparison.OrdinalIgnoreCase) || string.Equals(rel, "Option.json", StringComparison.OrdinalIgnoreCase))
                { progress?.Invoke(index,total); continue; }
                var dest = Path.Combine(targetRoot, rel);
                try
                {
                    if (IsPatchFile(file) && sameVolume)
                    {
                        if (TryCreateHardLink(dest, file)) { log?.Invoke("硬链接: " + rel); progress?.Invoke(index,total); continue; }
                    }
                    File.Copy(file, dest, overwrite: false); log?.Invoke("复制: " + rel);
                }
                catch (Exception ex) { log?.Invoke("复制失败: " + rel + " => " + ex.Message); }
                progress?.Invoke(index,total);
            }
        }

        public static Task CloneModRootAsync(string sourceRoot, string targetRoot, Action<string>? log = null, Action<int,int>? progress = null)
            => Task.Run(() => CloneModRoot(sourceRoot, targetRoot, log, progress));

        private static bool IsPatchFile(string path)
        {
            var name = Path.GetFileName(path) ?? string.Empty;
            if (PatchRegex.IsMatch(name)) return true;
            // 兼容包含 .patch_ 的文件名
            return name.Contains(".patch_");
        }

        private static bool TryCreateHardLink(string dest, string src)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest)) File.Delete(dest);
                if (CreateHardLink(dest, src, IntPtr.Zero)) return true;
            }
            catch { }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("Kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
    }
}
