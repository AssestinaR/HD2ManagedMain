using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using HD2ModCore.Infrastructure;
using Microsoft.Win32;

namespace HD2ModManager.Services
{
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        private class SettingsModel
        {
            public string? Language { get; set; }
            public string? SelectedProfileKey { get; set; }
            public string? ModLibraryFolder { get; set; }
            public string? GameDataFolder { get; set; }
            public bool AutoCleanup { get; set; } = false;
            public bool EnableLibraryImages { get; set; } = true;
            public bool AutoUpdateAssetMetadata { get; set; } = false;
            public string? AssetMetadataRepository { get; set; }
        }

        public const string DefaultAssetMetadataRepository = "https://raw.githubusercontent.com/Boxofbiscuits97/HD2SDK-CommunityEdition/main";

        public static string? GetLanguage()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                return model?.Language;
            }
            catch { return null; }
        }

        public static bool GetEnableLibraryImages()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return true; // default enabled
                var json = File.ReadAllText(SettingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                return model?.EnableLibraryImages ?? true;
            }
            catch { return true; }
        }

        public static bool SetEnableLibraryImages(bool enabled)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.EnableLibraryImages = enabled;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        public static bool GetAutoUpdateAssetMetadata()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var json = File.ReadAllText(SettingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                return model?.AutoUpdateAssetMetadata ?? false;
            }
            catch { return false; }
        }

        public static bool SetAutoUpdateAssetMetadata(bool enabled)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.AutoUpdateAssetMetadata = enabled;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        public static string GetAssetMetadataRepository()
        {
            try
            {
                var model = LoadAll();
                return string.IsNullOrWhiteSpace(model?.AssetMetadataRepository) ? DefaultAssetMetadataRepository : model!.AssetMetadataRepository!;
            }
            catch { return DefaultAssetMetadataRepository; }
        }

        public static bool SetAssetMetadataRepository(string? repository)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.AssetMetadataRepository = string.IsNullOrWhiteSpace(repository) ? DefaultAssetMetadataRepository : repository;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        public static bool GetAutoCleanup()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return false;
                var json = File.ReadAllText(SettingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                return model?.AutoCleanup ?? false;
            }
            catch { return false; }
        }

        public static bool SetAutoCleanup(bool enabled)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.AutoCleanup = enabled;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        public static bool SetLanguage(string culture)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.Language = culture;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        public static string? GetSelectedProfileKey()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                return model?.SelectedProfileKey;
            }
            catch { return null; }
        }

        public static bool SetSelectedProfileKey(string? key)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.SelectedProfileKey = key;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        private static SettingsModel? LoadAll()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return null;
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SettingsModel>(json);
            }
            catch { return null; }
        }

        public static string GetDefaultModLibraryFolder()
        {
            var recommended = GetRecommendedModLibraryFolder();
            return CanUseDirectory(recommended) ? recommended : GetPortableModLibraryFolder();
        }

        public static string GetPortableModLibraryFolder() => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mods");

        public static string GetRecommendedModLibraryFolder()
        {
            var data = GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(data)) return GetPortableModLibraryFolder();
            var gameDirectory = Directory.GetParent(data)?.FullName;
            var commonDirectory = gameDirectory is null ? null : Directory.GetParent(gameDirectory)?.FullName;
            return string.IsNullOrWhiteSpace(commonDirectory)
                ? GetPortableModLibraryFolder()
                : Path.Combine(commonDirectory, "HD2ModManager", "mods");
        }

        public static void EnsureDefaultModLibraryFolder()
        {
            var model = LoadAll() ?? new SettingsModel();
            if (!string.IsNullOrWhiteSpace(model.ModLibraryFolder)) return;
            var portable = GetPortableModLibraryFolder();
            model.ModLibraryFolder = Directory.Exists(portable) && Directory.EnumerateFileSystemEntries(portable).Any()
                ? portable
                : GetDefaultModLibraryFolder();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static StoragePaths CreateStoragePaths()
            => new(AppDomain.CurrentDomain.BaseDirectory, GetModLibraryFolder());

        public static string GetModLibraryFolder()
        {
            try
            {
                var model = LoadAll();
                var p = model?.ModLibraryFolder;
                if (string.IsNullOrWhiteSpace(p)) return GetDefaultModLibraryFolder();
                return p!;
            }
            catch { return GetDefaultModLibraryFolder(); }
        }

        public static bool SetModLibraryFolder(string? folder)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.ModLibraryFolder = folder;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        private static bool CanUseDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var probe = Path.Combine(path, $".hd2-write-probe-{Guid.NewGuid():N}.tmp");
            try
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(probe, "probe");
                return true;
            }
            catch { return false; }
            finally { try { if (File.Exists(probe)) File.Delete(probe); } catch { } }
        }

        public static string GetGameDataFolder()
        {
            try
            {
                var model = LoadAll();
                var p = model?.GameDataFolder;
                return p ?? string.Empty;
            }
            catch { return string.Empty; }
        }

        public static bool SetGameDataFolder(string? folder)
        {
            try
            {
                var model = LoadAll() ?? new SettingsModel();
                model.GameDataFolder = folder;
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
                return true;
            }
            catch { return false; }
        }

        // Try to detect Helldivers 2 installation via Steam library folders and set GameDataFolder
        public static string TryDetectAndSetGameDataFolder()
        {
            var detected = DetectGameDataFolderInternal();
            if (!string.IsNullOrWhiteSpace(detected))
            {
                SetGameDataFolder(detected);
            }
            return detected;
        }

        private static string DetectGameDataFolderInternal()
        {
            try
            {
                var libs = new System.Collections.Generic.List<string>();
                var steamPath = GetSteamInstallPath();
                if (!string.IsNullOrWhiteSpace(steamPath)) libs.Add(steamPath);
                // libraryfolders.vdf
                var vdf = Path.Combine(steamPath ?? string.Empty, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    try
                    {
                        var text = File.ReadAllText(vdf);
                        foreach (Match m in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase))
                        {
                            var p = m.Groups[1].Value.Replace("\\\\", "\\");
                            if (Directory.Exists(p)) libs.Add(p);
                        }
                    }
                    catch { }
                }
                // common candidates if registry missing
                var defaults = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam"),
                };
                foreach (var d in defaults) if (Directory.Exists(d)) libs.Add(d);

                // probe each lib for Helldivers 2
                foreach (var lib in libs)
                {
                    try
                    {
                        var common = Path.Combine(lib, "steamapps", "common");
                        if (!Directory.Exists(common)) continue;
                        var candidates = new[] { "Helldivers 2", "HELLDIVERS 2" };
                        foreach (var name in candidates)
                        {
                            var game = Path.Combine(common, name);
                            if (Directory.Exists(game))
                            {
                                var data = Path.Combine(game, "data");
                                if (Directory.Exists(data)) return data;
                                // fallback to return expected path even if not yet present
                                return data;
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return string.Empty;
        }

        private static string GetSteamInstallPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\\Valve\\Steam");
                var v = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"Software\\Valve\\Steam");
                var v = key?.GetValue("InstallPath") as string;
                if (!string.IsNullOrWhiteSpace(v)) return v;
            }
            catch { }
            return string.Empty;
        }
    }
}
