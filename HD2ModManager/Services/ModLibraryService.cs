using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using HD2ModManager.Models;

namespace HD2ModManager.Services
{
    public class ModLibraryService
    {
        private readonly string _libraryPath;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly Dictionary<string, ModEntity> _byGuid = new();
        public ReadOnlyDictionary<string, ModEntity> ByGuid => new(_byGuid);

        public ModLibraryService(string libraryPath)
        {
            _libraryPath = libraryPath;
        }

        public void Load()
        {
            _byGuid.Clear();
            if (!File.Exists(_libraryPath)) return;
           var json = TextEncodingUtil.ReadAllTextDetect(_libraryPath);
            try
            {
                var list = JsonSerializer.Deserialize<List<ModEntity>>(json, _options) ?? new List<ModEntity>();
                foreach (var m in list)
                {
                    if (!string.IsNullOrWhiteSpace(m.Guid)) _byGuid[m.Guid] = m;
                }
            }
            catch
            {
                // fallback: attempt simple cleaning and retry
                json = JsonCleaning(json);
                var list = JsonSerializer.Deserialize<List<ModEntity>>(json, _options) ?? new List<ModEntity>();
                foreach (var m in list)
                {
                    if (!string.IsNullOrWhiteSpace(m.Guid)) _byGuid[m.Guid] = m;
                }
            }
        }

        public void Save()
        {
            var root = SettingsService.GetModLibraryFolder();
            var list = _byGuid.Values.Select(m =>
            {
                // ensure SourcePath is relative to library root
                if (!string.IsNullOrWhiteSpace(m.SourcePath))
                {
                    try
                    {
                        var full = ResolveAbsolutePath(m.SourcePath);
                        // only make relative if under root
                        var rel = Path.GetRelativePath(root, full);
                        var combined = Path.GetFullPath(Path.Combine(root, rel));
                        if (combined.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                        {
                            m.SourcePath = rel.Replace('\\', '/');
                        }
                    }
                    catch { }
                }
                return m;
            }).ToList();
            var json = JsonSerializer.Serialize(list, _options);
            File.WriteAllText(_libraryPath, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        public bool Add(ModEntity mod)
        {
            if (string.IsNullOrWhiteSpace(mod.Guid)) mod.Guid = System.Guid.NewGuid().ToString();
            mod.CreatedAt = mod.CreatedAt == default ? DateTime.UtcNow : mod.CreatedAt;
            mod.UpdatedAt = DateTime.UtcNow;
            _byGuid[mod.Guid] = mod;
            return true;
        }

        public bool Remove(string guid)
        {
            if (!_byGuid.TryGetValue(guid, out var mod)) return false;
            try
            {
                var path = ResolveAbsolutePath(mod.SourcePath);
                LogService.Info($"Remove requested: guid={guid} name={mod.Name} rel={mod.SourcePath} abs={path}");
                if (!string.IsNullOrWhiteSpace(path))
                {
                    TryDeleteDirectory(path);
                }
            }
            catch { }
            return _byGuid.Remove(guid);
        }

        public bool Rename(string guid, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            if (!_byGuid.TryGetValue(guid, out var mod)) return false;
            try
            {
                var root = SettingsService.GetModLibraryFolder();
                var oldAbs = ResolveAbsolutePath(mod.SourcePath);
                LogService.Info($"Rename requested: guid={guid} oldName={mod.Name} oldRel={mod.SourcePath} oldAbs={oldAbs} newName={newName}");
                if (string.IsNullOrWhiteSpace(oldAbs) || !Directory.Exists(oldAbs)) return false;
                var safeName = SanitizeName(newName);
                var targetAbs = EnsureUniqueFolder(Path.Combine(root, safeName));
                LogService.Info($"Rename moving directory: {oldAbs} -> {targetAbs}");
                Directory.Move(oldAbs, targetAbs);
                mod.Name = newName.Trim();
                mod.SourcePath = Path.GetRelativePath(root, targetAbs).Replace('\\', '/');
                mod.UpdatedAt = DateTime.UtcNow;
                _byGuid[guid] = mod;
                Save();
                LogService.Info($"Rename completed: guid={guid} newRel={mod.SourcePath}");
                return true;
            }
            catch { return false; }
        }

        private static string SanitizeName(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) name = name.Replace(ch, '_');
            name = name.Trim();
            if (string.IsNullOrWhiteSpace(name)) name = "ImportedMod";
            return name;
        }

        private static string EnsureUniqueFolder(string baseDir)
        {
            var dir = baseDir;
            int i = 1;
            while (Directory.Exists(dir))
            {
                dir = baseDir + "_" + i;
                i++;
            }
            return dir;
        }

        private static string ResolveAbsolutePath(string? maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return string.Empty;
            try
            {
                var root = SettingsService.GetModLibraryFolder();
                var rel = maybeRelative.Replace('/', Path.DirectorySeparatorChar);
                // prevent path escape: remove leading ../ or ..\ segments
                while (rel.StartsWith(".." + Path.DirectorySeparatorChar))
                {
                    rel = rel.Substring(3);
                }
                var combined = Path.Combine(root, rel);
                return Path.GetFullPath(combined);
            }
            catch { return maybeRelative!; }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    LogService.Info($"TryDeleteDirectory start: {path}");
                    // Clear read-only attributes on files and directories
                    foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        try { var attr = File.GetAttributes(dir); File.SetAttributes(dir, attr & ~FileAttributes.ReadOnly); } catch { }
                    }
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { var attr = File.GetAttributes(file); File.SetAttributes(file, attr & ~FileAttributes.ReadOnly); } catch { }
                    }
                    Directory.Delete(path, recursive: true);
                    LogService.Info($"TryDeleteDirectory done: {path}");
                }
            }
            catch
            {
                // Attempt fallback: move to temp then delete
                try
                {
                    var tmpRoot = Path.Combine(Path.GetTempPath(), "HD2ModManager_Delete");
                    Directory.CreateDirectory(tmpRoot);
                    var tmp = Path.Combine(tmpRoot, Guid.NewGuid().ToString("N"));
                    LogService.Warn($"Delete fallback: moving {path} -> {tmp}");
                    Directory.Move(path, tmp);
                    foreach (var dir in Directory.EnumerateDirectories(tmp, "*", SearchOption.AllDirectories))
                    {
                        try { var attr = File.GetAttributes(dir); File.SetAttributes(dir, attr & ~FileAttributes.ReadOnly); } catch { }
                    }
                    foreach (var file in Directory.EnumerateFiles(tmp, "*", SearchOption.AllDirectories))
                    {
                        try { var attr = File.GetAttributes(file); File.SetAttributes(file, attr & ~FileAttributes.ReadOnly); } catch { }
                    }
                    Directory.Delete(tmp, recursive: true);
                    LogService.Info($"Delete fallback done: {tmp}");
                }
                catch { }
            }
        }

        public ModEntity? Get(string guid)
        {
            _byGuid.TryGetValue(guid, out var m);
            return m;
        }

        public IEnumerable<ModEntity> All() => _byGuid.Values;

        private static string JsonCleaning(string input)
        {
            // remove trailing commas before } or ] and strip // comments
            var s = System.Text.RegularExpressions.Regex.Replace(input, @",\s*(\}|\])", "$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, @"//.*", string.Empty);
            s = System.Text.RegularExpressions.Regex.Replace(s, @"/\*.*?\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            return s;
        }
    }
}
