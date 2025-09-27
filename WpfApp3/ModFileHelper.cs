using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using SharpCompress.Archives;
using SharpCompress.Common;
using System.Threading.Tasks;
using LiberTeaManager.Services;
using System.Text.RegularExpressions;

namespace LiberTeaManager
{
    public static class ModFileHelper
    {
        public static Action<string>? AppendLog;

        private static void EnsureModRoot()
        {
            if (string.IsNullOrWhiteSpace(SettingsContext.ModFolder)) { }
            if (!Directory.Exists(SettingsContext.ModFolder)) Directory.CreateDirectory(SettingsContext.ModFolder);
        }

        private static string NewTempDir(string prefix)
        {
            EnsureModRoot();
            var dir = Path.Combine(SettingsContext.ModFolder, prefix + Guid.NewGuid());
            Directory.CreateDirectory(dir);
            return dir;
        }

        // 同步包装
        public static void ExportMods(IEnumerable<MainModItem> mods, string exportFolder, int version = 1)
            => ExportModsAsync(mods, exportFolder, version).GetAwaiter().GetResult();

        public static async Task ExportModsAsync(IEnumerable<MainModItem> mods, string exportFolder, int version = 1)
        {
            EnsureModRoot();
            Directory.CreateDirectory(exportFolder);
            var list = mods.ToList();
            if (list.Count == 0) { AppendUiLog("未选择任何 Mod 进行导出。"); return; }
            AppendUiLog($"开始导出 {list.Count} 个 Mod ...");
            foreach (var mod in list)
            {
                await Task.Run(() => ExportSingleMinimal(mod, exportFolder, version));
            }
            AppendUiLog("全部导出完成。");
        }

        private static void ExportSingleMinimal(MainModItem mod, string exportFolder, int version)
        {
            try
            {
                string modSrc = Path.Combine(SettingsContext.ModFolder, mod.Name);
                if (!Directory.Exists(modSrc)) { AppendUiLog($"源文件夹不存在: {mod.Name}"); return; }
                string tempDir = NewTempDir("_export_");
                CopyDirectory(modSrc, tempDir);

                var manifestObj = BuildMinimalManifest(mod, version);
                var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(Path.Combine(tempDir, "manifest.json"), JsonSerializer.Serialize(manifestObj, jsonOptions));

                string zipPath = Path.Combine(exportFolder, mod.Name + ".zip");
                if (File.Exists(zipPath)) File.Delete(zipPath);
                ZipFile.CreateFromDirectory(tempDir, zipPath, CompressionLevel.Optimal, false);
                AppendUiLog($"已导出: {mod.Name}");
                try { Directory.Delete(tempDir, true); } catch { }
            }
            catch (Exception ex)
            {
                AppendUiLog($"导出失败 {mod.Name}: {ex.Message}");
            }
        }

        private static ExportMain BuildMinimalManifest(MainModItem mod, int version)
        {
            return new ExportMain
            {
                Version = version,
                Guid = mod.Guid,
                Name = mod.Name,
                Description = mod.Description ?? string.Empty,
                IconPath = mod.IconPath ?? string.Empty,
                Url = mod.Url ?? string.Empty,
                Options = mod.Options?.Select(o => new ExportOption
                {
                    Name = o.Name,
                    Description = o.Description ?? string.Empty,
                    Include = (o.Include != null && o.Include.Count > 0) ? new List<string>(o.Include) : new List<string>(),
                    Image = o.Image ?? string.Empty,
                    Url = o.Url ?? string.Empty,
                    SubOptions = o.SubOptions?.Select(s => new ExportSub
                    {
                        Name = s.Name,
                        Description = s.Description ?? string.Empty,
                        Include = (s.Include != null && s.Include.Count > 0) ? new List<string>(s.Include) : new List<string>(),
                        Image = s.Image ?? string.Empty,
                        Url = s.Url ?? string.Empty
                    }).ToList() ?? new List<ExportSub>()
                }).ToList() ?? new List<ExportOption>()
            };
        }

