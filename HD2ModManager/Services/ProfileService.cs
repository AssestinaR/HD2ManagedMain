using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using HD2ModManager.Models;

namespace HD2ModManager.Services
{
    public class ProfileService
    {
        private readonly string _profilesPath;
        private readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        private readonly Dictionary<string, Profile> _profiles = new();
        private string? _activeProfileKey;

        public ProfileService(string profilesPath)
        {
            _profilesPath = profilesPath;
        }

        public void Load()
        {
            _profiles.Clear();
            _activeProfileKey = null;
            if (!Directory.Exists(_profilesPath)) Directory.CreateDirectory(_profilesPath);
            foreach (var file in Directory.EnumerateFiles(_profilesPath, "*.profile.json"))
            {
                var json = File.ReadAllText(file);
                Profile? profile = null;
                try { profile = JsonSerializer.Deserialize<Profile>(json, _options); }
                catch { json = ModLibraryServiceJsonClean(json); profile = JsonSerializer.Deserialize<Profile>(json, _options); }
                if (profile != null)
                {
                    var key = Path.GetFileNameWithoutExtension(file);
                    _profiles[key] = profile;
                    // First profile becomes active by default if none set
                    _activeProfileKey ??= key;
                }
            // Persisted active key from settings overrides default selection
            var persisted = SettingsService.GetActiveProfileKey();
            if (!string.IsNullOrWhiteSpace(persisted) && _profiles.ContainsKey(persisted!))
            {
                _activeProfileKey = persisted;
            }
            }
        }

        public void Save(string key)
        {
            if (!_profiles.TryGetValue(key, out var profile)) return;
            var json = JsonSerializer.Serialize(profile, _options);
            File.WriteAllText(Path.Combine(_profilesPath, key + ".profile.json"), json);
        }

        public string CreateNew()
        {
            // 使用纯数字递增命名：1、2、3……
            int next = 1;
            if (_profiles.Count > 0)
            {
                // 尝试解析现有键中的最大数字
                var max = _profiles.Keys
                    .Select(k => int.TryParse(k, out var n) ? n : 0)
                    .DefaultIfEmpty(0)
                    .Max();
                next = Math.Max(1, max + 1);
            }
            var key = next.ToString();
            _profiles[key] = new Profile();
            _activeProfileKey ??= key; // auto activate first profile
            Save(key);
            return key;
        }

