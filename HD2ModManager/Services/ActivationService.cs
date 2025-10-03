using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HD2ModManager.Services
{
    public class ActivationService
    {
        private readonly ModLibraryService _library;
        private readonly ProfileService _profiles;
        private readonly NotificationService _notify;
        private readonly string _configDir;

        private const string ManifestFile = "activation.json";

        public ActivationService(ModLibraryService library, ProfileService profiles, NotificationService notify)
        {
            _library = library;
            _profiles = profiles;
            _notify = notify;
            _configDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");
        }

        // Map files from active profile into game data folder. For now we copy files (simple and robust).
        public bool ApplyActiveProfile(bool dryRun = false)
        {
            var active = _profiles.Active;
            if (active == null)
            {
                _notify.Show("未启用配置。", NotificationLevel.Info, TimeSpan.FromSeconds(3));
                return false;
            }

            var targetRoot = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(targetRoot))
            {
                _notify.Show("未设置游戏数据目录。", NotificationLevel.Error, TimeSpan.FromSeconds(5));
                return false;
            }
            Directory.CreateDirectory(targetRoot);

            // cleanup previous mappings
            var manifestPath = Path.Combine(_configDir, ManifestFile);
            var previous = LoadManifest(manifestPath);
            if (!dryRun)
            {
                foreach (var file in previous)
                {
                    try { if (File.Exists(file)) File.Delete(file); } catch { }
                }
            }

            var sorted = _profiles.GetSortedEntries(active);
            var created = new List<string>();
            foreach (var entry in sorted)
            {
                var mod = _library.Get(entry.Guid);
                if (mod == null) continue;
                var basePath = ResolveAbsoluteModPath(mod.SourcePath);
                foreach (var group in mod.FileGroups)
                {
                    foreach (var rel in group.Files)
                    {
                        try
                        {
                            var src = Path.GetFullPath(Path.Combine(basePath, rel.Replace('/', Path.DirectorySeparatorChar)));
                            var dest = Path.GetFullPath(Path.Combine(targetRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                            if (!dryRun)
                            {
                                TryCopy(src, dest);
                            }
                            created.Add(dest);
                        }
                        catch { }
                    }
                }
            }

            if (!dryRun)
            {
                SaveManifest(manifestPath, created);
            }
            _notify.Show(dryRun ? "激活预览完成。" : $"已应用配置，映射 {created.Count} 个文件。", NotificationLevel.Info, TimeSpan.FromSeconds(5));
            return true;
        }

        private static void TryCopy(string src, string dest)
        {
            try { if (File.Exists(dest)) File.Delete(dest); } catch { }
            File.Copy(src, dest, overwrite: true);
        }

        private static string ResolveAbsoluteModPath(string? sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return string.Empty;
            if (Path.IsPathRooted(sourcePath)) return sourcePath;
            var root = SettingsService.GetModLibraryFolder();
            return Path.GetFullPath(Path.Combine(root, sourcePath.Replace('/', Path.DirectorySeparatorChar)));
        }

        private static List<string> LoadManifest(string path)
        {
            try
            {
                if (!File.Exists(path)) return new List<string>();
                var json = File.ReadAllText(path);
                return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch { return new List<string>(); }
        }

        private static void SaveManifest(string path, List<string> files)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var json = System.Text.Json.JsonSerializer.Serialize(files.Distinct().ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
