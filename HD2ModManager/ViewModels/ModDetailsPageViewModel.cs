using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HD2ModCore.Application;
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
        private readonly DerivedStateCoordinator _derivedState;
        private readonly NotificationService? _notifications;
        private readonly IMaterialPackagingApplicationService _materialPackaging;
        private ModMaterialPackagingState? _materialState;
        private bool _materialOperationRunning;

        public string ModId { get; }
        public ModEntity? Mod { get; private set; }
        public string Name => Mod?.Name ?? "未找到 Mod";
        public string Description => string.IsNullOrWhiteSpace(Mod?.Description) ? "暂无备注" : Mod!.Description!;
        public string SourcePath => Mod?.SourcePath ?? string.Empty;
        public string Image => Mod?.Image ?? string.Empty;
        public int FileGroupCount => Mod?.FileGroups?.Count ?? 0;
        public string ProfileStatus { get; private set; } = "未加入当前配置";
        public string FileIntegritySummary { get; private set; } = "未检查";
        public string ConflictSummary { get; private set; } = "未检查";
        public string AssetTagsString { get; private set; } = "未解析";
        public string AssetListSummary { get; private set; } = "未解析";
        public string AssetOverrideSummary { get; private set; } = "未检查";
        public string UserStatusTitle { get; private set; } = "状态未知";
        public string UserStatusSummary { get; private set; } = "正在读取状态。";
        public string MaterialPackagingSummary { get; private set; } = "正在分析材质引用。";
        public bool CanSplitEmbeddedMaterials => !_materialOperationRunning && _materialState?.CanSplit == true;
        public bool CanReplaceEmbeddedMaterials => !_materialOperationRunning && _materialState?.HasEmbeddedMaterials == true;
        public bool CanEmbedExternalMaterials => !_materialOperationRunning && _materialState?.HasExternalMaterials == true;
        public string PatchSummary => Mod?.FileGroups == null || Mod.FileGroups.Count == 0
            ? "没有 patch 文件组"
            : string.Join(Environment.NewLine, Mod.FileGroups.Select(g => $"{g.HexPrefix}.patch_{g.PatchN}"));

        public RelayCommand RefreshCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand OpenAdvancedDetailsCommand { get; }
        public RelayCommand SplitEmbeddedMaterialsCommand { get; }
        public RelayCommand ReplaceEmbeddedMaterialsCommand { get; }
        public RelayCommand EmbedExternalMaterialsCommand { get; }

        public ModDetailsPageViewModel(ModLibraryService library, ProfileService profiles, DerivedStateCoordinator derivedState, string modId, NotificationService? notifications = null)
        {
            Title = "Mod 详情";
            _library = library;
            _profiles = profiles;
            _derivedState = derivedState;
            _notifications = notifications;
			_materialPackaging = CoreServices.CreateMaterialPackagingApplicationService();
            ModId = modId;
            RefreshCommand = new RelayCommand(Refresh);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            AddToProfileCommand = new RelayCommand(AddToProfile);
            DeleteCommand = new RelayCommand(Delete);
            OpenAdvancedDetailsCommand = new RelayCommand(OpenAdvancedDetails);
            SplitEmbeddedMaterialsCommand = new RelayCommand(async _ => await SplitEmbeddedMaterialsAsync(), _ => CanSplitEmbeddedMaterials);
            ReplaceEmbeddedMaterialsCommand = new RelayCommand(async _ => await MergeMaterialCandidateAsync(requireAllExternalMaterials: false), _ => CanReplaceEmbeddedMaterials);
            EmbedExternalMaterialsCommand = new RelayCommand(async _ => await MergeMaterialCandidateAsync(requireAllExternalMaterials: true), _ => CanEmbedExternalMaterials);
            _derivedState.SnapshotChanged += (_, _) => RunOnUiThread(Refresh);
            Refresh();
			_ = RefreshMaterialPackagingStateAsync();
        }

        public void Refresh()
        {
            Mod = _library.Get(ModId);
            OnPropertyChanged(nameof(Mod));
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(Description));
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
            OnPropertyChanged(nameof(UserStatusTitle));
            OnPropertyChanged(nameof(UserStatusSummary));
            OnPropertyChanged(nameof(MaterialPackagingSummary));
            RaiseMaterialCommandStates();
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

            var statuses = _derivedState.ProjectStatuses(_profiles.SelectedProfileId);
            if (statuses.TryGetValue(nodeId, out var userStatus))
            {
                UserStatusTitle = userStatus.Title;
                UserStatusSummary = userStatus.Summary;
            }
            var derived = _library.GetDerivedData(Mod.Guid);
            var summary = derived?.AssetSummary;
            if (summary == null)
            {
                AssetTagsString = "资产未解析";
                AssetListSummary = "资产派生数据正在后台更新。";
                AssetOverrideSummary = BuildCachedAssetOverrideSummary(nodeId);
                return;
            }
            AssetTagsString = BuildAssetTagTreeText(summary);
            AssetListSummary = summary.Assets.Count == 0
                ? "未发现可解析资产"
                : string.Join(Environment.NewLine, summary.Assets.Take(80).Select(a => a.DisplayName));
            if (summary.Assets.Count > 80) AssetListSummary += Environment.NewLine + $"... 另有 {summary.Assets.Count - 80} 个资产";
            AssetOverrideSummary = BuildCachedAssetOverrideSummary(nodeId);
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

        private string BuildCachedAssetOverrideSummary(ModNodeId nodeId)
        {
            var active = _profiles.ActiveProfile;
            if (active == null) return "当前没有活动配置";
            if (!active.Entries.Any(e => e.NodeId == nodeId)) return "当前 Mod 未加入活动配置";
            var graph = _derivedState.Snapshot.ExpectedGraph;
            if (graph is null || graph.ProfileId != active.Id || graph.ProfileRevision != active.Revision) return "预计覆盖数据正在后台更新。";
            var coverage = graph.Coverages.FirstOrDefault(c => c.NodeId == nodeId);
            var relatedChains = graph.AssetChains.Where(c => c.Entries.Any(e => e.NodeId == nodeId)).ToList();
            if (relatedChains.Count == 0)
            {
                return coverage == null ? "未发现资产级覆盖" : $"未发现资产级覆盖；共 {coverage.TotalAssetKeys} 个 AssetKey";
            }

            var lost = relatedChains.Count(c => c.Winner.NodeId != nodeId);
            var won = relatedChains.Count(c => c.Winner.NodeId == nodeId);
            var status = coverage is { FullyOverridden: true }
                ? "当前 Mod 的 AssetKey 已全部被后加载 Mod 覆盖"
                : coverage is { PartiallyOverridden: true }
                    ? $"当前 Mod 有 {coverage.OverriddenAssetKeys}/{coverage.TotalAssetKeys} 个 AssetKey 被覆盖"
                    : "当前 Mod 覆盖了其他 Mod 的资产";

            var lines = new List<string> { $"{status}；胜出 {won}，失效 {lost}" };
            lines.AddRange(relatedChains.Take(20).Select(chain =>
            {
                var current = chain.Entries.First(e => e.NodeId == nodeId);
                var displayName = $"{current.Mapping.FileDisplayName}（{current.Mapping.TypeDisplayName}）";
                return current.IsWinner
                    ? $"覆盖 {string.Join(" -> ", chain.Entries.Where(e => e.NodeId != nodeId).Select(e => e.ModName))}：{displayName}"
                    : $"被 {chain.Winner.ModName} 覆盖：{displayName}";
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

        private void OpenAdvancedDetails()
        {
            var window = new HD2ModManager.Views.AdvancedModDetailsWindow
            {
                Owner = System.Windows.Application.Current?.MainWindow,
                DataContext = this,
            };
            window.ShowDialog();
        }

        private async Task RefreshMaterialPackagingStateAsync()
        {
            if (Mod == null || !TryParseNodeId(Mod.Guid, out var nodeId) || !_library.Snapshot.Nodes.TryGetValue(nodeId, out var node)) return;
            try
            {
                _materialState = await _materialPackaging.InspectAsync(node, _library.ModsRootDirectory);
                MaterialPackagingSummary = $"需要材质 {_materialState.RequiredMaterialCount}；内嵌 {_materialState.EmbeddedMaterialCount}；外部 {_materialState.ExternalMaterialCount}；内嵌贴图 {_materialState.EmbeddedTextureCount}";
                if (_materialState.Blockers.Count != 0) MaterialPackagingSummary += Environment.NewLine + string.Join(Environment.NewLine, _materialState.Blockers);
            }
            catch (Exception exception)
            {
                _materialState = null;
                MaterialPackagingSummary = $"材质分析失败：{exception.Message}";
            }
            RunOnUiThread(() => { OnPropertyChanged(nameof(MaterialPackagingSummary)); RaiseMaterialCommandStates(); });
        }

        private async Task SplitEmbeddedMaterialsAsync()
        {
            if (!TryGetCurrentNode(out var node) || !TryChooseOutput(out var output)) return;
            await ExecuteMaterialOperationAsync(
                () => _materialPackaging.SplitAsync(node, _library.ModsRootDirectory, output.OutputDirectory).AsTask(),
                output.ImportToLibrary);
        }

        private async Task MergeMaterialCandidateAsync(bool requireAllExternalMaterials)
        {
            if (!TryGetCurrentNode(out var source)) return;
            _materialOperationRunning = true; RaiseMaterialCommandStates();
            try
            {
                var candidates = await _materialPackaging.FindCandidatesAsync(source, _library.Snapshot.Nodes.Values.ToArray(), _library.ModsRootDirectory, requireAllExternalMaterials);
                var items = candidates.Select(candidate => new HD2ModManager.Views.MaterialCandidateItem(candidate)).ToArray();
                if (items.Length == 0)
                {
                    _notifications?.Show("Mod 库中没有精确匹配此模型 Material AssetKey 的材质包。", NotificationLevel.Info);
                    return;
                }
                var picker = new HD2ModManager.Views.MaterialCandidateWindow { Owner = System.Windows.Application.Current?.MainWindow, DataContext = items };
                if (picker.ShowDialog() != true || picker.SelectedCandidate is null) return;
                if (!_library.Snapshot.Nodes.TryGetValue(picker.SelectedCandidate.NodeId, out var candidate)) return;
                if (!TryChooseOutput(out var output)) return;
                var destination = System.IO.Path.Combine(output.OutputDirectory, SanitizeFileName($"{source.Metadata.Name}-{candidate.Metadata.Name}-内嵌版"));
                await ExecuteMaterialOperationAsync(() => _materialPackaging.MergeAsync(source, candidate, _library.ModsRootDirectory, destination, requireAllExternalMaterials).AsTask(), output.ImportToLibrary, operationAlreadyRunning: true);
            }
            finally
            {
                _materialOperationRunning = false; RaiseMaterialCommandStates();
            }
        }

        private async Task ExecuteMaterialOperationAsync(Func<Task<MaterialPackagingOperationResult>> operation, bool importToLibrary, bool operationAlreadyRunning = false)
        {
            if (!operationAlreadyRunning) { _materialOperationRunning = true; RaiseMaterialCommandStates(); }
            try
            {
                var result = await operation();
                if (!result.IsSuccessful)
                {
                    _notifications?.Show(string.Join("；", result.Issues.Select(issue => issue.Message)), NotificationLevel.Error, TimeSpan.FromSeconds(8));
                    return;
                }
                if (importToLibrary)
                {
                    var importer = new ImportService(_library);
                    foreach (var directory in result.OutputDirectories) await importer.ImportPathAsync(directory, default);
                }
                _notifications?.Show($"材质打包完成：{result.AssetCount} 个资源，{result.GraphEdgeCount} 条引用；{(importToLibrary ? "已导入 Mod 库" : "已写出到目标文件夹")}。", NotificationLevel.Info, TimeSpan.FromSeconds(6));
                await RefreshMaterialPackagingStateAsync();
            }
            finally
            {
                if (!operationAlreadyRunning) { _materialOperationRunning = false; RaiseMaterialCommandStates(); }
            }
        }

        private bool TryGetCurrentNode(out ModNode node)
        {
            node = default!;
            return Mod != null && TryParseNodeId(Mod.Guid, out var nodeId) && _library.Snapshot.Nodes.TryGetValue(nodeId, out node!);
        }

        private static bool TryChooseOutput(out HD2ModManager.Views.MaterialPackagingOutputWindow output)
        {
            output = new HD2ModManager.Views.MaterialPackagingOutputWindow { Owner = System.Windows.Application.Current?.MainWindow };
            return output.ShowDialog() == true;
        }

        private void RaiseMaterialCommandStates()
        {
            OnPropertyChanged(nameof(CanSplitEmbeddedMaterials)); OnPropertyChanged(nameof(CanReplaceEmbeddedMaterials)); OnPropertyChanged(nameof(CanEmbedExternalMaterials));
            SplitEmbeddedMaterialsCommand.RaiseCanExecuteChanged(); ReplaceEmbeddedMaterialsCommand.RaiseCanExecuteChanged(); EmbedExternalMaterialsCommand.RaiseCanExecuteChanged();
        }

        private static string SanitizeFileName(string name) => string.Concat(name.Select(character => System.IO.Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();

        private void AddToProfile()
        {
            if (Mod == null) return;
            if (_profiles.AddModToSelected(Mod.Guid))
            {
                _notifications?.Show($"已加入正在编辑的配置：{Mod.Name}");
            }
            else
            {
                _notifications?.Show("无法加入配置，可能尚未选择配置或该 Mod 已存在。", NotificationLevel.Info);
            }
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
                ? $"未加入活动配置：{active.Name}"
                : $"已启用在活动配置：{active.Name}，顺序 {entry.LoadOrder}";
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
            var graph = _derivedState.Snapshot.ExpectedGraph;
            if (graph is null || graph.ProfileId != active.Id || graph.ProfileRevision != active.Revision) return "冲突数据正在后台更新。";
            var overlaps = graph.ArchiveOverlaps.Where(overlap => overlap.NodeIds.Contains(nodeId)).ToList();
            return overlaps.Count == 0 ? "未发现与当前配置的潜在重叠" : $"与 {overlaps.Sum(overlap => overlap.NodeIds.Count - 1)} 个 Mod 存在 {overlaps.Count} 个 archive 级潜在重叠";
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

        private static void RunOnUiThread(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else _ = dispatcher.InvokeAsync(action);
        }
    }

}