        public bool Remove(string key)
        {
            var path = Path.Combine(_profilesPath, key + ".profile.json");
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    // fallback: delete matching file by name
                    foreach (var f in Directory.EnumerateFiles(_profilesPath, "*.profile.json"))
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                        {
                            try { File.Delete(f); } catch { }
                        }
                    }
                }
            }
            catch { }
            var removed = _profiles.Remove(key);
            if (removed && _activeProfileKey == key)
            {
                _activeProfileKey = _profiles.Keys.FirstOrDefault();
                SettingsService.SetActiveProfileKey(_activeProfileKey);
            }
            return removed;
        }

        public IReadOnlyDictionary<string, Profile> All() => new ReadOnlyDictionary<string, Profile>(_profiles);
        public string? ActiveKey => _activeProfileKey;
        public Profile? Active => _activeProfileKey != null && _profiles.TryGetValue(_activeProfileKey, out var p) ? p : null;
        public void SetActive(string key)
        {
            if (_profiles.ContainsKey(key))
            {
                _activeProfileKey = key;
                SettingsService.SetActiveProfileKey(key);
            }
        }

        public bool DisableActive()
        {
            if (_activeProfileKey == null) return false;
            _activeProfileKey = null;
            SettingsService.SetActiveProfileKey(null);
            return true;
        }

        public bool Rename(string oldKey, string newKey)
        {
            if (string.IsNullOrWhiteSpace(oldKey) || string.IsNullOrWhiteSpace(newKey)) return false;
            if (!_profiles.ContainsKey(oldKey)) return false;
            // 规范化新名称：去空格
            newKey = newKey.Trim();
            // 禁止保留原名或空
            if (string.IsNullOrWhiteSpace(newKey) || string.Equals(oldKey, newKey, StringComparison.OrdinalIgnoreCase)) return false;
            // 不允许包含非法文件名字符
            foreach (var ch in System.IO.Path.GetInvalidFileNameChars())
            {
                if (newKey.Contains(ch)) return false;
            }
            // 冲突检测：避免覆盖同名配置
            if (_profiles.ContainsKey(newKey)) return false;
            var oldPath = Path.Combine(_profilesPath, oldKey + ".profile.json");
            var newPath = Path.Combine(_profilesPath, newKey + ".profile.json");
            // Persist profile under new name
            var profile = _profiles[oldKey];
            var json = JsonSerializer.Serialize(profile, _options);
            File.WriteAllText(newPath, json);
            if (File.Exists(oldPath))
            {
                try { File.Delete(oldPath); } catch { /* ignore */ }
            }
            _profiles.Remove(oldKey);
            _profiles[newKey] = profile;
            if (_activeProfileKey == oldKey)
            {
                _activeProfileKey = newKey;
                SettingsService.SetActiveProfileKey(newKey);
            }
            return true;
        }

        public List<ProfileEntry> GetSortedEntries(Profile profile)
        {
            var entries = profile.Entries.ToList();
            // Split by marker
            var top = entries.Where(e => e.Marker == -1).ToList();
            var middle = entries.Where(e => e.Marker == 0).ToList();
            var bottom = entries.Where(e => e.Marker == 1).ToList();
            var result = new List<ProfileEntry>();
            result.AddRange(TopoSortStable(top));
            result.AddRange(TopoSortStable(middle));
            result.AddRange(TopoSortStable(bottom));
            return result;
        }

        private static List<ProfileEntry> TopoSortStable(List<ProfileEntry> segment)
        {
            // base order: the input list order
            var index = segment.Select((e, i) => (e.Guid, i)).ToDictionary(t => t.Guid, t => t.i);
            var graph = new Dictionary<string, HashSet<string>>(); // u -> set(v) means u before v
            var indeg = new Dictionary<string, int>();
            foreach (var e in segment)
            {
                if (!graph.ContainsKey(e.Guid)) graph[e.Guid] = new HashSet<string>();
                indeg.TryAdd(e.Guid, 0);
            }
            foreach (var e in segment)
            {
                foreach (var a in e.After ?? Enumerable.Empty<string>())
                {
                    if (!graph.ContainsKey(a) || !graph.ContainsKey(e.Guid)) continue; // ignore references outside segment
                    if (graph[a].Add(e.Guid)) indeg[e.Guid] = indeg.GetValueOrDefault(e.Guid) + 1;
                }
                foreach (var b in e.Before ?? Enumerable.Empty<string>())
                {
                    if (!graph.ContainsKey(e.Guid) || !graph.ContainsKey(b)) continue;
                    if (graph[e.Guid].Add(b)) indeg[b] = indeg.GetValueOrDefault(b) + 1;
                }
            }
            var res = new List<ProfileEntry>();
            var pq = new SortedSet<(int, string)>(Comparer<(int, string)>.Create((x, y) => x.Item1 == y.Item1 ? string.CompareOrdinal(x.Item2, y.Item2) : x.Item1.CompareTo(y.Item1)));
            foreach (var e in segment.Where(s => indeg.GetValueOrDefault(s.Guid) == 0)) pq.Add((index.GetValueOrDefault(e.Guid, int.MaxValue), e.Guid));
            var remaining = segment.ToDictionary(s => s.Guid, s => s);
            while (pq.Count > 0)
            {
                var (idx, g) = pq.Min; pq.Remove(pq.Min);
                if (!remaining.TryGetValue(g, out var entry)) continue;
                res.Add(entry);
                remaining.Remove(g);
                foreach (var v in graph[g])
                {
                    indeg[v] = indeg.GetValueOrDefault(v) - 1;
                    if (indeg[v] == 0) pq.Add((index.GetValueOrDefault(v, int.MaxValue), v));
                }
            }
            // append any remaining (cycles broken) by base order
            res.AddRange(remaining.Values.OrderBy(e => index.GetValueOrDefault(e.Guid, int.MaxValue)).ToList());
            return res;
        }

        private static string ModLibraryServiceJsonClean(string input)
        {
            var s = System.Text.RegularExpressions.Regex.Replace(input, ",\\s*(\\}|\\])", "$1");
            s = System.Text.RegularExpressions.Regex.Replace(s, "//.*", string.Empty);
            s = System.Text.RegularExpressions.Regex.Replace(s, "/\\*.*?\\*/", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline);
            return s;
        }
    }
}
