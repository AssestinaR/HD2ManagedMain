using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HD2ModManager.Services
{
    public class TagCsvSuggestionService
    {
        private readonly Dictionary<string, string> _nameToTag = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _noteToTag = new(StringComparer.OrdinalIgnoreCase);

        public TagCsvSuggestionService(string baseDir)
        {
            // Load CSVs from Resources copied to output
            TryLoad(Path.Combine(baseDir, "Resources", "ArmorList.csv"), 0, 1);
            TryLoad(Path.Combine(baseDir, "Resources", "WeaponsList.csv"), 0, 1);
            TryLoad(Path.Combine(baseDir, "Resources", "SupportList.csv"), 0, 1);
            // Notes/passives mapping
            TryLoad(Path.Combine(baseDir, "Resources", "ArmorPassives.csv"), 0, 1, notes: true);
        }

        public IEnumerable<string> Suggest(Models.ModEntity mod)
        {
            var text = (mod.Name + "\n" + (mod.Description ?? string.Empty)).ToLowerInvariant();
            var res = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _nameToTag)
            {
                if (text.Contains(kv.Key)) res.Add(kv.Value);
            }
            foreach (var kv in _noteToTag)
            {
                if (text.Contains(kv.Key)) res.Add(kv.Value);
            }
            return res;
        }

        private void TryLoad(string path, int keyIdx, int tagIdx, bool notes = false)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in TextEncodingUtil.ReadAllLinesDetect(path))
                {
                    var l = line.Trim();
                    if (string.IsNullOrEmpty(l)) continue;
                    var parts = SplitCsv(l);
                    if (parts.Length <= Math.Max(keyIdx, tagIdx)) continue;
                    var key = parts[keyIdx].Trim();
                    var tag = parts[tagIdx].Trim();
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(tag)) continue;
                    if (notes) _noteToTag[key.ToLowerInvariant()] = tag;
                    else _nameToTag[key.ToLowerInvariant()] = tag;
                }
            }
            catch { }
        }

        private static string[] SplitCsv(string line)
        {
            // simple CSV split: handle quoted fields minimally
            var list = new List<string>();
            bool inQuotes = false; var cur = new System.Text.StringBuilder();
            foreach (var ch in line)
            {
                if (ch == '"') { inQuotes = !inQuotes; continue; }
                if (ch == ',' && !inQuotes) { list.Add(cur.ToString()); cur.Clear(); }
                else cur.Append(ch);
            }
            list.Add(cur.ToString());
            return list.ToArray();
        }
    }
}
