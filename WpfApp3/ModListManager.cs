using System.Collections.ObjectModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LiberTeaManager.Services;
using System;
using System.Linq;

namespace LiberTeaManager
{
    public static class ModListManager
    {
        public static string ProfileName { get; private set; } = "default";
        public static void SetProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "default";
            if (ProfileName != name)
            {
                ProfileName = name.Trim();
                _cache = null; _lastJson = null; _dirty = true; DisposeWatcher();
            }
        }

        private static string ProfilesFolder => Path.Combine(SettingsContext.ModFolder, "profiles");
        private static string ModListPath => ProfileName == "default"
            ? Path.Combine(SettingsContext.ModFolder, "modlist.json")
            : Path.Combine(ProfilesFolder, ProfileName + ".json");

        private static readonly object _sync = new();
        private static ObservableCollection<MainModItem>? _cache;
        private static string? _lastJson;
        private static DateTime _lastWriteUtc;
        private static bool _dirty = true;
        private static FileSystemWatcher? _watcher;

        private static JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Converters = { new FlexibleIntConverter() }
        };

        private static void DisposeWatcher()
        {
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
        }

        private static void EnsureModRoot()
        {
            if (!Directory.Exists(SettingsContext.ModFolder)) Directory.CreateDirectory(SettingsContext.ModFolder);
            if (ProfileName != "default" && !Directory.Exists(ProfilesFolder)) Directory.CreateDirectory(ProfilesFolder);
            if (_watcher == null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(ModListPath)!;
                    var file = Path.GetFileName(ModListPath);
                    _watcher = new FileSystemWatcher(dir, file) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
                    _watcher.Changed += (_, __) => MarkDirty();
                    _watcher.Created += (_, __) => MarkDirty();
                    _watcher.Renamed += (_, __) => MarkDirty();
                    _watcher.Deleted += (_, __) => MarkDirty();
                    _watcher.EnableRaisingEvents = true;
                }
                catch { }
            }
        }

        private static void MarkDirty() { lock (_sync) { _dirty = true; } }

        private static string MigrateBoolEnabledToInt(string json)
        {
            json = Regex.Replace(json, "\\\"Enabled\\\"\\s*:\\s*true", "\"Enabled\": 1", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, "\\\"Enabled\\\"\\s*:\\s*false", "\"Enabled\": 0", RegexOptions.IgnoreCase);
            return json;
        }

        public static List<string> GetAllProfiles()
        {
            EnsureModRoot();
            var list = new List<string> { "default" };
            if (Directory.Exists(ProfilesFolder))
            {
                foreach (var f in Directory.GetFiles(ProfilesFolder, "*.json"))
                {
                    var n = Path.GetFileNameWithoutExtension(f);
                    if (!string.IsNullOrWhiteSpace(n)) list.Add(n);
                }
            }
            return list;
        }

        public static void EnsureEmptyProfile(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;
            if (name == "default") return;
            EnsureModRoot();
            var path = Path.Combine(ProfilesFolder, name + ".json");
            if (!File.Exists(path)) File.WriteAllText(path, "[]");
        }

        public static void CopyProfile(string sourceName, string targetName)
        {
            if (string.IsNullOrWhiteSpace(sourceName) || string.IsNullOrWhiteSpace(targetName)) return;
            if (sourceName == targetName) return;
            if (targetName == "default") return;
            EnsureModRoot();
            string srcPath = sourceName == "default" ? Path.Combine(SettingsContext.ModFolder, "modlist.json") : Path.Combine(ProfilesFolder, sourceName + ".json");
            if (!File.Exists(srcPath)) return;
            string targetPath = Path.Combine(ProfilesFolder, targetName + ".json");
            File.Copy(srcPath, targetPath, overwrite: false);
        }

        public static void AddMod(MainModItem newMod, string tempDir)
        {
            EnsureModRoot();
            string modFolder = Path.Combine(SettingsContext.ModFolder, newMod.Name);
            try
            {
                var srcFull = Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var dstFull = Path.GetFullPath(modFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(srcFull, dstFull, StringComparison.OrdinalIgnoreCase))
                {
                    if (Directory.Exists(modFolder)) Directory.Delete(modFolder, true);
                    Directory.Move(tempDir, modFolder);
                }
            }
            catch (Exception ex)
            {
                if (!Directory.Exists(modFolder))
                {
                    try { CopyDirectoryRecursive(tempDir, modFolder); }
                    catch { throw new IOException($"导入 Mod 时移动/复制目录失败: {ex.Message}"); }
                }
            }
            var mods = LoadModList();
            mods.Add(newMod);
            SaveModList(mods);
        }

        private static void CopyDirectoryRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
            foreach (var dir in Directory.GetDirectories(source)) CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        public static void DeleteMod(Guid guid)
        {
            var mods = LoadModList();
            var toDelete = mods.FirstOrDefault(m => m.Guid == guid);
            if (toDelete != null)
            {
                string modFolder = Path.Combine(SettingsContext.ModFolder, toDelete.Name);
                if (Directory.Exists(modFolder)) Directory.Delete(modFolder, true);
                mods.Remove(toDelete);
                SaveModList(mods);
            }
        }

        public static void DeleteSelectedOptions()
        {
            var mods = LoadModList();
            foreach (var mod in mods)
            {
                var optionsToDelete = mod.Options.Where(o => o.IsSelected).ToList();
                foreach (var opt in optionsToDelete)
                {
                    string optFolder = Path.Combine(SettingsContext.ModFolder, mod.Name, opt.Name);
                    if (Directory.Exists(optFolder)) Directory.Delete(optFolder, true);
                    mod.Options.Remove(opt);
                }
                string manifestPath = Path.Combine(SettingsContext.ModFolder, mod.Name, "manifest.json");
                if (File.Exists(manifestPath))
                {
                    var json = MigrateBoolEnabledToInt(File.ReadAllText(manifestPath));
                    json = Regex.Replace(json, @",(\s*[}\]])", "$1");
                    File.WriteAllText(manifestPath, json);
                }
            }
            SaveModList(mods);
        }

        public static ObservableCollection<MainModItem> LoadModList()
        {
            EnsureModRoot();
            lock (_sync)
            {
                if (!_dirty && _cache != null) return _cache;
                try
                {
                    if (!File.Exists(ModListPath))
                    {
                        File.WriteAllText(ModListPath, "[]");
                    }
                    var json = File.ReadAllText(ModListPath);
                    json = MigrateBoolEnabledToInt(json);
                    json = Regex.Replace(json, @",(\s*[}\]])", "$1");
                    var list = JsonSerializer.Deserialize<ObservableCollection<MainModItem>>(json, _jsonOptions) ?? new ObservableCollection<MainModItem>();
                    foreach (var m in list)
                    {
                        m.RootModName = m.Name;
                        m.FileGroups ??= new List<ModFileGroup>();
                        if (m.Options == null) m.Options = new System.Collections.ObjectModel.ObservableCollection<OptionItem>();
                        foreach (var o in m.Options)
                        {
                            o.RootModName = m.Name;
                            o.FileGroups ??= new List<ModFileGroup>();
                            o.Include ??= new List<string>();
                            if (o.SubOptions == null) o.SubOptions = new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>();
                            foreach (var s in o.SubOptions)
                            {
                                s.RootModName = m.Name;
                                s.FileGroups ??= new List<ModFileGroup>();
                                s.Include ??= new List<string>();
                            }
                        }
                    }
                    _cache = list;
                    _lastJson = json;
                    _lastWriteUtc = File.GetLastWriteTimeUtc(ModListPath);
                    _dirty = false;
                    return list;
                }
                catch
                {
                    return _cache ??= new ObservableCollection<MainModItem>();
                }
            }
        }

        public static void SaveModList(ObservableCollection<MainModItem> mods)
        {
            EnsureModRoot();
            lock (_sync)
            {
                try
                {
                    var slim = mods.Select(m => new
                    {
                        m.Guid,
                        m.Name,
                        Enabled = (int)m.Enabled,
                        m.Description,
                        m.IconPath,
                        m.Image,
                        m.Url,
                        FileGroups = (m.FileGroups ?? new List<ModFileGroup>()).Select(g => new
                        {
                            g.HexPrefix,
                            g.PatchN,
                            Files = (g.Files ?? new List<string>()).ToList()
                        }).ToList(),
                        Options = (m.Options ?? new System.Collections.ObjectModel.ObservableCollection<OptionItem>()).Select(o => new
                        {
                            o.Name,
                            Enabled = (int)o.Enabled,
                            o.Description,
                            o.IconPath,
                            o.Image,
                            o.Url,
                            FileGroups = (o.FileGroups ?? new List<ModFileGroup>()).Select(g => new
                            {
                                g.HexPrefix,
                                g.PatchN,
                                Files = (g.Files ?? new List<string>()).ToList()
                            }).ToList(),
                            SubOptions = (o.SubOptions ?? new System.Collections.ObjectModel.ObservableCollection<SubOptionItem>()).Select(s => new
                            {
                                s.Name,
                                Enabled = (int)s.Enabled,
                                s.Description,
                                s.IconPath,
                                s.Image,
                                s.Url,
                                FileGroups = (s.FileGroups ?? new List<ModFileGroup>()).Select(g => new
                                {
                                    g.HexPrefix,
                                    g.PatchN,
                                    Files = (g.Files ?? new List<string>()).ToList()
                                }).ToList()
                            }).ToList()
                        }).ToList()
                    }).ToList();

                    var json = JsonSerializer.Serialize(slim, _jsonOptions);
                    File.WriteAllText(ModListPath, json);
                    _lastJson = json;
                    _lastWriteUtc = File.GetLastWriteTimeUtc(ModListPath);
                    _dirty = false;
                }
                catch { }
            }
        }

        public static void Flush() { }
    }

    internal class FlexibleIntConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Number => reader.GetInt32(),
                JsonTokenType.True => 1,
                JsonTokenType.False => 0,
                JsonTokenType.String => int.TryParse(reader.GetString(), out var v) ? v : 0,
                _ => 0
            };
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
        { writer.WriteNumberValue(value); }
    }
}