        private class ExportMain
        {
            public int Version { get; set; }
            public Guid Guid { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string IconPath { get; set; }
            public string Url { get; set; }
            public List<ExportOption> Options { get; set; }
        }
        private class ExportOption
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<string> Include { get; set; }
            public string Image { get; set; }
            public string Url { get; set; }
            public List<ExportSub> SubOptions { get; set; }
        }
        private class ExportSub
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<string> Include { get; set; }
            public string Image { get; set; }
            public string Url { get; set; }
        }

        public static List<MainModItem> GetSelectedMods(IEnumerable<MainModItem> mods) => mods.Where(m => m.IsSelected).ToList();

        public static string DecompressToTemp(string zipPath)
        {
            string tempDir = NewTempDir("_temp_");
            using (var archive = ArchiveFactory.Open(zipPath))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory)
                    {
                        string fileOut = Path.Combine(tempDir, entry.Key);
                        Directory.CreateDirectory(Path.GetDirectoryName(fileOut)!);
                        entry.WriteToFile(fileOut);
                    }
                }
            }
            return tempDir;
        }

        private static readonly string[] ImageExts = new[] { ".png", ".jpg", ".jpeg" };
        public sealed class DecompressResult
        {
            public string TempDir { get; init; } = string.Empty;
            public List<string> ExtractedFiles { get; init; } = new();
        }

        public static DecompressResult DecompressToTempFiltered(string archivePath)
        {
            string tempDir = NewTempDir("_temp_");
            var list = new List<string>();
            try
            {
                using var archive = SharpCompress.Archives.ArchiveFactory.Open(archivePath);
                var patchRegex = new Regex(@"[a-fA-F0-9]{16}\.patch_\d+", RegexOptions.Compiled);
                foreach (var entry in archive.Entries)
                {
                    if (entry.IsDirectory) continue;
                    var key = entry.Key.Replace('\\','/');
                    var fileName = Path.GetFileName(key);
                    bool need = false;
                    if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) need = true;
                    else if (patchRegex.IsMatch(fileName)) need = true;
                    else if (ImageExts.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))) need = true;
                    if (!need) continue;
                    var outPath = Path.Combine(tempDir, key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    try { entry.WriteToFile(outPath); list.Add(Path.GetRelativePath(tempDir, outPath).Replace('\\','/')); } catch { }
                }
            }
            catch { }
            return new DecompressResult { TempDir = tempDir, ExtractedFiles = list };
        }

        public static MainModItem BuildModFromExtracted(string modName, string tempDir, IList<string> extractedRelFiles)
        {
            var main = new MainModItem
            {
                Name = modName,
                Guid = Guid.NewGuid(),
                Description = string.Empty,
                Options = new System.Collections.ObjectModel.ObservableCollection<OptionItem>(),
                FileGroups = new List<ModFileGroup>(),
                RootModName = modName
            };
            // 根图片
            var rootImages = extractedRelFiles.Where(f => f.IndexOf('/') < 0 && ImageExts.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).Select(Path.GetFileName).Where(f => f != null).ToList();
            if (rootImages.Count > 0)
            {
                // 偏好匹配
                string[] prefs = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                var picked = rootImages.FirstOrDefault(f => prefs.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase))) ?? rootImages.First();
                main.IconPath = picked!; main.Image = picked!;
            }

            var optionMap = new Dictionary<string, OptionItem>(StringComparer.OrdinalIgnoreCase);
            var subMap = new Dictionary<string, SubOptionItem>(StringComparer.OrdinalIgnoreCase);

            var patchRegex = new Regex(@"^([a-fA-F0-9]{16})\.patch_(\d+)(?:\.|$)");
            foreach (var rel in extractedRelFiles)
            {
                var fileName = Path.GetFileName(rel);
                if (ImageExts.Any(ext => fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                {
                    // 处理选项/子选项图片
                    var dir = Path.GetDirectoryName(rel)?.Replace('\\','/') ?? string.Empty;
                    if (!string.IsNullOrEmpty(dir))
                    {
                        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 1)
                        {
                            var optName = parts[0];
                            if (!optionMap.TryGetValue(optName, out var opt))
                            {
                                opt = new OptionItem { Name = optName, Description = string.Empty, SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(), FileGroups = new List<ModFileGroup>(), Include = new List<string> { optName }, RootModName = modName };
                                optionMap[optName] = opt; main.Options.Add(opt);
                            }
                            // 只在未设置时设置图片，相对路径应包含目录
                            opt.IconPath = opt.Image = rel.Replace('\\','/');
                        }
                        else if (parts.Length >= 2)
                        {
                            var optName = parts[0]; var subName = parts[1]; var key = optName + "|" + subName;
                            if (!subMap.TryGetValue(key, out var sub))
                            {
                                if (!optionMap.TryGetValue(optName, out var opt))
                                {
                                    opt = new OptionItem { Name = optName, Description = string.Empty, SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(), FileGroups = new List<ModFileGroup>(), Include = new List<string> { optName }, RootModName = modName };
                                    optionMap[optName] = opt; main.Options.Add(opt);
                                }
                                sub = new SubOptionItem { Name = subName, Description = string.Empty, FileGroups = new List<ModFileGroup>(), Include = new List<string> { optName + "/" + subName }, RootModName = modName };
                                subMap[key] = sub; opt.SubOptions.Add(sub);
                            }
                            sub.IconPath = sub.Image = rel.Replace('\\','/');
                        }
                    }
                    continue;
                }

                // 文件组
                if (!patchRegex.IsMatch(fileName)) continue;
                var match = patchRegex.Match(fileName);
                var hex = match.Groups[1].Value;
                int patchN = int.TryParse(match.Groups[2].Value, out var pn) ? pn : 0;
                var d = Path.GetDirectoryName(rel)?.Replace('\\','/') ?? string.Empty;
                string? optName2 = null; string? subName2 = null;
                if (!string.IsNullOrEmpty(d))
                {
                    var parts = d.Split('/', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 1) optName2 = parts[0];
                    else if (parts.Length >= 2) { optName2 = parts[0]; subName2 = parts[1]; }
                }
                OptionItem? opt2 = null; SubOptionItem? sub2 = null;
                if (optName2 != null)
                {
                    if (!optionMap.TryGetValue(optName2, out opt2))
                    {
                        opt2 = new OptionItem { Name = optName2, Description = string.Empty, SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(), FileGroups = new List<ModFileGroup>(), Include = new List<string> { optName2 }, RootModName = modName };
                        optionMap[optName2] = opt2; main.Options.Add(opt2);
                    }
                    if (subName2 != null)
                    {
                        var key = optName2 + "|" + subName2;
                        if (!subMap.TryGetValue(key, out sub2))
                        {
                            sub2 = new SubOptionItem { Name = subName2, Description = string.Empty, FileGroups = new List<ModFileGroup>(), Include = new List<string> { optName2 + "/" + subName2 }, RootModName = modName };
                            subMap[key] = sub2; opt2.SubOptions.Add(sub2);
                        }
                    }
                }
                var targetGroups = sub2 != null ? sub2.FileGroups : (opt2 != null ? opt2.FileGroups : main.FileGroups);
                var relDir = Path.GetDirectoryName(rel)?.Replace('\\','/') ?? string.Empty;
                var groupRelPath = string.IsNullOrEmpty(relDir) ? hex : relDir + "/" + hex;
                var existing = targetGroups.FirstOrDefault(g => g.HexPrefix == hex && g.PatchN == patchN && g.RelativePath == groupRelPath);
                if (existing == null)
                {
                    existing = new ModFileGroup { HexPrefix = hex, PatchN = patchN, RelativePath = groupRelPath, Files = new List<string>() };
                    targetGroups.Add(existing);
                }
                existing.Files.Add(rel.Replace('\\','/'));
            }
            return main;
        }

        public static MainModItem BuildModFromDirectory(string modName, string rootDir)
        {
            var main = new MainModItem
            {
                Name = modName,
                Guid = Guid.NewGuid(),
                Description = string.Empty,
                Options = new System.Collections.ObjectModel.ObservableCollection<OptionItem>(),
                FileGroups = ManifestGenerator.GetModFileGroups(rootDir, rootDir),
                RootModName = modName,
                IconPath = string.Empty,
                Image = string.Empty
            };
            var rootImages = Directory.GetFiles(rootDir, "*", SearchOption.TopDirectoryOnly)
                                       .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                       .Select(Path.GetFileName)
                                       .ToList();
            if (rootImages.Count > 0)
            {
                string[] prefs = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                var picked = rootImages.FirstOrDefault(f => prefs.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase))) ?? rootImages.First();
                main.IconPath = picked!; main.Image = picked!;
            }
            foreach (var firstDir in Directory.GetDirectories(rootDir))
            {
                var optionName = Path.GetFileName(firstDir);
                var optImages = Directory.GetFiles(firstDir, "*", SearchOption.TopDirectoryOnly)
                                         .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                         .Select(f => optionName + "/" + Path.GetFileName(f))
                                         .ToList();
                if (optImages.Count > 0)
                {
                    string[] prefs = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                    var picked = optImages.FirstOrDefault(f => prefs.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase))) ?? optImages.First();
                    var option = new OptionItem
                    {
                        Name = optionName,
                        Description = string.Empty,
                        Image = picked,
                        IconPath = picked,
                        IsSelected = false,
                        SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(),
                        Include = new List<string> { optionName },
                        FileGroups = ManifestGenerator.GetModFileGroups(rootDir, firstDir),
                        RootModName = modName
                    };
                    foreach (var secondDir in Directory.GetDirectories(firstDir))
                    {
                        var subName = Path.GetFileName(secondDir);
                        var subImages = Directory.GetFiles(secondDir, "*", SearchOption.TopDirectoryOnly)
                                                 .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                                 .Select(f => optionName + "/" + subName + "/" + Path.GetFileName(f))
                                                 .ToList();
                        string subPicked = string.Empty;
                        if (subImages.Count > 0)
                        {
                            string[] prefs2 = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                            subPicked = subImages.FirstOrDefault(f => prefs2.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase))) ?? subImages.First();
                        }
                        var subOption = new SubOptionItem
                        {
                            Name = subName,
                            Description = string.Empty,
                            Image = subPicked,
                            IconPath = subPicked,
                            IsSelected = false,
                            Include = new List<string> { optionName + "/" + subName },
                            FileGroups = ManifestGenerator.GetModFileGroups(rootDir, secondDir),
                            RootModName = modName
                        };
                        option.SubOptions.Add(subOption);
                    }
                    main.Options.Add(option);
                }
                else
                {
                    var option = new OptionItem
                    {
                        Name = optionName,
                        Description = string.Empty,
                        Image = string.Empty,
                        IconPath = string.Empty,
                        IsSelected = false,
                        SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>(),
                        Include = new List<string> { optionName },
                        FileGroups = ManifestGenerator.GetModFileGroups(rootDir, firstDir),
                        RootModName = modName
                    };
                    foreach (var secondDir in Directory.GetDirectories(firstDir))
                    {
                        var subName = Path.GetFileName(secondDir);
                        var subImages = Directory.GetFiles(secondDir, "*", SearchOption.TopDirectoryOnly)
                                                 .Where(f => ImageExts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                                                 .Select(f => optionName + "/" + subName + "/" + Path.GetFileName(f))
                                                 .ToList();
                        string subPicked = string.Empty;
                        if (subImages.Count > 0)
                        {
                            string[] prefs2 = new[] { "icon", "cover", "logo", "preview", "thumbnail", "thumb" };
                            subPicked = subImages.FirstOrDefault(f => prefs2.Any(p => Path.GetFileNameWithoutExtension(f!).Contains(p, StringComparison.OrdinalIgnoreCase))) ?? subImages.First();
                        }
                        var subOption = new SubOptionItem
                        {
                            Name = subName,
                            Description = string.Empty,
                            Image = subPicked,
                            IconPath = subPicked,
                            IsSelected = false,
                            Include = new List<string> { optionName + "/" + subName },
                            FileGroups = ManifestGenerator.GetModFileGroups(rootDir, secondDir),
                            RootModName = modName
                        };
                        option.SubOptions.Add(subOption);
                    }
                    main.Options.Add(option);
                }
            }
            return main;
        }

        private static void AppendUiLog(string text)
        {
            if (AppendLog == null) return;
            try
            {
                if (Application.Current?.Dispatcher?.CheckAccess() == true)
                    AppendLog(text);
                else
                    Application.Current?.Dispatcher?.Invoke(() => AppendLog(text));
            }
            catch { }
        }

        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }
    }
}