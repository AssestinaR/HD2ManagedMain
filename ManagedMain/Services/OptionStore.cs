using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ManagedMain.Models;

namespace ManagedMain.Services
{
    public class OptionStore
    {
        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public string OptionFilePath { get; }

        public OptionStore(string? baseDir = null)
        {
            baseDir ??= AppContext.BaseDirectory;
            OptionFilePath = Path.Combine(baseDir, "Option.json");
        }

        public ManagedMainOptions LoadOrCreate()
        {
            try
            {
                if (!File.Exists(OptionFilePath))
                {
                    var created = new ManagedMainOptions();
                    Save(created);
                    return created;
                }
                var json = File.ReadAllText(OptionFilePath);
                var obj = JsonSerializer.Deserialize<ManagedMainOptions>(json, _jsonOptions) ?? new ManagedMainOptions();
                Normalize(obj);
                return obj;
            }
            catch
            {
                var fallback = new ManagedMainOptions();
                try { Save(fallback); } catch { }
                return fallback;
            }
        }

        public void Save(ManagedMainOptions options)
        {
            Normalize(options);
            var json = JsonSerializer.Serialize(options, _jsonOptions);
            File.WriteAllText(OptionFilePath, json);
        }

        private static void Normalize(ManagedMainOptions options)
        {
            options.GameFolder = ToAbs(options.GameFolder);
            foreach (var p in options.Profiles)
            {
                p.RootPath = ToAbs(p.RootPath);
                if (string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.RootPath))
                {
                    try { p.Name = new DirectoryInfo(p.RootPath).Name; } catch { }
                }
            }
        }

        private static string ToAbs(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
                return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
            }
            catch { return path; }
        }
    }
}
