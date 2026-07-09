using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：展示单个 Mod 的派生信息与文件组概览。
    public sealed class ModDetailsPageViewModel : PageViewModel
    {
        private readonly ModLibraryService _library;
        private readonly ProfileService _profiles;
        private readonly NotificationService? _notifications;

        public string ModId { get; }
        public ModEntity? Mod { get; private set; }
        public string Name => Mod?.Name ?? "未找到 Mod";
        public string Description => string.IsNullOrWhiteSpace(Mod?.Description) ? "暂无备注" : Mod!.Description!;
        public string TagsString => Mod?.Tags == null || Mod.Tags.Count == 0 ? "无标签" : string.Join("，", Mod.Tags);
        public string SourcePath => Mod?.SourcePath ?? string.Empty;
        public string Image => Mod?.Image ?? string.Empty;
        public int FileGroupCount => Mod?.FileGroups?.Count ?? 0;
        public string ProfileStatus { get; private set; } = "未加入当前配置";
        public string FileIntegritySummary { get; private set; } = "未检查";
        public string ConflictSummary { get; private set; } = "未检查";
        public string AssetTagsString { get; private set; } = "未解析";
        public string AssetListSummary { get; private set; } = "未解析";
        public string AssetOverrideSummary { get; private set; } = "未检查";
        public string PatchSummary => Mod?.FileGroups == null || Mod.FileGroups.Count == 0
            ? "没有 patch 文件组"
            : string.Join(Environment.NewLine, Mod.FileGroups.Select(g => $"{g.HexPrefix}.patch_{g.PatchN}"));

        public RelayCommand RefreshCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand EditTagsCommand { get; }
        public RelayCommand DeleteCommand { get; }

        public ModDetailsPageViewModel(ModLibraryService library, ProfileService profiles, string modId, NotificationService? notifications = null)
        {
            Title = "Mod 详情";
            _library = library;
            _profiles = profiles;
            _notifications = notifications;
            ModId = modId;
            RefreshCommand = new RelayCommand(Refresh);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            AddToProfileCommand = new RelayCommand(AddToProfile);
            EditTagsCommand = new RelayCommand(EditTags);
            DeleteCommand = new RelayCommand(Delete);
            Refresh();
        }

        public void Refresh()
        {
            Mod = _library.Get(ModId);
            OnPropertyChanged(nameof(Mod));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
            OnPropertyChanged(nameof(TagsString));
            OnPropertyChanged(nameof(SourcePath));
            OnPropertyChanged(nameof(Image));
            OnPropertyChanged(nameof(FileGroupCount));
            OnPropertyChanged(nameof(PatchSummary));
            RefreshDerivedStatus();
            OnPropertyChanged(nameof(ProfileStatus));
            OnPropertyChanged(nameof(FileIntegritySummary));
            OnPropertyChanged(nameof(ConflictSummary));
            OnPropertyChanged(nameof(AssetTagsString));
            OnPropertyChanged(nameof(AssetListSummary));
            OnPropertyChanged(nameof(AssetOverrideSummary));
        }

        private void RefreshDerivedStatus()
        {
            ProfileStatus = BuildProfileStatus();
            FileIntegritySummary = BuildFileIntegritySummary();
            ConflictSummary = BuildConflictSummary();
            RefreshAssetStatus();
        }

        private void RefreshAssetStatus()
        {
            AssetTagsString = "未解析";
            AssetListSummary = "未解析";
            AssetOverrideSummary = "未检查";

            if (Mod == null)
            {
                AssetTagsString = "未找到 Mod";
                AssetListSummary = "未找到 Mod";
                AssetOverrideSummary = "未找到 Mod";
                return;
            }
            if (!TryParseNodeId(Mod.Guid, out var nodeId) || !_library.Snapshot.Nodes.TryGetValue(nodeId, out var node))
            {
                AssetTagsString = "无法识别 Mod ID";
                AssetListSummary = "无法识别 Mod ID";
                AssetOverrideSummary = "无法识别 Mod ID";
                return;
            }

            try
            {
                var paths = new StoragePaths(AppDomain.CurrentDomain.BaseDirectory);
                var derived = _library.GetDerivedData(Mod.Guid);
                var summary = derived?.AssetSummary;
                if (summary == null)
                {
                    AssetTagsString = "资产未解析";
                    AssetListSummary = "资产未解析";
                    AssetOverrideSummary = BuildAssetOverrideSummary(paths, nodeId);
                    return;
                }
                AssetTagsString = BuildAssetTagTreeText(summary);
                AssetListSummary = summary.Assets.Count == 0
                    ? "未发现可解析资产"
                    : string.Join(Environment.NewLine, summary.Assets.Take(80).Select(a => a.DisplayName));
                if (summary.Assets.Count > 80)
                {
                    AssetListSummary += Environment.NewLine + $"... 另有 {summary.Assets.Count - 80} 个资产";
                }

                AssetOverrideSummary = BuildAssetOverrideSummary(paths, nodeId);
            }
            catch (Exception ex)
            {
                AssetTagsString = "资产解析失败";
                AssetListSummary = $"资产解析失败：{ex.Message}";
                AssetOverrideSummary = "资产覆盖检测不可用";
            }
        }

        private static string BuildAssetTagTreeText(ModAssetSummary summary)
        {
            if (summary.TargetGroups.Count == 0)
            {
                return summary.DerivedTags.Count == 0 ? "无资产标签" : string.Join("，", summary.DerivedTags);
            }

            var builder = new StringBuilder();
            foreach (var group in summary.TargetGroups)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine($"{group.Category}（{group.AssetCount}）");
                for (var i = 0; i < group.Items.Count; i++)
                {
                    var item = group.Items[i];
                    var marker = i == group.Items.Count - 1 ? "└─" : "├─";
                    var typeSummary = item.TypeNames.Count == 0 ? "类型未知" : string.Join("，", item.TypeNames);
                    builder.AppendLine($"{marker} {item.DisplayName}（{item.AssetCount}，{typeSummary}）");
                }
            }

            return builder.ToString().TrimEnd();
        }

        private string BuildAssetOverrideSummary(StoragePaths paths, ModNodeId nodeId)
        {
            var active = _profiles.ActiveProfile;
            if (active == null) return "当前没有活动配置";
            if (!active.Entries.Any(e => e.Enabled && e.NodeId == nodeId)) return "当前 Mod 未启用在活动配置中";

            var overrideAnalyzer = CoreServices.CreateModAssetOverrideAnalyzer(paths);
            var analysis = overrideAnalyzer.AnalyzeAsync(_profiles.GetSortedEntries(active), _library.Snapshot, _library.ModsRootDirectory).AsTask().GetAwaiter().GetResult();
            var coverage = analysis.Coverages.FirstOrDefault(c => c.NodeId == nodeId);
            var relatedChains = analysis.OverrideChains.Where(c => c.Entries.Any(e => e.NodeId == nodeId)).ToList();
            if (relatedChains.Count == 0)
            {
                return coverage == null ? "未发现资产级覆盖" : $"未发现资产级覆盖；共 {coverage.TotalAssets} 个资产";
            }

            var lost = relatedChains.Count(c => c.Winner.NodeId != nodeId);
            var won = relatedChains.Count(c => c.Winner.NodeId == nodeId);
            var status = coverage is { FullyOverridden: true }
                ? "当前 Mod 的资产已全部被后加载 Mod 覆盖"
                : coverage is { PartiallyOverridden: true }
                    ? $"当前 Mod 有 {coverage.OverriddenAssets}/{coverage.TotalAssets} 个资产被覆盖"
                    : "当前 Mod 覆盖了其他 Mod 的资产";

            var lines = new List<string> { $"{status}；胜出 {won}，失效 {lost}" };
            lines.AddRange(relatedChains.Take(20).Select(chain =>
            {
                var current = chain.Entries.First(e => e.NodeId == nodeId);
                return current.IsWinner
                    ? $"覆盖 {string.Join(" -> ", chain.Entries.Where(e => e.NodeId != nodeId).Select(e => e.ModName))}：{current.Asset.DisplayName}"
                    : $"被 {chain.Winner.ModName} 覆盖：{current.Asset.DisplayName}";
            }));
            if (relatedChains.Count > 20)
            {
                lines.Add($"... 另有 {relatedChains.Count - 20} 条覆盖关系");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private void OpenFolder()
        {
            if (Mod == null) return;
            var abs = _library.GetDerivedData(Mod.Guid)?.AbsoluteDirectory ?? _library.ResolveAbsolutePath(Mod.SourcePath);
            if (string.IsNullOrWhiteSpace(abs)) return;
            if (!System.IO.Directory.Exists(abs)) System.IO.Directory.CreateDirectory(abs);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = abs, UseShellExecute = true });
        }

        private void AddToProfile()
        {
            if (Mod == null) return;
            if (_profiles.AddModToActive(Mod.Guid))
            {
                _notifications?.Show($"已加入当前配置：{Mod.Name}");
            }
            else
            {
                _notifications?.Show("无法加入当前配置，可能未创建活动配置或该 Mod 已存在。", NotificationLevel.Info);
            }
        }

        private void EditTags()
        {
            if (Mod == null) return;
            var shell = System.Windows.Application.Current?.MainWindow as HD2ModManager.MainWindow;
            var vm = shell?.DataContext as ShellViewModel;
            vm?.OpenTagEdit(new[] { Mod.Guid });
        }

        private void Delete()
        {
            if (Mod == null) return;
            var name = Mod.Name;
            var confirm = System.Windows.MessageBox.Show($"确定删除 Mod“{name}”？\n这会同时删除库中的已存储文件。", "删除 Mod", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            if (confirm != System.Windows.MessageBoxResult.Yes) return;
            ThumbnailService.CancelPendingGeneration();
            _library.Remove(Mod.Guid);
            _library.Save();
            Mod = null;
            _notifications?.Show($"已删除：{name}");
            var shell = System.Windows.Application.Current?.MainWindow as HD2ModManager.MainWindow;
            var vm = shell?.DataContext as ShellViewModel;
            vm?.Navigate(HD2ModManager.Enums.WorkspaceMode.LibraryOnly);
        }

        private string BuildProfileStatus()
        {
            if (Mod == null) return "未找到 Mod";
            var active = _profiles.ActiveProfile;
            if (active == null) return "当前没有活动配置";
            if (!TryParseNodeId(Mod.Guid, out var nodeId)) return "无法识别 Mod ID";
            var entry = active.Entries.FirstOrDefault(e => e.NodeId == nodeId);
            return entry == null
                ? $"未加入当前配置：{active.Name}"
                : $"已加入当前配置：{active.Name}，顺序 {entry.LoadOrder}，{(entry.Enabled ? "启用" : "禁用")}";
        }

        private string BuildFileIntegritySummary()
        {
            if (Mod == null) return "未找到 Mod";
            var derived = _library.GetDerivedData(Mod.Guid);
            var modDirectory = derived?.AbsoluteDirectory ?? _library.ResolveAbsolutePath(Mod.SourcePath);
            if (string.IsNullOrWhiteSpace(modDirectory) || !System.IO.Directory.Exists(modDirectory)) return "目录不存在";
            var patchFiles = derived?.PatchFiles ?? Array.Empty<IndexedPatchFile>();
            if (patchFiles.Count == 0) return "没有 patch 文件组";

            var lines = new List<string>();
            foreach (var group in patchFiles.Where(f => f.SidecarKind == PatchSidecarKind.Base).OrderBy(f => f.ArchiveHex16, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.NormalizedOrder))
            {
                var baseName = group.FileName;
                var baseExists = System.IO.File.Exists(System.IO.Path.Combine(modDirectory, baseName));
                var streamExists = System.IO.File.Exists(System.IO.Path.Combine(modDirectory, baseName + ".stream"));
                var gpuExists = System.IO.File.Exists(System.IO.Path.Combine(modDirectory, baseName + ".gpu_resources"));
                lines.Add($"{baseName}：{(baseExists ? "主体存在" : "缺少主体")}，stream {(streamExists ? "存在" : "无")}，gpu_resources {(gpuExists ? "存在" : "无")}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private string BuildConflictSummary()
        {
            if (Mod == null) return "未找到 Mod";
            var active = _profiles.ActiveProfile;
            if (active == null) return "当前没有活动配置，无法检测冲突";
            if (!TryParseNodeId(Mod.Guid, out var nodeId)) return "无法识别 Mod ID";

            var ids = active.Entries.Select(e => e.NodeId).Append(nodeId).Distinct().ToList();
            if (ids.Count < 2) return "当前配置中没有可比较的其他 Mod";

            try
            {
                var detector = CoreServices.CreateConflictDetector();
                var pairs = detector.DetectNodeConflictsAsync(ids, _library.Snapshot, _library.ModsRootDirectory).AsTask().GetAwaiter().GetResult();
                var related = pairs.Where(p => p.A == nodeId || p.B == nodeId).ToList();
                if (related.Count == 0) return "未发现与当前配置的资产冲突";

                return string.Join(Environment.NewLine, related.Select(pair =>
                {
                    var otherId = pair.A == nodeId ? pair.B : pair.A;
                    var otherName = _library.Get(otherId.Value.ToString("N"))?.Name ?? otherId.Value.ToString("N");
                    return $"与 {otherName} 冲突：{pair.SharedKeys.Count} 个资产键";
                }));
            }
            catch (Exception ex)
            {
                return $"冲突检测失败：{ex.Message}";
            }
        }

        private static bool TryParseNodeId(string value, out ModNodeId nodeId)
        {
            if (Guid.TryParse(value, out var guid))
            {
                nodeId = new ModNodeId(guid);
                return true;
            }

            nodeId = default;
            return false;
        }
    }

}
