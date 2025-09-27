using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;

namespace LiberTeaManager.Services
{
    internal sealed class SettingsService : ISettingsService
    {
        private readonly string _settingsPath;
        private readonly ILogService _log;

        public SettingsService(ILogService log, string? baseDir = null)
        {
            _log = log;
            var root = baseDir ?? AppDomain.CurrentDomain.BaseDirectory;
            _settingsPath = Path.Combine(root, "Option.json");
        }

        public string ModFolder { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mod");
        public string GameFolder { get; set; } = string.Empty;
        public double MainWindowWidth { get; set; } = 1100;
        public double MainWindowHeight { get; set; } = 800;
        public bool FastImport { get; set; } = true;

        // 新增: 当前配置文件名 + 历史 ModFolder 列表 (ProfileName -> ModFolder)
        public string CurrentProfile { get; set; } = "default";
        public Dictionary<string, string> ProfileModFolders { get; set; } = new();

        public void Load()
        {
            try
            {
                if (!File.Exists(_settingsPath)) return;
                var json = File.ReadAllText(_settingsPath);
                var model = JsonSerializer.Deserialize<SettingsModel>(json);
                if (model != null)
                {
                    if (!string.IsNullOrWhiteSpace(model.ModFolder)) ModFolder = model.ModFolder;
                    if (!string.IsNullOrWhiteSpace(model.GameFolder)) GameFolder = model.GameFolder;
                    if (model.MainWindowWidth > 0) MainWindowWidth = model.MainWindowWidth;
                    if (model.MainWindowHeight > 0) MainWindowHeight = model.MainWindowHeight;
                    FastImport = model.FastImport;
                    if (!string.IsNullOrWhiteSpace(model.CurrentProfile)) CurrentProfile = model.CurrentProfile;
                    ProfileModFolders = model.ProfileModFolders ?? new();
                    // 确保当前 profile 有记录
                    if (!ProfileModFolders.ContainsKey(CurrentProfile)) ProfileModFolders[CurrentProfile] = ModFolder;
                }
            }
            catch (Exception ex)
            {
                _log.Log("读取设置失败: " + ex.Message);
            }
        }

        public void Save()
        {
            try
            {
                // 更新当前 profile 的 ModFolder 映射
                if (!string.IsNullOrWhiteSpace(CurrentProfile))
                {
                    ProfileModFolders[CurrentProfile] = ModFolder;
                }
                var model = new SettingsModel
                {
                    ModFolder = ModFolder,
                    GameFolder = GameFolder,
                    MainWindowWidth = MainWindowWidth,
                    MainWindowHeight = MainWindowHeight,
                    FastImport = FastImport,
                    CurrentProfile = CurrentProfile,
                    ProfileModFolders = ProfileModFolders
                };
                var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                _log.Log("保存设置失败: " + ex.Message);
            }
        }

        private class SettingsModel
        {
            public string ModFolder { get; set; }
            public string GameFolder { get; set; }
            public double MainWindowWidth { get; set; }
            public double MainWindowHeight { get; set; }
            public bool FastImport { get; set; } = true;
            public string CurrentProfile { get; set; } = "default";
            public Dictionary<string, string>? ProfileModFolders { get; set; }
        }
    }
}
