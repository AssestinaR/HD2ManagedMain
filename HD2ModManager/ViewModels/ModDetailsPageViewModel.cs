using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HD2ModAdaptation.Analysis;
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
        private readonly IAdvancedModAnalysisService _advancedAnalysis;
        private readonly IPatchGraphDiagnosticsService _patchGraphDiagnostics;
        private ModMaterialPackagingState? _materialState;
		private bool _sameKeyReconstructionRunning;
        private bool _advancedAnalysisRunning;
        private bool _advancedAnalysisReady;
		private bool _advancedAnalysisHasEquipment;
		private bool _dependencyGraphTestRunning;
        private bool _dependencyGraphComparisonRunning;
        private readonly IAdvancedModAssetQueryService _advancedAssetQueryService;
		private readonly StoragePaths _paths;
        private readonly EventHandler<DerivedStateSnapshot> _snapshotChangedHandler;
        private CancellationTokenSource? _advancedDetailsCancellation;
        private bool _advancedDetailsLoaded;
        private bool _disposed;
        private IReadOnlyList<AdvancedModAssetRow> _allAdvancedAssets = Array.Empty<AdvancedModAssetRow>();
        private ModContentFacts? _detailContentFacts;
        private string _advancedAssetQuery = string.Empty;
        private bool _advancedOnlyIssues;
        private int _detailRequestGeneration;

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
        public string UnitCompatibilitySummary { get; private set; } = "模型版本尚未检测。";
        public bool IsModelOutdated { get; private set; }
        public string DataIndexSummary { get; private set; } = "跨 Mod 资产索引尚未读取。";
        public string UserStatusTitle { get; private set; } = "状态未知";
        public string UserStatusSummary { get; private set; } = "正在读取状态。";
        public string MaterialPackagingSummary { get; private set; } = "材质操作基于导入后的轻量引用图；无需执行高级分析。";
        public string MaterialDeliverySummary { get; private set; } = "导入后的轻量引用图完成后即可读取材质交付事实。";
        public string SameKeyReconstructionSummary { get; private set; } = "仅更新失效 Unit，并将结果直接写入 Manager 的 Output 文件夹；不会自动导入或部署。";
		public string AdvancedAnalysisSummary { get; private set; } = "正在检查高级缓存。";
        public string DependencyGraphTestSummary { get; private set; } = "仅读取 Unit 材质绑定表与 Material 贴图表；结果会写入 logs。";
        public string DependencyGraphComparisonSummary { get; private set; } = "可用完整 Unit 解析对比轻量引用链的去重关系集合。";
        public bool CanRunAdvancedAnalysis => !_disposed && !_advancedAnalysisRunning && HasPatchGroups && TryGetCurrentNode(out _);
		public bool CanRunDependencyGraphTest => !_disposed && !_dependencyGraphTestRunning && TryGetCurrentNode(out _);
        public bool CanCompareDependencyGraph => !_disposed && !_dependencyGraphComparisonRunning && TryGetCurrentNode(out _);
        public bool CanSplitEmbeddedMaterials => !_disposed && _materialState is { HasEmbeddedMaterials: true } && TryGetCurrentNode(out _);
        public bool CanReplaceEmbeddedMaterials => !_disposed && TryGetCurrentNode(out _);
        public bool CanEmbedExternalMaterials => !_disposed && TryGetCurrentNode(out _);
        public bool CanRebuildSameKey => !_disposed && !_sameKeyReconstructionRunning && _advancedAnalysisReady && _advancedAnalysisHasEquipment && TryGetCurrentNode(out _);
        public bool CanPlanCrossArmorTransfer => !_disposed && _advancedAnalysisReady && _advancedAnalysisHasEquipment && TryGetCurrentNode(out _);
        private bool HasPatchGroups => Mod?.FileGroups?.Count > 0;
        public ObservableCollection<AdvancedModAssetRowViewModel> AdvancedAssets { get; } = new();
        public string AdvancedAssetQuery { get => _advancedAssetQuery; set { if (SetField(ref _advancedAssetQuery, value)) ApplyAdvancedAssetFilter(); } }
        public bool AdvancedOnlyIssues { get => _advancedOnlyIssues; set { if (SetField(ref _advancedOnlyIssues, value)) ApplyAdvancedAssetFilter(); } }
        public string AdvancedAssetState { get; private set; } = "正在加载稳定资产事实。";
        public string PatchSummary => Mod?.FileGroups == null || Mod.FileGroups.Count == 0
            ? "没有 patch 文件组"
            : string.Join(Environment.NewLine, Mod.FileGroups.Select(g => $"{g.HexPrefix}.patch_{g.PatchN}"));

        public RelayCommand RefreshCommand { get; }
        public RelayCommand UpdateImageCommand { get; }
        public RelayCommand OpenFolderCommand { get; }
        public RelayCommand AddToProfileCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand OpenAdvancedDetailsCommand { get; }
        public RelayCommand SplitEmbeddedMaterialsCommand { get; }
        public RelayCommand ReplaceEmbeddedMaterialsCommand { get; }
        public RelayCommand EmbedExternalMaterialsCommand { get; }
        public RelayCommand RebuildSameKeyCommand { get; }
		public RelayCommand PlanCrossArmorTransferCommand { get; }
        public RelayCommand RunAdvancedAnalysisCommand { get; }
		public RelayCommand RunDependencyGraphTestCommand { get; }
        public RelayCommand CompareDependencyGraphCommand { get; }

        public ModDetailsPageViewModel(ModLibraryService library, ProfileService profiles, DerivedStateCoordinator derivedState, string modId, NotificationService? notifications = null)
        {
            Title = "Mod 详情";
            _library = library;
            _profiles = profiles;
            _derivedState = derivedState;
            _notifications = notifications;
            _paths = SettingsService.CreateStoragePaths();
			_materialPackaging = CoreServices.CreateMaterialPackagingApplicationService();
            _materialDeliveryFacts = CoreServices.CreateMaterialDeliveryFactsService(_paths, _derivedState.InformationCenter);
            _sameKeyReconstruction = CoreServices.CreateModSameKeyReconstructionService(_paths, _derivedState.InformationCenter);
            _equipmentUnitCatalog = CoreServices.CreateEquipmentUnitCatalogService(_paths);
            _advancedAnalysis = CoreServices.CreateAdvancedModAnalysisService(_paths, _derivedState.InformationCenter);
            _patchGraphDiagnostics = CoreServices.CreatePatchGraphDiagnosticsService();
            _advancedAssetQueryService = CoreServices.CreateAdvancedModAssetQueryService(_paths, _derivedState.InformationCenter);
            ModId = modId;
            RefreshCommand = new RelayCommand(Refresh);
            UpdateImageCommand = new RelayCommand(UpdateImage, path => path is string imagePath && File.Exists(imagePath));
            OpenFolderCommand = new RelayCommand(OpenFolder);
            AddToProfileCommand = new RelayCommand(AddToProfile);
            DeleteCommand = new RelayCommand(Delete);
            OpenAdvancedDetailsCommand = new RelayCommand(OpenAdvancedDetails);
            SplitEmbeddedMaterialsCommand = new RelayCommand(_ => OpenMaterialPackaging(splitEmbeddedMaterials: true), _ => CanSplitEmbeddedMaterials);
            ReplaceEmbeddedMaterialsCommand = new RelayCommand(_ => OpenMaterialPackaging(splitEmbeddedMaterials: false, requireAllExternalMaterials: false), _ => CanReplaceEmbeddedMaterials);
            EmbedExternalMaterialsCommand = new RelayCommand(_ => OpenMaterialPackaging(splitEmbeddedMaterials: false, requireAllExternalMaterials: true), _ => CanEmbedExternalMaterials);
            RebuildSameKeyCommand = new RelayCommand(async _ => await RebuildSameKeyAsync(), _ => CanRebuildSameKey);
			PlanCrossArmorTransferCommand = new RelayCommand(async _ => await PlanCrossArmorTransferAsync(), _ => CanPlanCrossArmorTransfer);
            RunAdvancedAnalysisCommand = new RelayCommand(async _ => await RunAdvancedAnalysisAsync(), _ => CanRunAdvancedAnalysis);
			RunDependencyGraphTestCommand = new RelayCommand(async _ => await RunDependencyGraphTestAsync(), _ => CanRunDependencyGraphTest);
			CompareDependencyGraphCommand = new RelayCommand(async _ => await CompareDependencyGraphAsync(), _ => CanCompareDependencyGraph);
            _snapshotChangedHandler = (_, _) => RunOnUiThread(() =>
            {
                if (_disposed) return;
                Refresh();
                _ = RefreshInformationProductsAsync();
                if (_advancedDetailsLoaded) _ = RefreshAdvancedDetailsAsync();
            });
            _derivedState.SnapshotChanged += _snapshotChangedHandler;
            Refresh();
			_ = RefreshInformationProductsAsync();
			_ = RefreshAdvancedAnalysisStateAsync();
        }

        private async Task RefreshInformationProductsAsync()
        {
            if (_disposed || !TryGetCurrentNode(out var node)) return;
            try
            {
                var assetInventory = await _derivedState.InformationCenter.RequestAssetInventoryAsync(
                    node,
                    _library.ModsRootDirectory,
                    new ModInformationRequest(ModInformationKind.AssetInventory, "ModDetails"));
                var unitVersion = await _derivedState.InformationCenter.RequestUnitVersionAsync(
                    node,
                    _library.ModsRootDirectory,
                    new ModInformationRequest(ModInformationKind.UnitVersion, "ModDetails"));
                RunOnUiThread(() =>
                {
                    if (_disposed) return;
                    _detailContentFacts = assetInventory.Data;
                    if (unitVersion.Data is { } facts)
                    {
                        UnitCompatibilitySummary = facts.Report.Summary;
                        IsModelOutdated = facts.Report.IsOutdated;
                    }
                    else
                    {
                        UnitCompatibilitySummary = unitVersion.Status == ModInformationStatus.Unavailable
                            ? "模型版本检测不可用。"
                            : "模型版本检测失败。";
                        IsModelOutdated = false;
                    }
                    OnPropertyChanged(nameof(UnitCompatibilitySummary));
                    OnPropertyChanged(nameof(IsModelOutdated));
                    RefreshAssetStatus();
                });
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                RunOnUiThread(() =>
                {
                    if (_disposed) return;
                    UnitCompatibilitySummary = $"模型版本检测失败：{exception.Message}";
                    IsModelOutdated = false;
                    OnPropertyChanged(nameof(UnitCompatibilitySummary));
                    OnPropertyChanged(nameof(IsModelOutdated));
                });
            }
        }

        private void UpdateImage(object? parameter)
        {
            if (parameter is not string sourceImagePath || Mod is null) return;

            var modDirectory = _library.ResolveAbsolutePath(Mod.SourcePath);
            if (string.IsNullOrWhiteSpace(modDirectory) || !Directory.Exists(modDirectory)) return;

            var destination = Path.Combine(modDirectory, "icon" + Path.GetExtension(sourceImagePath).ToLowerInvariant());
            File.Copy(sourceImagePath, destination, overwrite: true);
            Mod.Image = destination;
            _library.Add(Mod);
            _library.Save();
            if (TryGetCurrentNode(out var updatedNode))
                _ = _derivedState.InformationCenter.InvalidateNodeAsync(updatedNode.Id);
            Refresh();
            _notifications?.Show($"已更新图像：{Mod.Name}");
            _ = RegenerateThumbnailAsync(destination);
        }

        private async Task RegenerateThumbnailAsync(string imagePath)
        {
            try
            {
                if (TryGetCurrentNode(out var node))
                {
                    var facts = await _library.RequestThumbnailAsync(ModId, "UserRefresh", requireFresh: true).ConfigureAwait(false);
                    if (facts.Data is { } thumbnailFacts)
                        await ThumbnailService.EnsureThumbnailAsync(thumbnailFacts, 72).ConfigureAwait(false);
                }
                await _derivedState.RefreshAsync().ConfigureAwait(false);
            }
            catch
            {
                // 图像已经保存；缩略图失败时由下次库页刷新重试。
            }
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
                    RefreshMaterialDeliveryFactsAsync(cancellationToken),
                    RefreshMaterialPackagingStateAsync(cancellationToken)).ConfigureAwait(false);
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

        private async Task RefreshAdvancedAnalysisStateAsync()
        {
            if (!TryGetCurrentNode(out var node)) return;
            try
            {
                var state = await _advancedAnalysis.GetCachedStateAsync(node, _library.ModsRootDirectory);
                if (_disposed) return;
                _advancedAnalysisReady = state.IsReady;
				if (state.IsReady) await RefreshAdvancedEquipmentStateAsync(node).ConfigureAwait(false);
				else _advancedAnalysisHasEquipment = false;
                AdvancedAnalysisSummary = state.IsReady
                    ? $"高级缓存已就绪：{state.BuiltUtc:yyyy-MM-dd HH:mm:ss}。"
                    : "尚未执行高级分析。Unit 更新和更换护甲计划已禁用；材质操作可直接使用轻量引用图。";
            }
            catch (Exception exception)
            {
                _advancedAnalysisReady = false;
				_advancedAnalysisHasEquipment = false;
                AdvancedAnalysisSummary = $"高级缓存状态读取失败：{exception.Message}";
            }
            RunOnUiThread(() =>
            {
                OnPropertyChanged(nameof(AdvancedAnalysisSummary));
                OnPropertyChanged(nameof(CanRunAdvancedAnalysis));
                RaiseMaterialCommandStates();
                RaiseSameKeyReconstructionCommandState();
            });
        }

        private async Task RunAdvancedAnalysisAsync()
        {
            if (!TryGetCurrentNode(out var node)) return;
            await RunOnUiThreadAsync(() =>
            {
                _advancedAnalysisRunning = true;
                AdvancedAnalysisSummary = "正在读取 Unit 完整结构、材质和贴图引用…";
                OnPropertyChanged(nameof(AdvancedAnalysisSummary));
                OnPropertyChanged(nameof(CanRunAdvancedAnalysis));
                RunAdvancedAnalysisCommand.RaiseCanExecuteChanged();
            });
            try
            {
                var result = await Task.Run(() => _advancedAnalysis.AnalyzeAsync(node, _library.ModsRootDirectory).AsTask());
                await RunOnUiThreadAsync(() =>
                {
                    AdvancedAnalysisSummary = result.Issues.Count == 0
                        ? $"高级分析完成：{result.BuiltUtc:yyyy-MM-dd HH:mm:ss}。"
                        : $"高级分析完成，但发现 {result.Issues.Count} 项读取提醒。";
                    _advancedAnalysisReady = result.IsReady;
                });
                await RefreshAdvancedEquipmentStateAsync(node).ConfigureAwait(false);
                await RefreshAdvancedDetailsAsync();
            }
            catch (Exception exception)
            {
                await RunOnUiThreadAsync(() =>
                {
                    _advancedAnalysisReady = false;
					_advancedAnalysisHasEquipment = false;
                    AdvancedAnalysisSummary = $"高级分析失败：{exception.Message}";
                    _notifications?.Show(AdvancedAnalysisSummary, NotificationLevel.Error, TimeSpan.FromSeconds(10));
                });
            }
            finally
            {
                await RunOnUiThreadAsync(() =>
                {
                    _advancedAnalysisRunning = false;
                    OnPropertyChanged(nameof(AdvancedAnalysisSummary));
                    OnPropertyChanged(nameof(CanRunAdvancedAnalysis));
                    RunAdvancedAnalysisCommand.RaiseCanExecuteChanged();
                    RaiseMaterialCommandStates();
                    RaiseSameKeyReconstructionCommandState();
                });
            }
        }

        private async Task RefreshAdvancedEquipmentStateAsync(ModNode node)
        {
            try
            {
                var analyses = await _advancedAnalysis.GetRequiredAnalysesAsync(node, _library.ModsRootDirectory).ConfigureAwait(false);
                var unitKeys = analyses.SelectMany(analysis => analysis.Assets)
                    .Where(asset => asset.AssetKey.TypeId == 0xe0a48d0be9a7453f)
                    .Select(asset => new AssetKey(asset.AssetKey.TypeId, asset.AssetKey.FileId))
                    .ToHashSet();
                var entries = await _equipmentUnitCatalog.GetEntriesAsync(unitKeys).ConfigureAwait(false);
                _advancedAnalysisHasEquipment = entries.Any(entry => string.Equals(entry.Category, "Armor", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(entry.Category, "Helmet", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                _advancedAnalysisHasEquipment = false;
            }
        }

        private async Task RunDependencyGraphTestAsync()
        {
            if (!TryGetCurrentNode(out var node)) return;
            var requestGeneration = _detailRequestGeneration;
            _dependencyGraphTestRunning = true;
            DependencyGraphTestSummary = "正在执行轻量引用链测试…";
            OnPropertyChanged(nameof(DependencyGraphTestSummary));
            OnPropertyChanged(nameof(CanRunDependencyGraphTest));
            RunDependencyGraphTestCommand.RaiseCanExecuteChanged();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var analyses = await Task.Run(() => _patchGraphDiagnostics.AnalyzeDependencyGraphAsync(node, _library.ModsRootDirectory).AsTask());
                if (!IsCurrentDetailRequest(node, requestGeneration)) return;
                stopwatch.Stop();
                var assets = analyses.Sum(analysis => analysis.Assets.Count);
                var unitMaterials = analyses.Sum(analysis => analysis.References.Count(reference => reference.Kind == HD2ModAdaptation.Analysis.PatchReferenceKind.UnitMaterial));
                var materialTextures = analyses.Sum(analysis => analysis.References.Count(reference => reference.Kind == HD2ModAdaptation.Analysis.PatchReferenceKind.MaterialTexture));
                var issues = analyses.SelectMany(analysis => analysis.Issues).ToArray();
                var reportPath = WriteDependencyGraphTestReport(node, analyses, stopwatch.Elapsed, issues);
                var health = AssessDependencyGraph(analyses);
                DependencyGraphTestSummary = $"轻量引用链{(health.IsNormal ? "正常" : "发现异常")}：{assets} 个资源，Unit→Material {unitMaterials} 条，Material→Texture {materialTextures} 条，耗时 {stopwatch.ElapsedMilliseconds} ms。已写入 JSON 报告。";
                LogService.Info($"轻量引用链测试：Mod={node.Metadata.Name} ({node.Id.Value:N})，Patch组={analyses.Count}，资源={assets}，Unit→Material={unitMaterials}，Material→Texture={materialTextures}，问题={issues.Length}，耗时={stopwatch.ElapsedMilliseconds}ms。JSON={reportPath}。详情页未写入缓存。" + (issues.Length == 0 ? string.Empty : $" 问题：{string.Join(" | ", issues.Take(10).Select(issue => $"{issue.Code}: {issue.Message}"))}"));
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                DependencyGraphTestSummary = $"轻量引用链测试失败：{exception.Message}";
                LogService.Error($"轻量引用链测试失败：Mod={node.Metadata.Name}，耗时={stopwatch.ElapsedMilliseconds}ms，错误={exception}");
            }
            finally
            {
                _dependencyGraphTestRunning = false;
                OnPropertyChanged(nameof(DependencyGraphTestSummary));
                OnPropertyChanged(nameof(CanRunDependencyGraphTest));
                RunDependencyGraphTestCommand.RaiseCanExecuteChanged();
            }
        }

        private async Task CompareDependencyGraphAsync()
        {
            if (!TryGetCurrentNode(out var node)) return;
            var requestGeneration = _detailRequestGeneration;
            _dependencyGraphComparisonRunning = true;
            DependencyGraphComparisonSummary = "正在分别读取轻量与完整引用链并比较去重关系…";
            OnPropertyChanged(nameof(DependencyGraphComparisonSummary));
            try
            {
                var stopwatch = Stopwatch.StartNew();
                var lightweight = await Task.Run(() => _patchGraphDiagnostics.AnalyzeDependencyGraphAsync(node, _library.ModsRootDirectory).AsTask());
                var full = await Task.Run(() => _patchGraphDiagnostics.AnalyzeFullPatchGraphAsync(node, _library.ModsRootDirectory).AsTask());
                if (!IsCurrentDetailRequest(node, requestGeneration)) return;
                stopwatch.Stop();
                var comparison = CompareReferenceSets(lightweight, full);
                var reportPath = WriteDependencyGraphComparisonReport(node, lightweight, full, comparison, stopwatch.Elapsed);
                DependencyGraphComparisonSummary = comparison.IsMatch
                    ? $"轻量链与完整分析的去重引用集合一致，耗时 {stopwatch.ElapsedMilliseconds} ms。"
                    : $"发现差异：仅轻量 {comparison.OnlyLightweight.Length} 条、仅完整 {comparison.OnlyFull.Length} 条；请查看 JSON。";
                LogService.Info($"轻量/完整引用链对比：Mod={node.Metadata.Name}，一致={comparison.IsMatch}，仅轻量={comparison.OnlyLightweight.Length}，仅完整={comparison.OnlyFull.Length}，耗时={stopwatch.ElapsedMilliseconds}ms，JSON={reportPath}。");
            }
            catch (Exception exception)
            {
                DependencyGraphComparisonSummary = $"引用链对比失败：{exception.Message}";
                LogService.Error($"轻量/完整引用链对比失败：Mod={node.Metadata.Name}，错误={exception}");
            }
            finally
            {
                _dependencyGraphComparisonRunning = false;
                OnPropertyChanged(nameof(DependencyGraphComparisonSummary));
                OnPropertyChanged(nameof(CanCompareDependencyGraph));
                CompareDependencyGraphCommand.RaiseCanExecuteChanged();
            }
        }

        private string WriteDependencyGraphTestReport<TAnalysis, TIssue>(ModNode node, IEnumerable<TAnalysis> analyses, TimeSpan elapsed, IReadOnlyList<TIssue> issues)
        {
            var serializedGroups = analyses
                .Select((analysis, index) => new
                {
                    Index = index,
                    Analysis = JsonSerializer.SerializeToElement(analysis)
                })
                .ToArray();

            var resources = serializedGroups
                .SelectMany(group => ReadJsonArrayProperty(group.Analysis, "Assets").Select(asset => new
                {
                    OwnerPatchGroup = ReadJsonStringProperty(group.Analysis, "Input", "PatchTocFilePath") ?? $"PatchGroup#{group.Index + 1}",
                    Resource = asset
                }))
                .ToArray();

            var referenceChains = serializedGroups
                .SelectMany(group => ReadJsonArrayProperty(group.Analysis, "References").Select(reference => new
                {
                    OwnerPatchGroup = ReadJsonStringProperty(group.Analysis, "Input", "PatchTocFilePath") ?? $"PatchGroup#{group.Index + 1}",
                    Reference = reference
                }))
                .GroupBy(item => item.Reference.GetRawText(), StringComparer.Ordinal)
                .Select(group => new
                {
                    Reference = group.First().Reference,
                    Owners = group.Select(item => item.OwnerPatchGroup).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    DuplicateCount = group.Count()
                })
                .ToArray();

            var issueDetails = issues
                .Select(issue => JsonSerializer.SerializeToElement(issue))
                .ToArray();
			var health = AssessDependencyGraph(serializedGroups.Select(group => group.Analysis));
            var isNormal = issueDetails.Length == 0 && health.IsNormal;
            var report = new
            {
                GeneratedUtc = DateTime.UtcNow,
                Mod = new
                {
                    Id = node.Id.Value,
                    Name = node.Metadata.Name
                },
                ElapsedMilliseconds = elapsed.TotalMilliseconds,
                Normality = new
                {
                    IsNormal = isNormal,
                    Judgment = isNormal ? "正常" : "存在需要检查的引用链读取或语义问题",
                    Explanation = isNormal
                        ? "所有 Patch 组均已完成轻量引用链读取，引用类型与目标资源类型一致，且未返回诊断问题。"
						: $"读取或语义校验发现问题：读取诊断 {issueDetails.Length} 项，引用语义问题 {health.Problems.Length} 项。"
                },
                Summary = new
                {
                    PatchGroupCount = serializedGroups.Length,
                    ResourceCount = resources.Length,
                    UniqueReferenceChainCount = referenceChains.Length,
                    DuplicateReferenceCount = referenceChains.Sum(chain => chain.DuplicateCount - 1),
                    IssueCount = issueDetails.Length
                },
                ResourceOwnership = resources,
                DeduplicatedReferenceChains = referenceChains,
				SemanticValidation = health,
                Issues = issueDetails,
                PatchGroups = serializedGroups.Select(group => group.Analysis).ToArray()
            };

            var reportDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(
                reportDirectory,
                $"DependencyGraph-{SanitizeFileName(node.Metadata.Name)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                WriteIndented = true
            }), Encoding.UTF8);
            return reportPath;
        }

        private static DependencyGraphHealth AssessDependencyGraph(IEnumerable<JsonElement> analyses)
        {
            var assets = analyses.SelectMany(analysis => ReadJsonArrayProperty(analysis, "Assets"))
                .Select(asset => asset.GetProperty("AssetKey").GetRawText())
                .ToHashSet(StringComparer.Ordinal);
            var assetTypes = analyses.SelectMany(analysis => ReadJsonArrayProperty(analysis, "Assets"))
                .ToDictionary(asset => asset.GetProperty("AssetKey").GetRawText(), asset => new
                {
                    IsUnit = asset.GetProperty("IsUnit").GetBoolean(),
                    IsMaterial = asset.GetProperty("IsMaterial").GetBoolean(),
                    IsTexture = asset.GetProperty("IsTexture").GetBoolean()
                }, StringComparer.Ordinal);
            var problems = new List<object>();
            foreach (var reference in analyses.SelectMany(analysis => ReadJsonArrayProperty(analysis, "References")))
            {
                var kind = reference.GetProperty("Kind").GetInt32();
                var source = reference.GetProperty("SourceAssetKey").GetRawText();
                var target = reference.GetProperty("TargetAssetKey").GetRawText();
                var sourceIsExpected = assetTypes.TryGetValue(source, out var sourceType) && (kind == 0 ? sourceType.IsUnit : sourceType.IsMaterial);
                var targetIsEmbedded = assets.Contains(target);
                var targetIsExpected = !targetIsEmbedded || (assetTypes.TryGetValue(target, out var targetType) && (kind == 0 ? targetType.IsMaterial : targetType.IsTexture));
                if (!sourceIsExpected || !targetIsExpected)
                {
                    problems.Add(new { Kind = kind == 0 ? "UnitMaterial" : "MaterialTexture", Source = source, Target = target, SourceIsExpected = sourceIsExpected, TargetIsExpected = targetIsExpected, TargetIsEmbedded = targetIsEmbedded });
                }
            }
            return new DependencyGraphHealth(problems.Count == 0, problems.ToArray());
        }

        private static DependencyGraphHealth AssessDependencyGraph<TAnalysis>(IEnumerable<TAnalysis> analyses)
            => AssessDependencyGraph(analyses.Select(analysis => JsonSerializer.SerializeToElement(analysis)));

        private static ReferenceSetComparison CompareReferenceSets<TAnalysis>(IEnumerable<TAnalysis> lightweight, IEnumerable<TAnalysis> full)
        {
            var light = BuildReferenceSet(lightweight);
            var complete = BuildReferenceSet(full);
            return new ReferenceSetComparison(
                light.Except(complete, StringComparer.Ordinal).ToArray(),
                complete.Except(light, StringComparer.Ordinal).ToArray());
        }

        private static HashSet<string> BuildReferenceSet<TAnalysis>(IEnumerable<TAnalysis> analyses)
            => analyses.SelectMany(analysis => ReadJsonArrayProperty(JsonSerializer.SerializeToElement(analysis), "References"))
                .Select(reference => $"{reference.GetProperty("Kind").GetInt32()}|{reference.GetProperty("SourceAssetKey").GetRawText()}|{reference.GetProperty("TargetAssetKey").GetRawText()}")
                .ToHashSet(StringComparer.Ordinal);

        private static string WriteDependencyGraphComparisonReport<TAnalysis>(ModNode node, IEnumerable<TAnalysis> lightweight, IEnumerable<TAnalysis> full, ReferenceSetComparison comparison, TimeSpan elapsed)
        {
            var reportDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(reportDirectory);
            var reportPath = Path.Combine(reportDirectory, $"DependencyGraphComparison-{SanitizeFileName(node.Metadata.Name)}-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new
            {
                GeneratedUtc = DateTime.UtcNow,
                Mod = new { node.Id.Value, node.Metadata.Name },
                ElapsedMilliseconds = elapsed.TotalMilliseconds,
                IsMatch = comparison.IsMatch,
                OnlyLightweight = comparison.OnlyLightweight,
                OnlyFull = comparison.OnlyFull,
                LightweightHealth = AssessDependencyGraph(lightweight),
                FullHealth = AssessDependencyGraph(full)
            }, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
            return reportPath;
        }

        private sealed record DependencyGraphHealth(bool IsNormal, object[] Problems);
        private sealed record ReferenceSetComparison(string[] OnlyLightweight, string[] OnlyFull)
        {
            public bool IsMatch => OnlyLightweight.Length == 0 && OnlyFull.Length == 0;
        }

        private static IEnumerable<JsonElement> ReadJsonArrayProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.Array
                ? property.EnumerateArray().Select(item => item.Clone()).ToArray()
                : Array.Empty<JsonElement>();
        }

        private static string? ReadJsonStringProperty(JsonElement element, string parentPropertyName, string propertyName)
        {
            return element.TryGetProperty(parentPropertyName, out var parent)
                && parent.ValueKind == JsonValueKind.Object
                && parent.TryGetProperty(propertyName, out var property)
                && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
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
                await RunOnUiThreadAsync(() =>
                {
                    AdvancedAssetState = _allAdvancedAssets.Count == 0 ? "轻量引用图尚未完成；请等待导入后的后台分析。" : $"共 {_allAdvancedAssets.Count} 个 AssetKey（轻量引用图）";
                    ApplyAdvancedAssetFilter();
                });
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                await RunOnUiThreadAsync(() =>
                {
                    AdvancedAssetState = $"稳定资产事实读取失败：{exception.Message}";
					OnPropertyChanged(nameof(AdvancedAssetState));
                });
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
            _detailRequestGeneration++;
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
            OnPropertyChanged(nameof(UnitCompatibilitySummary));
            OnPropertyChanged(nameof(IsModelOutdated));
            OnPropertyChanged(nameof(DataIndexSummary));
			OnPropertyChanged(nameof(MaterialDiagnosticSummary));
            OnPropertyChanged(nameof(UserStatusTitle));
            OnPropertyChanged(nameof(UserStatusSummary));
            OnPropertyChanged(nameof(MaterialPackagingSummary));
			OnPropertyChanged(nameof(MaterialDeliverySummary));
            OnPropertyChanged(nameof(SameKeyReconstructionSummary));
			OnPropertyChanged(nameof(AdvancedAnalysisSummary));
            OnPropertyChanged(nameof(CanRunAdvancedAnalysis));
			OnPropertyChanged(nameof(DependencyGraphTestSummary));
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
            DataIndexSummary = "跨 Mod 资产索引尚未读取。";

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
                var facts = _detailContentFacts ?? derived?.ContentFacts ?? _derivedState.Snapshot.ContentFacts.GetValueOrDefault(nodeId);
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
                _ = RefreshDataIndexSummaryAsync(nodeId, facts);
                return;
            }
            AssetTagsString = BuildAssetTagTreeText(summary);
            AssetListSummary = summary.Assets.Count == 0
                ? "未发现可解析资产"
                : string.Join(Environment.NewLine, summary.Assets.Take(80).Select(a => a.DisplayName));
            if (summary.Assets.Count > 80) AssetListSummary += Environment.NewLine + $"... 另有 {summary.Assets.Count - 80} 个资产";
            AssetOverrideSummary = BuildCachedAssetOverrideSummary(nodeId);
            MaterialDiagnosticSummary = BuildMaterialDiagnosticSummary(nodeId);
            _ = RefreshDataIndexSummaryAsync(nodeId, _detailContentFacts ?? derived?.ContentFacts ?? _derivedState.Snapshot.ContentFacts.GetValueOrDefault(nodeId));
        }

        private async Task RefreshDataIndexSummaryAsync(ModNodeId nodeId, ModContentFacts? facts)
        {
            if (facts is null) return;
            try
            {
                var index = CoreServices.CreateModDataIndex(_paths);
                var assets = facts.PatchGroups.SelectMany(group => group.AssetKeys).Distinct().ToArray();
                var providers = 0;
                var consumers = 0;
                foreach (var asset in assets)
                {
                    providers += (await index.FindProvidersAsync(asset)).Count(entry => entry.NodeId != nodeId);
                    consumers += (await index.FindConsumersAsync(asset)).Count(entry => entry.NodeId != nodeId);
                }
                RunOnUiThread(() =>
                {
                    if (_disposed) return;
                    DataIndexSummary = $"跨 Mod 索引：关联提供者 {providers} 个，引用消费者 {consumers} 个。";
                    OnPropertyChanged(nameof(DataIndexSummary));
                });
            }
            catch (Exception exception)
            {
                RunOnUiThread(() =>
                {
                    if (_disposed) return;
                    DataIndexSummary = $"跨 Mod 资产索引不可用：{exception.Message}";
                    OnPropertyChanged(nameof(DataIndexSummary));
                });
            }
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
            var outputRoot = Path.Combine(AppContext.BaseDirectory, "Output");
            var destination = Path.Combine(outputRoot, $"{SanitizeFileName(source.Metadata.Name)}+{DateTime.Now:yyyyMMdd-HHmmssfff}+SameKey重建");
            _sameKeyReconstructionRunning = true;
            SameKeyReconstructionSummary = "正在读取 Payload 并生成 Same-key 重建结果…";
            OnPropertyChanged(nameof(SameKeyReconstructionSummary));
            RaiseSameKeyReconstructionCommandState();
            try
            {
                var result = await Task.Run(() => _sameKeyReconstruction.GenerateCandidateAsync(
                    source,
                    _library.ModsRootDirectory,
                    SettingsService.GetGameDataFolder(),
                    destination).AsTask());
                if (!result.IsSuccessful)
                {
                    SameKeyReconstructionSummary = $"重建失败：{string.Join("；", result.Issues.Select(issue => issue.Message).Take(3))}";
                    _notifications?.Show(SameKeyReconstructionSummary, NotificationLevel.Error, TimeSpan.FromSeconds(12));
                    return;
                }

                SameKeyReconstructionSummary = $"重建完成：Unit {result.OutputUnitCount}；替换 mesh {result.ReplacementMeshCount}；极小化 mesh {result.MinifiedMeshCount}。输出：{result.OutputDirectory}";
                _notifications?.Show("Same-key 重建结果已写入 Output 文件夹。", NotificationLevel.Info, TimeSpan.FromSeconds(8));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
            {
                SameKeyReconstructionSummary = $"重建失败：{exception.Message}";
                _notifications?.Show(SameKeyReconstructionSummary, NotificationLevel.Error, TimeSpan.FromSeconds(12));
            }
            finally
            {
                _sameKeyReconstructionRunning = false;
                OnPropertyChanged(nameof(SameKeyReconstructionSummary));
                RaiseSameKeyReconstructionCommandState();
            }
        }

        private async Task PlanCrossArmorTransferAsync()
        {
            if (!TryGetCurrentNode(out var source)) return;
            try
            {
                _notifications?.Show("正在使用高级缓存中的 Unit 事实生成装备目录…", NotificationLevel.Info, TimeSpan.FromSeconds(8));
                var analyses = await _advancedAnalysis.GetRequiredAnalysesAsync(source, _library.ModsRootDirectory).ConfigureAwait(false);
                var unitKeys = analyses.SelectMany(analysis => analysis.Assets)
					.Where(asset => asset.AssetKey.TypeId == 0xe0a48d0be9a7453f)
					.Select(asset => new AssetKey(asset.AssetKey.TypeId, asset.AssetKey.FileId))
					.ToHashSet();
                var sourceCatalogCandidates = await _equipmentUnitCatalog.GetEntriesAsync(unitKeys);
                var sourcePatchPaths = analyses
					.Select(analysis => analysis.Input.PatchTocFilePath)
                    .ToArray();
                var preparedTransferableMeshes = analyses
                    .SelectMany(analysis => analysis.PreparedSourceUnits)
                    .Where(unit => unit.IsReadable)
                    .SelectMany(unit => unit.Meshes.Where(mesh => mesh.IsTransferable)
                        .Select(mesh => (new AssetKey(unit.Entry.AssetKey.TypeId, unit.Entry.AssetKey.FileId), mesh.MeshInfoIndex)))
                    .ToHashSet();
                var sourceCandidates = sourceCatalogCandidates
                    .Select(entry => entry with { Parts = entry.Parts.Where(part => preparedTransferableMeshes.Contains((part.UnitAssetKey, part.MeshInfoIndex))).ToArray() })
                    .Where(entry => entry.Parts.Count != 0)
                    .ToArray();
                var allCandidates = await _equipmentUnitCatalog.GetEntriesAsync();
                GameDataArchiveBrowserSnapshot? targetReplacementSnapshot = null;
                try
                {
                    var browser = CoreServices.CreateGameDataArchiveBrowserService(_paths, _derivedState.InformationCenter);
                    targetReplacementSnapshot = await browser.BuildAsync(_library.Snapshot, _library.ModsRootDirectory, SettingsService.GetGameDataFolder()).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
                {
                    _notifications?.Show($"未能读取装备替换状态，“全选未替换”按钮将不可用：{exception.Message}", NotificationLevel.Info, TimeSpan.FromSeconds(8));
                }
                if (sourceCandidates.Length == 0)
                {
                    _notifications?.Show("当前 Mod 没有可转移的真实 Unit 几何：索引未匹配、Unit 无法读取或所有匹配 mesh 均已极小化。", NotificationLevel.Info, TimeSpan.FromSeconds(10));
                    return;
                }
                if (sourcePatchPaths.Length != 1)
                {
                    _notifications?.Show("跨护甲验证候选目前仅支持源 Mod 含一个 Patch 主文件组。", NotificationLevel.Info, TimeSpan.FromSeconds(10));
                    return;
                }
                var preparedSourceEntries = analyses
                    .Where(analysis => string.Equals(analysis.Input.PatchTocFilePath, sourcePatchPaths[0], StringComparison.OrdinalIgnoreCase))
                    .SelectMany(analysis => analysis.Entries)
                    .ToArray();
                var viewModel = new HD2ModManager.Views.CrossArmorTransferPlanWindowViewModel(_equipmentUnitCatalog, sourceCandidates, allCandidates, sourcePatchPaths[0], SettingsService.GetGameDataFolder(), preparedSourceEntries, _paths, targetReplacementSnapshot);
                await OpenCrossArmorPlanOnUiThreadAsync(viewModel).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                RunOnUiThread(() => _notifications?.Show($"读取跨护甲计划失败：{exception.Message}", NotificationLevel.Error, TimeSpan.FromSeconds(12)));
            }
        }

        private static async Task OpenCrossArmorPlanOnUiThreadAsync(HD2ModManager.Views.CrossArmorTransferPlanWindowViewModel viewModel)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell) shell.OpenCrossArmorPlan(viewModel);
                return;
            }

            await dispatcher.InvokeAsync(() =>
            {
                if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell) shell.OpenCrossArmorPlan(viewModel);
            }).Task.ConfigureAwait(false);
        }

        private void OpenMaterialPackaging(bool splitEmbeddedMaterials, bool requireAllExternalMaterials = false)
        {
            if (!TryGetCurrentNode(out var source)) return;
            if (System.Windows.Application.Current?.MainWindow?.DataContext is not ShellViewModel shell) return;
            shell.OpenMaterialPackaging(this, new MaterialPackagingPageViewModel(
                source,
                _library.ModsRootDirectory,
                _materialPackaging,
                _library,
                _notifications,
                splitEmbeddedMaterials,
                requireAllExternalMaterials));
        }

        private bool TryGetCurrentNode(out ModNode node)
        {
            node = default!;
            return Mod != null && TryParseNodeId(Mod.Guid, out var nodeId) && _library.Snapshot.Nodes.TryGetValue(nodeId, out node!);
        }

        private bool IsCurrentDetailRequest(ModNode node, int requestGeneration)
            => !_disposed
                && requestGeneration == _detailRequestGeneration
                && TryGetCurrentNode(out var currentNode)
                && currentNode.Id == node.Id;

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
            if (System.Windows.Application.Current?.MainWindow?.DataContext is ShellViewModel shell)
            {
                _ = shell.AddModToSelectedProfileAsync(Mod.Guid, Mod.Name);
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

        private static Task RunOnUiThreadAsync(Action action)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.CheckAccess())
            {
                action();
                return Task.CompletedTask;
            }
            return dispatcher.InvokeAsync(action).Task;
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
