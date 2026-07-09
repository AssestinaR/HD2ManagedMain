using System;
using System.IO;
using System.Linq;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModManager.Services
{
    public sealed class ActivationResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public ApplyResult? CoreResult { get; init; }
    }

    // 作用：使用 HD2ModCore 的一站式 Profile 应用服务替代旧复制式部署。
    public class ActivationService
    {
        private readonly ModLibraryService _library;
        private readonly ProfileService _profiles;
        private readonly NotificationService _notify;
        private readonly StoragePaths _paths;

        public ActivationService(ModLibraryService library, ProfileService profiles, NotificationService notify)
        {
            _library = library;
            _profiles = profiles;
            _notify = notify;
            _paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
        }

        public bool ApplyActiveProfile(bool dryRun = false)
        {
            return ApplyActiveProfileDetailed(dryRun).Success;
        }

        public ActivationResult ApplyActiveProfileDetailed(bool dryRun = false)
        {
            if (dryRun)
            {
                var previewMessage = "新版部署由 HD2ModCore 在执行时生成计划；当前暂不支持旧 dry-run。";
                _notify.Show(previewMessage, NotificationLevel.Info, TimeSpan.FromSeconds(4));
                return new ActivationResult { Success = true, Message = previewMessage };
            }

            var profile = _profiles.ActiveCoreProfile;
            if (profile == null)
            {
                const string message = "未启用配置。";
                _notify.Show(message, NotificationLevel.Info, TimeSpan.FromSeconds(3));
                return new ActivationResult { Success = false, Message = message };
            }

            var gameData = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(gameData))
            {
                const string message = "未设置游戏数据目录。";
                _notify.Show(message, NotificationLevel.Error, TimeSpan.FromSeconds(5));
                return new ActivationResult { Success = false, Message = message };
            }
            Directory.CreateDirectory(gameData);

            var service = CoreServices.CreateProfileApplyService();
            var result = service.ApplyAsync(profile, _library.Snapshot, _paths.ModsDirectory, gameData).AsTask().GetAwaiter().GetResult();
            var errors = result.Issues.Count(i => i.Severity == CoreIssueSeverity.Error);
            var warnings = result.Issues.Count(i => i.Severity == CoreIssueSeverity.Warning);
            var messageText = result.Success
                ? $"已应用配置：{result.Operations.Count} 个操作，警告 {warnings}。"
                : $"应用配置失败：错误 {errors}，警告 {warnings}。";
            _notify.Show(messageText, result.Success ? NotificationLevel.Info : NotificationLevel.Error, TimeSpan.FromSeconds(6));
            return new ActivationResult { Success = result.Success, Message = messageText, CoreResult = result };
        }
    }
}
