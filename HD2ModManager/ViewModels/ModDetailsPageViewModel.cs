using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;
using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：展示单个 Mod 的派生信息与文件组概览。
    public class ModDetailsPageViewModel : PageViewModel
    {
        private readonly ModLibraryService _library;
        private readonly ProfileService _profiles;
        private readonly DerivedStateCoordinator _derivedState;
        private readonly NotificationService? _notifications;
        private readonly IMaterialPackagingApplicationService _materialPackaging;
        private readonly IMaterialDeliveryFactsService _materialDeliveryFacts;
		private readonly IModSameKeyReconstructionService _sameKeyReconstruction;
        private readonly IEquipmentUnitCatalogService _equipmentUnitCatalog;
        private ModMaterialPackagingState? _materialState;
        private bool _materialOperationRunning;
		private bool _sameKeyReconstructionRunning;
        private readonly IAdvancedModAssetQueryService _advancedAssetQueryService;
        private readonly EventHandler<DerivedStateSnapshot> _snapshotChangedHandler;
        private CancellationTokenSource? _advancedDetailsCancellation;
        private bool _advancedDetailsLoaded;
        private bool _disposed;
        private IReadOnlyList<AdvancedModAssetRow> _allAdvancedAssets = Array.Empty<AdvancedModAssetRow>();
        private string _advancedAssetQuery = string.Empty;
        private bool _advancedOnlyIssues;

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
		public string MaterialDiagnosticSummary { get; private set; } = "当前没有活动配置或材质诊断正在更新。";
        public string UserStatusTitle { get; private set; } = "状态未知";
        public string UserStatusSummary { get; private set; } = "正在读取状态。";
        public string MaterialPackagingSummary { get; private set; } = "在高级详情中按需读取稳定材质事实。";
        public string MaterialDeliverySummary { get; private set; } = "在高级详情中按需读取稳定材质交付事实。";
        public string SameKeyReconstructionSummary { get; private set; } = "仅更新失效 Unit：删除旧 Unit/关联旧 Composite，写入 current target Unit，并原样保留其余资源与 sidecar。不分析或重组材质。";
        public bool CanSplitEmbeddedMaterials => !_materialOperationRunning && _materialState?.CanSplit == true;
        public bool CanReplaceEmbeddedMaterials => !_materialOperationRunning && _materialState?.HasEmbeddedMaterials == true;
        public bool CanEmbedExternalMaterials => !_materialOperationRunning && _materialState?.HasExternalMaterials == true;
        public bool CanRebuildSameKey => !_sameKeyReconstructionRunning && !_disposed && TryGetCurrentNode(out _);
		public bool CanPlanCrossArmorTransfer => !_disposed && TryGetCurrentNode(out _);
        public ObservableCollection<AdvancedModAssetRowViewModel> AdvancedAssets { get; } = new();
        public string AdvancedAssetQuery { get => _advancedAssetQuery; set { if (SetField(ref _advancedAssetQuery, value)) ApplyAdvancedAssetFilter(); } }
        public bool AdvancedOnlyIssues { get => _advancedOnlyIssues; set { if (SetField(ref _advancedOnlyIssues, value)) ApplyAdvancedAssetFilter(); } }
        public string AdvancedAssetState { get; private set; } = "正在加载稳定资产事实。";
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
        public RelayCommand RebuildSameKeyCommand { get; }
		public RelayCommand PlanCrossArmorTransferCommand { get; }

        public ModDetailsPageViewModel(ModLibraryService library, ProfileService profiles, DerivedStateCoordinator derivedState, string modId, NotificationService? notifications = null)
        {
            Title = "Mod 详情";
            _library = library;
            _profiles = profiles;
            _derivedState = derivedState;
            _notifications = notifications;
			_materialPackaging = CoreServices.CreateMaterialPackagingApplicationService();
            _materialDeliveryFacts = CoreServices.CreateMaterialDeliveryFactsService(SettingsService.CreateStoragePaths());
			_sameKeyReconstruction = CoreServices.CreateModSameKeyReconstructionService(SettingsService.CreateStoragePaths());
            _equipmentUnitCatalog = CoreServices.CreateEquipmentUnitCatalogService(SettingsService.CreateStoragePaths());
            _advancedAssetQueryService = CoreServices.CreateAdvancedModAssetQueryService(SettingsService.CreateStoragePaths());
            ModId = modId;
            RefreshCommand = new RelayCommand(Refresh);
            OpenFolderCommand = new RelayCommand(OpenFolder);
            AddToProfileCommand = new RelayCommand(AddToProfile);
            DeleteCommand = new RelayCommand(Delete);
            OpenAdvancedDetailsCommand = new RelayCommand(OpenAdvancedDetails);
            SplitEmbeddedMaterialsCommand = new RelayCommand(async _ => await SplitEmbeddedMaterialsAsync(), _ => CanSplitEmbeddedMaterials);
            ReplaceEmbeddedMaterialsCommand = new RelayCommand(async _ => await MergeMaterialCandidateAsync(requireAllExternalMaterials: false), _ => CanReplaceEmbeddedMaterials);
            EmbedExternalMaterialsCommand = new RelayCommand(async _ => await MergeMaterialCandidateAsync(requireAllExternalMaterials: true), _ => CanEmbedExternalMaterials);
            RebuildSameKeyCommand = new RelayCommand(async _ => await RebuildSameKeyAsync(), _ => CanRebuildSameKey);
			PlanCrossArmorTransferCommand = new RelayCommand(async _ => await PlanCrossArmorTransferAsync(), _ => CanPlanCrossArmorTransfer);
            _snapshotChangedHandler = (_, _) => RunOnUiThread(() =>
            {
                if (_disposed) return;
                Refresh();
                if (_advancedDetailsLoaded) _ = RefreshAdvancedDetailsAsync();
            });
            _derivedState.SnapshotChanged += _snapshotChangedHandler;
            Refresh();
        }

        public async Task RefreshAdvancedDetailsAsync()
        {
            if (_disposed) return;
            _advancedDetailsLoaded = true;
            _advancedDetailsCancellation?.Cancel();
            _advancedDetailsCancellation?.Dispose();
            _advancedDetailsCancellation = new CancellationTokenSource();
            var cancellationToken = _advancedDetailsCancellation.Token;
            try
            {
                await Task.WhenAll(
                    RefreshAdvancedAssetsAsync(cancellationToken),
                    RefreshMaterialPackagingStateAsync(cancellationToken),
                    RefreshMaterialDeliveryFactsAsync(cancellationToken)).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // The advanced window or its owning detail page was closed.
            }
        }

        public void CancelAdvancedDetails()
        {
            _advancedDetailsLoaded = false;
            _advancedDetailsCancellation?.Cancel();
        }

        private async Task RefreshAdvancedAssetsAsync(CancellationToken cancellationToken)
        {
            if (Mod is null || !TryParseNodeId(Mod.Guid, out var nodeId)) return;
            try
            {
                var active = _profiles.ActiveProfile;
                var graph = active is null ? null : _derivedState.Snapshot.ExpectedGraph;
                var diagnostics = active is null ? null : _derivedState.Snapshot.MaterialDiagnostics;
                _allAdvancedAssets = await _advancedAssetQueryService.QueryAsync(nodeId, _library.Snapshot, graph, diagnostics, cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested) return;
                AdvancedAssetState = _allAdvancedAssets.Count == 0 ? "尚未生成稳定资产事实；请等待导入分析完成。" : $"共 {_allAdvancedAssets.Count} 个 AssetKey（稳定事实）";
                ApplyAdvancedAssetFilter();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                _allAdvancedAssets = Array.Empty<AdvancedModAssetRow>();
                AdvancedAssetState = $"稳定资产事实读取失败：{exception.Message}";
                ApplyAdvancedAssetFilter();
            }
        }

        private void ApplyAdvancedAssetFilter()
        {
            var query = AdvancedAssetQuery.Trim();
            var rows = _allAdvancedAssets
                .Where(row => !AdvancedOnlyIssues || !string.IsNullOrWhiteSpace(row.DiagnosticSummary))
                .Where(row => string.IsNullOrWhiteSpace(query)
                    || row.TypeName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.ResourceName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.TargetSummary.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || row.AssetKey.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(row => new AdvancedModAssetRowViewModel(row));
            AdvancedAssets.Clear();
            foreach (var row in rows) AdvancedAssets.Add(row);
            OnPropertyChanged(nameof(AdvancedAssetState));
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
			OnPropertyChanged(nameof(MaterialDiagnosticSummary));
            OnPropertyChanged(nameof(UserStatusTitle));
            OnPropertyChanged(nameof(UserStatusSummary));
            OnPropertyChanged(nameof(MaterialPackagingSummary));
			OnPropertyChanged(nameof(MaterialDeliverySummary));
            OnPropertyChanged(nameof(SameKeyReconstructionSummary));
			OnPropertyChanged(nameof(CanPlanCrossArmorTransfer));
            RaiseMaterialCommandStates();
            RaiseSameKeyReconstructionCommandState();
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
                var facts = derived?.ContentFacts ?? _derivedState.Snapshot.ContentFacts.GetValueOrDefault(nodeId);
                if (facts is not null)
                {
                    var assets = facts.PatchGroups.SelectMany(group => group.AssetKeys).ToArray();
                    AssetTagsString = "稳定 Patch 事实";
                    AssetListSummary = assets.Length == 0
                        ? "未发现可解析资产"
                        : $"共 {assets.Length} 个 AssetKey：Unit {assets.Count(asset => asset.TypeId == 0xe0a48d0be9a7453f)}，Material {assets.Count(asset => asset.TypeId == 0xeac0b497876adedf)}，Texture {assets.Count(asset => asset.TypeId == 0xcd4238c6a0c69e32)}。高级详情可查看完整稳定资产表。";
                }
                else
                {
                    AssetTagsString = "资产未解析";
                    AssetListSummary = "稳定资产事实正在导入分析。";
                }
                AssetOverrideSummary = BuildCachedAssetOverrideSummary(nodeId);
                return;
            }
            AssetTagsString = BuildAssetTagTreeText(summary);
            AssetListSummary = summary.Assets.Count == 0
                ? "未发现可解析资产"
                : string.Join(Environment.NewLine, summary.Assets.Take(80).Select(a => a.DisplayName));
            if (summary.Assets.Count > 80) AssetListSummary += Environment.NewLine + $"... 另有 {summary.Assets.Count - 80} 个资产";
            AssetOverrideSummary = BuildCachedAssetOverrideSummary(nodeId);
            MaterialDiagnosticSummary = BuildMaterialDiagnosticSummary(nodeId);
        }

        private string BuildMaterialDiagnosticSummary(ModNodeId nodeId)
        {
            var active = _profiles.ActiveProfile;
            if (active is null) return "当前没有活动配置。";
            var diagnostics = _derivedState.Snapshot.MaterialDiagnostics;
            if (diagnostics is null || diagnostics.ProfileId != active.Id || diagnostics.ProfileRevision != active.Revision) return "材质诊断正在后台更新。";
            var items = diagnostics.Items.Where(item => item.NodeId == nodeId).ToArray();
            return items.Length == 0
                ? "未发现当前有效资源图中的材质依赖异常。"
                : string.Join(Environment.NewLine, items.Take(20).Select(item => $"{MaterialDiagnosticPrefix(item.Kind)}{item.Summary}：{item.Detail}")) + (items.Length > 20 ? $"{Environment.NewLine}... 另有 {items.Length - 20} 项" : string.Empty);
        }

        private static string MaterialDiagnosticPrefix(ProfileMaterialDiagnosticKind kind) => kind switch
        {
            ProfileMaterialDiagnosticKind.CurrentGameMaterialFallback => "✓ ",
            ProfileMaterialDiagnosticKind.CurrentGameMaterialCandidate => "⚠ ",
            _ => string.Empty
        };

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
            if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
            {
                shell.OpenAdvancedModDetails(ModId);
            }
        }

        private async Task RefreshMaterialPackagingStateAsync(CancellationToken cancellationToken)
        {
            if (Mod == null || !TryParseNodeId(Mod.Guid, out var nodeId) || !_library.Snapshot.Nodes.TryGetValue(nodeId, out var node)) return;
            try
            {
                _materialState = await _materialPackaging.InspectAsync(node, _library.ModsRootDirectory, cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested) return;
                MaterialPackagingSummary = $"需要材质 {_materialState.RequiredMaterialCount}；内嵌 {_materialState.EmbeddedMaterialCount}；外部 {_materialState.ExternalMaterialCount}；内嵌贴图 {_materialState.EmbeddedTextureCount}";
                if (_materialState.Blockers.Count != 0) MaterialPackagingSummary += Environment.NewLine + string.Join(Environment.NewLine, _materialState.Blockers);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                _materialState = null;
                MaterialPackagingSummary = $"材质分析失败：{exception.Message}";
            }
            RunOnUiThread(() => { OnPropertyChanged(nameof(MaterialPackagingSummary)); RaiseMaterialCommandStates(); });
        }

        private async Task RefreshMaterialDeliveryFactsAsync(CancellationToken cancellationToken)
        {
            if (Mod is null || !TryParseNodeId(Mod.Guid, out var nodeId)) return;
            try
            {
                var facts = await _materialDeliveryFacts.GetAsync(nodeId, _library.Snapshot, cancellationToken);
                if (_disposed || cancellationToken.IsCancellationRequested) return;
                var lines = new List<string>
                {
                    $"交付模式：{MaterialDeliveryModeName(facts.Mode)}",
                    $"Unit {facts.UnitCount}；需要材质 {facts.RequiredMaterialCount}；内嵌 {facts.EmbeddedMaterialCount}；外部 {facts.ExternalMaterialCount}；缺失内嵌贴图 {facts.MissingEmbeddedTextureCount}"
                };
                if (facts.Candidates.Count != 0)
                {
                    lines.Add("库内材质候选：" + string.Join("；", facts.Candidates.Take(3).Select(candidate => $"{candidate.Name}（覆盖 {candidate.CoveredMaterialCount}，缺贴图 {candidate.MissingTextureCount}{(candidate.IsComplete ? "，完整" : string.Empty)}）")) + (facts.Candidates.Count > 3 ? $"；另有 {facts.Candidates.Count - 3} 个" : string.Empty));
                }
                lines.AddRange(facts.Notices);
                MaterialDeliverySummary = string.Join(Environment.NewLine, lines);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                MaterialDeliverySummary = $"稳定材质交付事实读取失败：{exception.Message}";
            }
            RunOnUiThread(() => OnPropertyChanged(nameof(MaterialDeliverySummary)));
        }

        private static string MaterialDeliveryModeName(MaterialDeliveryMode mode) => mode switch
        {
            MaterialDeliveryMode.NoMaterialDependencies => "无材质依赖",
            MaterialDeliveryMode.EmbeddedComplete => "内嵌闭包完整（整体重建）",
            MaterialDeliveryMode.EmbeddedIncomplete => "内嵌闭包不完整",
            MaterialDeliveryMode.ExternalResolved => "外部材质已解析（仅重建模型）",
            MaterialDeliveryMode.ExternalUnresolved => "外部材质未解析",
            MaterialDeliveryMode.Mixed => "内嵌与外部混用",
            _ => "未知"
        };

        private async Task RebuildSameKeyAsync()
        {
            if (!TryGetCurrentNode(out var source)) return;
            var output = new HD2ModManager.Views.SameKeyReconstructionOutputWindow { Owner = System.Windows.Application.Current?.MainWindow };
            if (output.ShowDialog() != true) return;
            _sameKeyReconstructionRunning = true;
            RaiseSameKeyReconstructionCommandState();
            try
            {
                _notifications?.Show("正在读取写出所需 Payload 并生成 current-target 验证候选，请勿关闭程序…", NotificationLevel.Info, TimeSpan.FromSeconds(20));
                var result = await Task.Run(() => _sameKeyReconstruction.GenerateCandidateAsync(source, _library.ModsRootDirectory, SettingsService.GetGameDataFolder(), output.OutputDirectory).AsTask());
                if (!result.IsSuccessful)
                {
                    _notifications?.Show(string.Join("；", result.Issues.Select(issue => issue.Message).Take(3)), NotificationLevel.Error, TimeSpan.FromSeconds(10));
                    return;
                }
                if (output.ImportToLibrary && result.OutputDirectory is not null)
                {
                    await new ImportService(_library).ImportPathAsync(result.OutputDirectory, default);
                }
                _notifications?.Show($"正式验证候选已生成：Unit {result.OutputUnitCount}；替换 mesh {result.ReplacementMeshCount}；极小化 mesh {result.MinifiedMeshCount}。其余资源与 sidecar 已保留。输出目录包含 formal-validation-checklist.md。{(output.ImportToLibrary ? "已导入 Mod 库。" : "已写出到目标文件夹。")}", NotificationLevel.Info, TimeSpan.FromSeconds(10));
            }
			catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
			{
				_notifications?.Show($"生成验证候选失败：{exception.Message}", NotificationLevel.Error, TimeSpan.FromSeconds(12));
			}
            finally
            {
                _sameKeyReconstructionRunning = false;
                RaiseSameKeyReconstructionCommandState();
            }
        }

        private async Task PlanCrossArmorTransferAsync()
        {
            if (!TryGetCurrentNode(out var source)) return;
            try
            {
                _notifications?.Show("正在读取当前 Mod 的 Unit 事实与装备目录…", NotificationLevel.Info, TimeSpan.FromSeconds(8));
                var facts = await CoreServices.CreateModContentFactsService(SettingsService.CreateStoragePaths())
                    .GetNodeFactsAsync(source, _library.ModsRootDirectory);
                var unitKeys = facts.PatchGroups.SelectMany(group => group.AssetKeys)
                    .Where(asset => asset.TypeId == 0xe0a48d0be9a7453f).ToHashSet();
                var sourceCatalogCandidates = await _equipmentUnitCatalog.GetEntriesAsync(unitKeys);
                var sourcePatchPaths = facts.PatchGroups
                    .SelectMany(group => group.Files)
                    .Where(file => file.SidecarKind == PatchSidecarKind.Base)
                    .Select(file => file.FilePath)
                    .ToArray();
                var sourceCandidates = await _equipmentUnitCatalog.FilterTransferableSourcePartsAsync(sourceCatalogCandidates, sourcePatchPaths);
                var allCandidates = await _equipmentUnitCatalog.GetEntriesAsync();
                if (sourceCandidates.Count == 0)
                {
                    _notifications?.Show("当前 Mod 没有可转移的真实 Unit 几何：索引未匹配、Unit 无法读取或所有匹配 mesh 均已极小化。", NotificationLevel.Info, TimeSpan.FromSeconds(10));
                    return;
                }
                if (sourcePatchPaths.Length != 1)
                {
                    _notifications?.Show("跨护甲验证候选目前仅支持源 Mod 含一个 Patch 主文件组。", NotificationLevel.Info, TimeSpan.FromSeconds(10));
                    return;
                }
                var viewModel = new HD2ModManager.Views.CrossArmorTransferPlanWindowViewModel(_equipmentUnitCatalog, sourceCandidates, allCandidates, sourcePatchPaths[0], SettingsService.GetGameDataFolder());
                if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell) shell.OpenCrossArmorPlan(viewModel);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                _notifications?.Show($"读取跨护甲计划失败：{exception.Message}", NotificationLevel.Error, TimeSpan.FromSeconds(12));
            }
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
                await RefreshAdvancedDetailsAsync();
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

        private void RaiseSameKeyReconstructionCommandState()
        {
            OnPropertyChanged(nameof(CanRebuildSameKey));
			OnPropertyChanged(nameof(CanPlanCrossArmorTransfer));
            RebuildSameKeyCommand.RaiseCanExecuteChanged();
			PlanCrossArmorTransferCommand.RaiseCanExecuteChanged();
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

        public override void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _derivedState.SnapshotChanged -= _snapshotChangedHandler;
            _advancedDetailsCancellation?.Cancel();
            _advancedDetailsCancellation?.Dispose();
            _advancedDetailsCancellation = null;
        }
    }

}
