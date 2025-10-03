using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HD2ModManager.Services
{
    public class TagCatalogService
    {
        private static readonly Lazy<TagCatalogService> _inst = new(() => new TagCatalogService());
        public static TagCatalogService Instance => _inst.Value;
        public sealed class TagItem
        {
            public string Name { get; set; } = string.Empty; // Display name: Code English Chinese
            public string? Parent { get; set; }
            public string Code { get; set; } = string.Empty;
            public string? EnglishName { get; set; }
            public string? ChineseName { get; set; }
            public string? Category { get; set; }
            public string? Group { get; set; } // For weapons: Primary/Secondary/Throwable
            public string? Subcategory { get; set; } // e.g., Assault Rifle, Shotgun
            // Armor details
            public string? PassiveEnglish { get; set; }
            public string? PassiveChinese { get; set; }
            public string? PassiveDescEnglish { get; set; }
            public string? PassiveDescChinese { get; set; }
            public int? Armor { get; set; }
            public int? Speed { get; set; }
            public int? Stamina { get; set; }
        }

        private readonly Dictionary<string, TagItem> _tags = new(StringComparer.OrdinalIgnoreCase);
        private string _configDir = string.Empty;

        public IReadOnlyCollection<string> GetAllNames() => _tags.Keys.ToList();
        public IReadOnlyCollection<TagItem> GetAll() => _tags.Values.ToList();

        public bool AddCustomTag(string name)
        {
            try
            {
                var n = (name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(n)) return false;
                if (!_tags.ContainsKey(n))
                {
                    _tags[n] = new TagItem { Name = n };
                    return true;
                }
                return false;
            }
            catch { return false; }
        }

        public void Load(string configDir)
        {
            _configDir = configDir;
            try
            {
                var path = Path.Combine(configDir, "tags.json");
                if (!File.Exists(path)) return;
                var json = TextEncodingUtil.ReadAllTextDetect(path);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<TagItem>>(json) ?? new List<TagItem>();
                _tags.Clear();
                foreach (var t in list)
                {
                    if (!string.IsNullOrWhiteSpace(t.Name)) _tags[t.Name] = t;
                }
            }
            catch { }
        }

        public bool Save()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_configDir)) return false;
                var path = Path.Combine(_configDir, "tags.json");
                Directory.CreateDirectory(_configDir);
                var json = System.Text.Json.JsonSerializer.Serialize(
                    _tags.Values.OrderBy(s => s.Name).ToList(),
                    new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true,
                        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                    });
                File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                return true;
            }
            catch { return false; }
        }

        public void RebuildFromCsv(string baseDir)
        {
            var resDir = Path.Combine(baseDir, "Resources");
            // Build from ArmorList.csv with proper hierarchy
            var armorCsv = Path.Combine(resDir, "ArmorList.csv");
            _tags.Clear();
            if (File.Exists(armorCsv))
            {
                var passiveMap = LoadArmorPassives(Path.Combine(resDir, "ArmorPassives.csv"));
                foreach (var line in TextEncodingUtil.ReadAllLinesDetect(armorCsv))
                {
                    var parts = SplitCsv(line);
                    if (parts.Length < 2) continue;
                    // Columns: Category,Code,EnglishName,ChineseName,...
                    var category = parts[0].Trim();
                    var code = parts[1].Trim();
                    if (string.Equals(category, "Category", StringComparison.OrdinalIgnoreCase)) continue; // skip header
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var catZh = MapArmorCategoryToChinese(category);
                    var en = parts.Length > 2 ? parts[2].Trim() : string.Empty;
                    var zh = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                    var passive = parts.Length > 4 ? parts[4].Trim() : string.Empty;
                    var armorVal = parts.Length > 6 && int.TryParse(parts[6].Trim(), out var a) ? a : (int?)null;
                    var speedVal = parts.Length > 7 && int.TryParse(parts[7].Trim(), out var s) ? s : (int?)null;
                    var staminaVal = parts.Length > 8 && int.TryParse(parts[8].Trim(), out var st) ? st : (int?)null;
                    var name = ComposeDisplay(code, en, zh);
                    var item = new TagItem
                    {
                        Name = name,
                        Code = code,
                        EnglishName = en,
                        ChineseName = zh,
                        Category = "护甲",
                        Parent = catZh,
                        Armor = armorVal,
                        Speed = speedVal,
                        Stamina = staminaVal
                    };
                    if (!string.IsNullOrWhiteSpace(passive) && passiveMap.TryGetValue(passive, out var pd))
                    {
                        item.PassiveEnglish = pd.en;
                        item.PassiveChinese = pd.zh;
                        item.PassiveDescEnglish = pd.enDesc;
                        item.PassiveDescChinese = pd.zhDesc;
                    }
                    _tags[item.Name] = item;
                    // ensure category tag under 护甲
                    EnsureParent(catZh, "护甲");
                }
                EnsureParent("护甲", null);
            }
            else
            {
                // Fallback: ensure root exists
                EnsureParent("护甲", null);
            }

            // Weapons
            var weaponsCsv = Path.Combine(resDir, "WeaponsList.csv");
            if (File.Exists(weaponsCsv))
            {
                foreach (var line in TextEncodingUtil.ReadAllLinesDetect(weaponsCsv))
                {
                    var parts = SplitCsv(line);
                    if (parts.Length < 5) continue;
                    var category = parts[0].Trim(); // Group
                    var sub = parts[1].Trim(); // Subcategory
                    var code = parts[2].Trim();
                    var en = parts[3].Trim();
                    var zh = parts[4].Trim();
                    // Skip header row
                    if (string.Equals(category, "Group", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var cat = string.IsNullOrWhiteSpace(category) ? "武器" : TranslateWeaponCategoryToChinese(category);
                    var name = ComposeDisplay(code, en, zh);
                    _tags[name] = new TagItem { Name = name, Code = code, EnglishName = en, ChineseName = zh, Category = "武器", Parent = cat, Group = TranslateWeaponCategoryToChinese(category), Subcategory = TranslateWeaponCategoryToChinese(sub) };
                    EnsureParent(cat, "武器");
                }
                EnsureParent("武器", null);
            }

            // Support
            var supportCsv = Path.Combine(resDir, "SupportList.csv");
            if (File.Exists(supportCsv))
            {
                foreach (var line in TextEncodingUtil.ReadAllLinesDetect(supportCsv))
                {
                    var parts = SplitCsv(line);
                    if (parts.Length < 4) continue;
                    var category = parts[0].Trim();
                    var code = parts[1].Trim();
                    var en = parts[2].Trim();
                    var zh = parts[3].Trim();
                    // Skip header row
                    if (string.Equals(category, "Category", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    var cat = string.IsNullOrWhiteSpace(category) ? "支援" : TranslateSupportCategoryToChinese(category);
                    var name = ComposeDisplay(code, en, zh);
                    _tags[name] = new TagItem { Name = name, Code = code, EnglishName = en, ChineseName = zh, Category = "支援", Parent = cat };
                    EnsureParent(cat, "支援");
                }
                EnsureParent("支援", null);
            }
        }

        private static string MapArmorCategoryToChinese(string cat)
        {
            return cat switch
            {
                "Light" => "轻甲",
                "Medium" => "中甲",
                "Heavy" => "重甲",
                _ => cat
            };
        }

        private void EnsureParent(string name, string? parent)
        {
            // Translate category names to Chinese unless they are precise codes (e.g., FS-55)
            var n = IsPreciseCode(name) ? name : TranslateGenericCategoryToChinese(name);
            var p = parent == null ? null : TranslateGenericCategoryToChinese(parent);
            if (!_tags.ContainsKey(n))
            {
                _tags[n] = new TagItem { Name = n, Parent = p, Category = p };
            }
            else if (parent != null)
            {
                _tags[n].Parent ??= p;
            }
        }

        private static string[] SplitCsv(string line)
        {
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

        private static bool IsPreciseCode(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            // match patterns like FS-55, I-44, AR-23, MG-43, including prefixes with slash like A/MG-43
            return System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z]{1,3}(/)?[A-Z]{0,3}-\d{1,4}$");
        }

        private static string TranslateWeaponCategoryToChinese(string cat)
        {
            return cat switch
            {
                "Primary" => "主武器",
                "Secondary" => "副武器",
                "Throwable" => "可投掷",
                "Assault Rifle" => "突击步枪",
                "Shotgun" => "霰弹枪",
                "SMG" => "冲锋枪",
                "Marksman Rifle" => "射手步枪",
                "Pistol" => "手枪",
                "Energy" => "能量",
                "Explosive" => "爆炸",
                "Standard" => "常规",
                "Special" => "特殊",
                "Group" => "分组",
                "Subcategory" => "子类别",
                _ => cat
            };
        }

        private static string TranslateSupportCategoryToChinese(string cat)
        {
            return cat switch
            {
                "Support Weapon" => "支援武器",
                "Support Backpack" => "支援背包",
                "Support Emplacement" => "支援工事",
                "Support MineLayer" => "支援布雷器",
                "Support Sentry" => "支援炮塔",
                "Support Vehicle" => "支援载具",
                _ => cat
            };
        }

        private static string TranslateGenericCategoryToChinese(string cat)
        {
            if (string.IsNullOrWhiteSpace(cat)) return cat;
            // first map armor
            var armor = MapArmorCategoryToChinese(cat);
            if (armor != cat) return armor;
            // then support
            var support = TranslateSupportCategoryToChinese(cat);
            if (support != cat) return support;
            // then weapon
            var weapon = TranslateWeaponCategoryToChinese(cat);
            if (weapon != cat) return weapon;
            return cat;
        }

        private static (string en, string zh, string enDesc, string zhDesc)? LookupPassive(string passive, Dictionary<string, (string en, string zh, string enDesc, string zhDesc)> passiveMap)
        {
            if (string.IsNullOrWhiteSpace(passive)) return null;
            if (passiveMap.TryGetValue(passive, out var pd)) return pd;
            return null;
        }

        private static Dictionary<string, (string en, string zh, string enDesc, string zhDesc)> LoadArmorPassives(string path)
        {
            var dict = new Dictionary<string, (string, string, string, string)>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(path)) return dict;
                foreach (var line in TextEncodingUtil.ReadAllLinesDetect(path))
                {
                    var parts = SplitCsv(line);
                    if (parts.Length < 4) continue;
                    if (string.Equals(parts[0].Trim(), "PassiveEnglish", StringComparison.OrdinalIgnoreCase)) continue;
                    var en = parts[0].Trim();
                    var zh = parts[1].Trim();
                    var enDesc = parts[2].Trim();
                    var zhDesc = parts[3].Trim();
                    dict[en] = (en, zh, enDesc, zhDesc);
                }
            }
            catch { }
            return dict;
        }

        private static string ComposeDisplay(string code, string en, string zh)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(code)) parts.Add(code);
            if (!string.IsNullOrWhiteSpace(en)) parts.Add(en);
            if (!string.IsNullOrWhiteSpace(zh)) parts.Add(zh);
            return string.Join(" ", parts);
        }
    }
}
