using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ManagedMain.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Text.RegularExpressions;
using System.Diagnostics; // logging

namespace ManagedMain.Services
{
    public class ImportService
    {
        private readonly ILogService? _log; // added logging dependency (can be null)
        public ImportService(ILogService? log = null) { _log = log; }
        // Centralized temp root (ManagedMain only). Old random GUID pattern replaced.
        private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "ManagedMain_Temp");
        private const string MarkerName = ".mmtemp"; // marker for age based cleanup
        private static void EnsureTempRoot()
        {
            try { if (!Directory.Exists(TempRoot)) Directory.CreateDirectory(TempRoot); }
            catch (Exception ex) { Debug.WriteLine("[ImportService] 创建临时根目录失败: " + ex.Message); }
        }
        private static string ShortRand()
        {
            var s = Path.GetRandomFileName().Replace(".", string.Empty);
            return s.Length <= 4 ? s : s[..4];
        }
        private static string NewTempDir(string prefix)
        {
            EnsureTempRoot();
            var dir = Path.Combine(TempRoot, prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss") + "_" + ShortRand());
            try { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, MarkerName), DateTime.UtcNow.ToString("O")); }
            catch (Exception ex) { Debug.WriteLine("[ImportService] 创建临时目录失败: " + ex.Message); }
            return dir;
        }
        private static void CleanupOldTempDirs(TimeSpan? maxAge = null)
        {
            try
            {
                EnsureTempRoot();
                var age = maxAge ?? TimeSpan.FromHours(2);
                foreach (var d in Directory.EnumerateDirectories(TempRoot))
                {
                    try
                    {
                        var marker = Path.Combine(d, MarkerName);
                        DateTime ts = Directory.GetCreationTimeUtc(d);
                        if (File.Exists(marker))
                        {
                            if (DateTime.TryParse(File.ReadAllText(marker), out var parsed)) ts = parsed.ToUniversalTime();
                        }
                        if (DateTime.UtcNow - ts > age)
                        {
                            Directory.Delete(d, true);
                        }
                    }
                    catch (Exception exDel) { Debug.WriteLine("[ImportService] 清理临时目录失败: " + exDel.Message); }
                }
            }
            catch (Exception ex) { Debug.WriteLine("[ImportService] CleanupOldTempDirs 失败: " + ex.Message); }
        }

        public MainModItem ImportFolderAsMod(string profileRoot, string folderPath)
        {
            var modName = new DirectoryInfo(folderPath).Name;
            var target = Path.Combine(profileRoot, modName);
            if (!string.Equals(Path.GetFullPath(folderPath).TrimEnd('\\','/'), Path.GetFullPath(target).TrimEnd('\\','/'), StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectoryRecursive(folderPath, target);
            }
            var manifest = Path.Combine(target, "manifest.json");
            if (File.Exists(manifest))
            {
                try { return BuildFromManifest(target, manifest); } catch (Exception ex) { Debug.WriteLine("[ImportService] 读取 manifest 失败: " + ex.Message); }
            }
            return FileGroupScanner.BuildModFromDirectory(modName, target);
        }

        /// <summary>
        /// SAFETY: 仅按白名单解压资源文件 (manifest / patch / 图片 / json / txt)。不执行文件，不写入除目标 mod 目录以外的位置。
        /// 修改点 (Option 1): 白名单 + staging + 延迟清理 + 日志锚点。
        /// </summary>
        public MainModItem ImportArchiveAsMod(string profileRoot, string archivePath)
        {
            CleanupOldTempDirs();
            var staging = NewTempDir("import_");
            int totalEntries = 0, kept = 0, skipped = 0;
            var allowedImageExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg" };
            var allowedOtherExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".json", ".txt" };
            var patchRegex = new Regex(@"^[a-fA-F0-9]{16}\.patch_\d+(?:\.stream|\.gpu_resources)?$", RegexOptions.Compiled);
            _log?.Log($"IMPORT_START archive={Path.GetFileName(archivePath)}");

            try
            {
                using (var archive = ArchiveFactory.Open(archivePath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue; totalEntries++;
                        var key = entry.Key.Replace('\\', '/');
                        var fileName = Path.GetFileName(key);
                        bool allow = false;
                        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) allow = true;
                        else if (patchRegex.IsMatch(fileName)) allow = true;
                        else
                        {
                            var ext = Path.GetExtension(fileName);
                            if (allowedImageExt.Contains(ext) || allowedOtherExt.Contains(ext)) allow = true;
                        }
                        if (!allow) { skipped++; continue; }
                        try
                        {
                            var outPath = Path.Combine(staging, key);
                            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                            entry.WriteToFile(outPath, new ExtractionOptions { ExtractFullPath = false, Overwrite = true });
                            kept++;
                        }
                        catch (Exception exEntry)
                        {
                            skipped++; Debug.WriteLine("[ImportService] 解压文件失败: " + key + " => " + exEntry.Message);
                        }
                    }
                }

                // Determine effective mod root inside staging
                string? modFolder = null;
                var manifestPath = Directory.GetFiles(staging, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
                if (!string.IsNullOrEmpty(manifestPath))
                    modFolder = Path.GetDirectoryName(manifestPath);
                else
                {
                    var topDirs = Directory.GetDirectories(staging, "*", SearchOption.TopDirectoryOnly);
                    modFolder = topDirs.Length == 1 ? topDirs[0] : staging; // fallback to staging root
                }

                string archiveBaseName = Path.GetFileNameWithoutExtension(archivePath) ?? "ImportedMod";
                string folderBaseName = new DirectoryInfo(modFolder!).Name;
                string finalName = folderBaseName;

                // Try manifest Name first
                try
                {
                    if (!string.IsNullOrEmpty(manifestPath))
                    {
                        var json = File.ReadAllText(manifestPath!);
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("Name", out var n))
                        {
                            var s = n.GetString();
                            if (!string.IsNullOrWhiteSpace(s)) finalName = s!;
                        }
                    }
                }
                catch (Exception ex) { Debug.WriteLine("[ImportService] 解析 manifest 名称失败: " + ex.Message); }

                // If manifest name missing or looks temporary, use archive base name
                if (string.IsNullOrWhiteSpace(finalName) || LooksLikeHashedOrTemp(finalName))
                    finalName = LooksLikeHashedOrTemp(folderBaseName) ? archiveBaseName : folderBaseName;

                finalName = SanitizeFileName(finalName);
                var target = EnsureUniqueDirectory(Path.Combine(profileRoot, finalName));

                // Move (prefer) to avoid second copy; if cross-volume fallback to copy
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Directory.Move(modFolder!, target);
                }
                catch (Exception exMove)
                {
                    Debug.WriteLine("[ImportService] 目录移动失败，改为复制: " + exMove.Message);
                    CopyDirectoryRecursive(modFolder!, target);
                }

                // Build model (manifest or directory) in final location
                MainModItem result;
                var manifestInTarget = Path.Combine(target, "manifest.json");
                if (File.Exists(manifestInTarget))
                {
                    try { result = BuildFromManifest(target, manifestInTarget); }
                    catch (Exception exB) { Debug.WriteLine("[ImportService] BuildFromManifest 失败: " + exB.Message); result = FileGroupScanner.BuildModFromDirectory(new DirectoryInfo(target).Name, target); }
                }
                else
                {
                    result = FileGroupScanner.BuildModFromDirectory(new DirectoryInfo(target).Name, target);
                }
                _log?.Log($"IMPORT_DONE archive={Path.GetFileName(archivePath)} name={result.Name} kept={kept} skipped={skipped} total={totalEntries} stagingKept=true");
                return result;
            }
            catch (Exception ex)
            {
                _log?.Log($"IMPORT_DONE archive={Path.GetFileName(archivePath)} error={ex.Message} kept={kept} skipped={skipped} total={totalEntries}");
                throw;
            }
        }

        private static bool LooksLikeHashedOrTemp(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return true;
            var n = name.Trim();
            if (Guid.TryParse(n, out _)) return true;
            if (Regex.IsMatch(n, "^[A-Fa-f0-9]{16,}$")) return true;
            if (n.StartsWith("ManagedMain_", StringComparison.OrdinalIgnoreCase)) return true;
            // consider our staging/temp prefixes as temporary names
            if (n.StartsWith("import_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("export_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("unzip_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("temp_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("tmp_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("staging_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("copy_", StringComparison.OrdinalIgnoreCase)) return true;
            if (n.StartsWith("single_", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "ImportedMod";
            return name;
        }

        private static string EnsureUniqueDirectory(string targetPath)
        {
            var dir = Path.GetDirectoryName(targetPath) ?? ".";
            var baseName = Path.GetFileName(targetPath);
            string candidate = targetPath; int i = 1;
            while (Directory.Exists(candidate))
            {
                candidate = Path.Combine(dir, $"{baseName}_{i}"); i++;
            }
            return candidate;
        }

        private static MainModItem BuildFromManifest(string targetRoot, string manifestPath)
        {
            var json = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var main = new MainModItem
            {
                Name = root.TryGetProperty("Name", out var rname) ? rname.GetString() ?? new DirectoryInfo(targetRoot).Name : new DirectoryInfo(targetRoot).Name,
                Description = root.TryGetProperty("Description", out var rdesc) ? rdesc.GetString() : null,
                IconPath = root.TryGetProperty("IconPath", out var rico) ? rico.GetString() : null,
                OptionsSingleSelect = root.TryGetProperty("OptionsSingleSelect", out var oss) && oss.ValueKind == JsonValueKind.True,
            };
            if (string.IsNullOrWhiteSpace(main.Image) && !string.IsNullOrWhiteSpace(main.IconPath)) main.Image = main.IconPath;
            main.FileGroups = FileGroupScanner.GetModFileGroups(targetRoot, targetRoot);
            if (root.TryGetProperty("Options", out var ropts) && ropts.ValueKind == JsonValueKind.Array)
            {
                foreach (var optEl in ropts.EnumerateArray())
                {
                    var opt = new OptionItem
                    {
                        Name = optEl.TryGetProperty("Name", out var on) ? on.GetString() ?? "Option" : "Option",
                        Description = optEl.TryGetProperty("Description", out var od) ? od.GetString() : null,
                        Image = optEl.TryGetProperty("Image", out var oi) ? oi.GetString() : null,
                        SubOptionsSingleSelect = optEl.TryGetProperty("SubOptionsSingleSelect", out var sss) && sss.ValueKind == JsonValueKind.True,
                    };
                    if (optEl.TryGetProperty("Include", out var oinc) && oinc.ValueKind == JsonValueKind.Array)
                    {
                        var collected = new List<ModFileGroup>();
                        foreach (var inc in oinc.EnumerateArray())
                        {
                            var rel = inc.GetString();
                            if (string.IsNullOrWhiteSpace(rel)) continue;
                            var dirInc = Path.Combine(targetRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                            if (Directory.Exists(dirInc)) collected.AddRange(FileGroupScanner.GetModFileGroups(targetRoot, dirInc));
                        }
                        opt.FileGroups = collected;
                    }
                    if (optEl.TryGetProperty("SubOptions", out var subs) && subs.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var subEl in subs.EnumerateArray())
                        {
                            var sub = new SubOptionItem
                            {
                                Name = subEl.TryGetProperty("Name", out var sn) ? sn.GetString() ?? "SubOption" : "SubOption",
                                Description = subEl.TryGetProperty("Description", out var sd) ? sd.GetString() : null,
                                Image = subEl.TryGetProperty("Image", out var si) ? si.GetString() : null,
                            };
                            if (subEl.TryGetProperty("Include", out var sinc) && sinc.ValueKind == JsonValueKind.Array)
                            {
                                var collected = new List<ModFileGroup>();
                                foreach (var inc in sinc.EnumerateArray())
                                {
                                    var rel = inc.GetString();
                                    if (string.IsNullOrWhiteSpace(rel)) continue;
                                    var dirInc = Path.Combine(targetRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                                    if (Directory.Exists(dirInc)) collected.AddRange(FileGroupScanner.GetModFileGroups(targetRoot, dirInc));
                                }
                                sub.FileGroups = collected;
                            }
                            opt.SubOptions.Add(sub);
                        }
                    }
                    main.Options.Add(opt);
                }
            }
            return main;
        }

        private static void CopyDirectoryRecursive(string source, string dest)
        {
            var srcFull = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var dstFull = Path.GetFullPath(dest).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (dstFull.StartsWith(srcFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
            {
                var alt = Path.Combine(Path.GetDirectoryName(dstFull) ?? dstFull, "copy_" + ShortRand());
                CopyDirectoryRecursiveSafe(srcFull, alt);
                SafeMove(alt, dstFull);
                return;
            }
            CopyDirectoryRecursiveSafe(srcFull, dstFull);
        }

        private static void CopyDirectoryRecursiveSafe(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
            {
                var destFile = Path.Combine(dest, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                var name = Path.GetFileName(dir);
                var destSub = Path.Combine(dest, name);
                CopyDirectoryRecursiveSafe(dir, destSub);
            }
        }

        private static void SafeMove(string source, string dest)
        {
            if (Directory.Exists(dest)) Directory.Delete(dest, true);
            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            Directory.Move(source, dest);
        }
    }
}
