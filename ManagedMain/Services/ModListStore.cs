using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    public class ModListStore
    {
        private readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public ObservableCollection<MainModItem> Load(string profileRoot)
        {
            var list = new ObservableCollection<MainModItem>();
            try
            {
                var path = System.IO.Path.Combine(profileRoot, "modlist.json");
                if (!System.IO.File.Exists(path)) return list;
                var json = System.IO.File.ReadAllText(path);
                var arr = JsonSerializer.Deserialize<ObservableCollection<MainModItem>>(json, _opts) ?? list;

                // Normalize: prefer Image field internally; migrate IconPath -> Image if needed
                foreach (var m in arr)
                {
                    if (string.IsNullOrWhiteSpace(m.Image) && !string.IsNullOrWhiteSpace(m.IconPath)) m.Image = m.IconPath;
                    foreach (var o in m.Options)
                    {
                        if (string.IsNullOrWhiteSpace(o.Image) && !string.IsNullOrWhiteSpace(o.IconPath)) o.Image = o.IconPath;
                        foreach (var s in o.SubOptions)
                        {
                            if (string.IsNullOrWhiteSpace(s.Image) && !string.IsNullOrWhiteSpace(s.IconPath)) s.Image = s.IconPath;
                        }
                    }
                }
                return arr;
            }
            catch { return list; }
        }

        public void Save(string profileRoot, ObservableCollection<MainModItem> mods)
        {
            try
            {
                // Before save: clear IconPath so modlist.json keeps only Image field
                foreach (var m in mods)
                {
                    m.IconPath = null;
                    foreach (var o in m.Options)
                    {
                        o.IconPath = null;
                        foreach (var s in o.SubOptions)
                        {
                            s.IconPath = null;
                        }
                    }
                }

                var path = System.IO.Path.Combine(profileRoot, "modlist.json");
                var json = JsonSerializer.Serialize(mods, _opts);
                System.IO.File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}
