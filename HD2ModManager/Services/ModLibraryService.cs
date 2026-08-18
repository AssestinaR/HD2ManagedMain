using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using HD2ModCore.Domain;
using HD2ModCore.Application;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;

namespace HD2ModManager.Services
{
    public enum ModContentChangeKind
    {
        Added,
        Changed,
        Removed,
        // Derived facts changed, but no Patch payload changed on disk.
        DerivedOnly,
    }

    public sealed class ModContentFactsChangedEventArgs : EventArgs
    {
        public ModContentFactsChangedEventArgs(IReadOnlyCollection<ModNodeId> nodeIds, ModContentChangeKind kind)
        {
            NodeIds = nodeIds;
            Kind = kind;
        }

        public IReadOnlyCollection<ModNodeId> NodeIds { get; }
        public ModContentChangeKind Kind { get; }
    }

    // 作用：为现有 WPF UI 提供基于 HD2ModCore LibrarySnapshot 的模组库外观。
    public class ModLibraryService
    {
        private readonly StoragePaths _paths;
        private readonly HD2ModCore.Application.IModLibraryManager _manager;
        private readonly HD2ModCore.Application.ILibraryDerivedDataService _derivedDataService;
		private readonly ModAssetSummaryProjector _assetSummaryProjector;
        private readonly HD2ModCore.Application.IModInformationCenter _informationCenter;
        private readonly HD2ModCore.Application.IModLibrarySynchronizer _synchronizer;
        private LibrarySnapshot _snapshot;
        private DerivedLibraryData _derivedData;
        private Dictionary<string, ModEntity> _byGuid = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _derivedRefreshGate = new(1, 1);
        private readonly SemaphoreSlim _libraryMutationGate = new(1, 1);
        private readonly DecorationActivationStore _decorationActivations;
        private readonly OptionActivationStore _optionActivations;
        private readonly DecorationAttachmentService _decorationAttachments;
        private long _stateVersion;

        public ReadOnlyDictionary<string, ModEntity> ByGuid => new(_byGuid);
        public LibrarySnapshot Snapshot => _snapshot;
        public DerivedLibraryData DerivedData => _derivedData;
        public string ModsRootDirectory => _paths.ModsDirectory;
        public HD2ModCore.Application.IModInformationCenter InformationCenter => _informationCenter;
        public event EventHandler<ModContentFactsChangedEventArgs>? ModContentFactsChanged;
        public event EventHandler? SnapshotChanged;
        public event EventHandler? OptionActivationChanged;

        public async Task<DecorationActivationSummary> ToggleDecorationForAllAvailableHostsAsync(string decorationId, CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var available = GetAvailableDecorationHostIds(decorationId);
                var enabled = _decorationActivations.GetEnabledHosts(decorationId).Intersect(available, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (available.Count == 0) return new DecorationActivationSummary(0, 0, false, "无可用主体。");
                var enable = !available.All(enabled.Contains);
                await ApplyDecorationActivationBatchCoreAsync([new DecorationActivationMutation(decorationId, enable)], cancellationToken).ConfigureAwait(false);
                return new DecorationActivationSummary(enable ? available.Count : 0, available.Count, enable, enable ? $"为 {available.Count} 个 Mod 启用了。" : "已对全部主体禁用。");
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<DecorationActivationSummary> SetDecorationEnabledForHostAsync(string decorationId, string hostId, bool enabled, CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var available = GetAvailableDecorationHostIds(decorationId);
                if (!available.Contains(hostId, StringComparer.OrdinalIgnoreCase)) return new DecorationActivationSummary(0, available.Count, false, "当前主体不支持该装饰。");
                var hosts = _decorationActivations.GetEnabledHosts(decorationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                await ApplyDecorationActivationBatchCoreAsync([new DecorationActivationMutation(decorationId, enabled, hostId)], cancellationToken).ConfigureAwait(false);
                var enabledCount = hosts.Count(id => available.Contains(id, StringComparer.OrdinalIgnoreCase));
                enabledCount = _decorationActivations.GetEnabledHosts(decorationId).Count(id => available.Contains(id, StringComparer.OrdinalIgnoreCase));
                return new DecorationActivationSummary(enabledCount, available.Count, enabled, enabled ? "已为当前主体启用。" : "已对当前主体禁用。");
            }
            finally { _libraryMutationGate.Release(); }
        }

        // The shared mutation boundary for all decoration enable/disable operations.
        // It computes each host's final attachment set before rebuilding so a host is
        // never rebuilt more than once for one user action.
        public async Task<DecorationActivationBatchResult> ApplyDecorationActivationBatchAsync(
            IReadOnlyCollection<DecorationActivationMutation> mutations,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(mutations);
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { return await ApplyDecorationActivationBatchCoreAsync(mutations, cancellationToken).ConfigureAwait(false); }
            finally { _libraryMutationGate.Release(); }
        }

        public DecorationActivationSummary GetDecorationActivationSummary(string decorationId)
        {
            var available = GetAvailableDecorationHostIds(decorationId);
            var enabled = _decorationActivations.GetEnabledHosts(decorationId).Count(id => available.Contains(id, StringComparer.OrdinalIgnoreCase));
            return new DecorationActivationSummary(enabled, available.Count, available.Count != 0 && enabled == available.Count,
                available.Count == 0 ? "无可用主体。" : enabled == 0 ? "尚未启用。" : enabled == available.Count ? $"为 {enabled} 个 Mod 启用了。" : $"为 {enabled}/{available.Count} 个 Mod 启用了。");
        }

        public bool IsDecorationEnabledForHost(string decorationId, string hostId)
            => GetAvailableDecorationHostIds(decorationId).Contains(hostId, StringComparer.OrdinalIgnoreCase)
               && _decorationActivations.GetEnabledHosts(decorationId).Contains(hostId, StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ModEntity> GetDecorationsForHost(string hostId)
            => All().Where(mod => mod.IsDecoration && GetAvailableDecorationHostIds(mod.Guid).Contains(hostId, StringComparer.OrdinalIgnoreCase)).ToArray();

        public IReadOnlyList<ModEntity> GetOptionsForHost(string hostId)
            => All().Where(mod => mod.IsOption && mod.HostModGuids.Any(host => SameModGuid(host, hostId))).ToArray();

        public IReadOnlyList<ModEntity> GetAttachmentsForHost(string hostId)
            => GetDecorationsForHost(hostId).Concat(GetOptionsForHost(hostId)).ToArray();

        public bool IsOptionEnabledForHost(string optionId, string hostId)
            => _optionActivations.GetEnabledHosts(optionId).Any(host => SameModGuid(host, hostId));

        public string GetOptionActivationStatus(string optionId)
        {
            var option = Get(optionId);
            if (option?.IsOption != true) return "尚未启用。";
            var count = _optionActivations.GetEnabledHosts(optionId).Count;
            return count == 0 ? "尚未启用。" : $"为 {count} 个 Mod 启用了。";
        }

        public async Task<string> ToggleOptionForHostAsync(string optionId, string hostId, CancellationToken cancellationToken = default)
        {
            var option = Get(optionId);
            if (option?.IsOption != true || !option.HostModGuids.Any(host => SameModGuid(host, hostId))) return "当前主体不支持该选项。";
            var hosts = _optionActivations.GetEnabledHosts(optionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var storedHost = hosts.FirstOrDefault(host => SameModGuid(host, hostId));
            var enabled = storedHost is null;
            if (enabled) hosts.Add(hostId); else hosts.Remove(storedHost!);
            await _optionActivations.SetEnabledHostsAsync(optionId, hosts, cancellationToken).ConfigureAwait(false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            OptionActivationChanged?.Invoke(this, EventArgs.Empty);
            return enabled ? "已为当前主体启用选项。" : "已为当前主体禁用选项。";
        }

        public async Task<string> ToggleOptionForAllAvailableHostsAsync(string optionId, CancellationToken cancellationToken = default)
        {
            var option = Get(optionId);
            if (option?.IsOption != true) return "当前不是选项 Mod。";
            var available = option.HostModGuids
                .Where(host => All().Any(mod => mod.Capabilities.CanJoinProfile && SameModGuid(mod.Guid, host)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (available.Count == 0) return "无可用主体。";
            var enabled = _optionActivations.GetEnabledHosts(optionId);
            var enable = !available.All(enabled.Contains);
            await _optionActivations.SetEnabledHostsAsync(optionId, enable ? available : Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            OptionActivationChanged?.Invoke(this, EventArgs.Empty);
            return enable ? $"为 {available.Count} 个 Mod 启用了。" : "已对全部主体禁用。";
        }

        public async Task<string> SetOptionEnabledForAllAvailableHostsAsync(string optionId, bool enabled, CancellationToken cancellationToken = default)
        {
            var option = Get(optionId);
            if (option?.IsOption != true) return "当前不是选项 Mod。";
            var available = option.HostModGuids.Where(host => All().Any(mod => mod.Capabilities.CanJoinProfile && SameModGuid(mod.Guid, host))).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (available.Count == 0) return "无可用主体。";
            await _optionActivations.SetEnabledHostsAsync(optionId, enabled ? available : Array.Empty<string>(), cancellationToken).ConfigureAwait(false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            OptionActivationChanged?.Invoke(this, EventArgs.Empty);
            return enabled ? $"为 {available.Count} 个 Mod 启用了。" : "已对全部主体禁用。";
        }

        public ModLibraryService(string libraryPath, HD2ModCore.Application.IModInformationCenter informationCenter)
        {
            _paths = SettingsService.CreateStoragePaths();
            _manager = CoreServices.CreateModLibraryManager(_paths);
            _informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
            _synchronizer = CoreServices.CreateModLibrarySynchronizer();
            _derivedDataService = CoreServices.CreateLibraryDerivedDataService(_paths, _informationCenter);
		_assetSummaryProjector = CoreServices.CreateModAssetSummaryProjector(_paths);
            _decorationActivations = new DecorationActivationStore(Path.Combine(_paths.DataDirectory, "decoration-activations.json"));
            _optionActivations = new OptionActivationStore(Path.Combine(_paths.DataDirectory, "option-activations.json"));
            _decorationAttachments = new DecorationAttachmentService(_paths);
            _snapshot = EmptySnapshot();
            _derivedData = EmptyDerivedData();
        }

        public void Load(bool buildDerivedData = true)
        {
            _snapshot = _manager.LoadOrCreateAsync().AsTask().GetAwaiter().GetResult();
            RebuildIndex(buildDerivedData);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task LoadAsync(bool buildDerivedData = true, CancellationToken cancellationToken = default)
        {
            var loadVersion = Volatile.Read(ref _stateVersion);
            var loadedSnapshot = await _manager.LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
            DerivedLibraryData? loadedDerivedData = null;
            if (buildDerivedData)
            {
                loadedDerivedData = await _derivedDataService.BuildAsync(
                    loadedSnapshot,
                    _paths.ModsDirectory,
                    SettingsService.GetGameDataFolder(),
                    null,
                    cancellationToken).AsTask().ConfigureAwait(false);
            }

            if (loadVersion != Volatile.Read(ref _stateVersion))
            {
                LogService.Info("跳过过期的后台模组库加载结果：用户操作已先行提交。");
                return;
            }

            _snapshot = loadedSnapshot;
            if (loadedDerivedData is not null) _derivedData = loadedDerivedData;
            RebuildEntityIndex(includePatchFiles: buildDerivedData);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task RefreshDerivedDataAsync(CancellationToken cancellationToken = default)
            => RefreshDerivedDataAsync(guids: null, ModContentChangeKind.DerivedOnly, cancellationToken);

        // Content commits must not expose a new Patch payload before both lightweight facts
        // and its reference graph have replaced the corresponding stale cache entries.
        public async Task RefreshCommittedContentAsync(
            IEnumerable<ModNodeId> nodeIds,
            ModContentChangeKind changeKind = ModContentChangeKind.Changed,
            bool alreadyInvalidated = false,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(nodeIds);
            var affectedNodeIds = nodeIds.Distinct().ToArray();
            if (affectedNodeIds.Length == 0) return;

            if (!alreadyInvalidated)
            {
                foreach (var nodeId in affectedNodeIds)
                    await _informationCenter.InvalidateNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            }

            await RefreshDerivedDataAsync(
                affectedNodeIds.Select(nodeId => nodeId.Value.ToString("N")),
                changeKind,
                cancellationToken,
                includeReferenceGraphs: true).ConfigureAwait(false);
        }

        public async Task<bool> SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
            var synchronizeVersion = Volatile.Read(ref _stateVersion);
            var synchronizeSnapshot = _snapshot;
            var result = await _synchronizer.SynchronizeAsync(synchronizeSnapshot, _paths.ModsDirectory, cancellationToken).ConfigureAwait(false);
            if (!result.FilesystemChanged) return false;

            if (synchronizeVersion != Volatile.Read(ref _stateVersion))
            {
                LogService.Info("跳过过期的后台模组库同步结果：用户操作已先行提交。");
                return false;
            }

            _snapshot = result.Snapshot;
            await CoreServices.CreateModLibraryStore(_paths).SaveAsync(_snapshot, cancellationToken).ConfigureAwait(false);
            foreach (var nodeId in result.ChangedNodeIds.Concat(result.MissingNodeIds))
                await _informationCenter.InvalidateNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            RebuildIndex(buildDerivedData: false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            await RefreshCommittedContentAsync(
                result.AddedNodeIds.Concat(result.ChangedNodeIds),
                ModContentChangeKind.Changed,
                alreadyInvalidated: true,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            return true;
            }
            finally
            {
                _libraryMutationGate.Release();
            }
        }

        public async Task RefreshDerivedDataAsync(IEnumerable<string>? guids, CancellationToken cancellationToken = default)
            => await RefreshDerivedDataAsync(guids, ModContentChangeKind.DerivedOnly, cancellationToken).ConfigureAwait(false);

        public async Task RefreshDerivedDataAsync(
            IEnumerable<string>? guids,
            ModContentChangeKind changeKind,
            CancellationToken cancellationToken = default,
            bool includeReferenceGraphs = false)
        {
            await _derivedRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var nodeIds = guids is null
                    ? null
                    : guids.Select(ParseNodeId).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
                while (true)
                {
                    var snapshot = _snapshot;
                    var refreshVersion = Volatile.Read(ref _stateVersion);
                    var rebuilt = await _derivedDataService.BuildAsync(snapshot, _paths.ModsDirectory, SettingsService.GetGameDataFolder(), nodeIds, cancellationToken).AsTask().ConfigureAwait(false);
                    if (refreshVersion != Volatile.Read(ref _stateVersion))
                    {
                        LogService.Info("丢弃过期的派生数据构建结果并重试：模组库快照已变化。");
                        cancellationToken.ThrowIfCancellationRequested();
                        continue;
                    }

                    if (includeReferenceGraphs)
                    {
                        var graphIssues = await RefreshReferenceGraphsAsync(snapshot, rebuilt.Nodes.Keys, cancellationToken).ConfigureAwait(false);
                        if (graphIssues.Count != 0)
                            rebuilt = rebuilt with { Issues = rebuilt.Issues.Concat(graphIssues).ToArray() };
                    }

                    if (nodeIds is null)
                    {
                        _derivedData = rebuilt;
                    }
                    else
                    {
                        var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
                        foreach (var pair in rebuilt.Nodes) nodes[pair.Key] = pair.Value;
						var issues = _derivedData.Issues.Where(issue => issue.NodeId is null || !nodeIds!.Contains(issue.NodeId.Value)).Concat(rebuilt.Issues).ToList();
						_derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, issues, rebuilt.AssetSummaryGeneration ?? _derivedData.AssetSummaryGeneration);
					}
					if (nodeIds is null) RebuildEntityIndex(); else UpdateEntityIndex(nodeIds);
                    IReadOnlyCollection<ModNodeId> changedNodeIds = nodeIds is null ? rebuilt.Nodes.Keys.ToArray() : nodeIds;
                    if (changedNodeIds.Count > 0)
                    {
                        ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(changedNodeIds, changeKind));
                    }
                    break;
                }
            }
            finally
            {
                _derivedRefreshGate.Release();
            }
        }

		// Asset labels depend on stable Patch facts plus Game Data / metadata generations.
		// This path deliberately does not request AssetInventory again.
		public async Task<bool> RefreshAssetSummariesAsync(CancellationToken cancellationToken = default)
		{
			var snapshot = _snapshot;
			if (_derivedData.Nodes.Count != snapshot.Nodes.Count)
			{
				await RefreshDerivedDataAsync(cancellationToken).ConfigureAwait(false);
				return true;
			}

			await _derivedRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				var generation = await _assetSummaryProjector.GetMappingGenerationAsync(cancellationToken).ConfigureAwait(false);
				if (string.Equals(_derivedData.AssetSummaryGeneration, generation, StringComparison.Ordinal)
					&& _derivedData.Nodes.Values.All(node => node.AssetSummary is not null))
					return false;

				var factsByNode = new Dictionary<ModNode, ModContentFacts>();
				foreach (var pair in _derivedData.Nodes)
				{
					if (snapshot.Nodes.TryGetValue(pair.Key, out var node)) factsByNode[node] = pair.Value.ContentFacts;
				}
				var projection = await _assetSummaryProjector.ProjectManyWithGenerationAsync(factsByNode, cancellationToken).ConfigureAwait(false);
				var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value with { AssetSummary = projection.Summaries.GetValueOrDefault(pair.Key) });
				_derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, _derivedData.Issues, projection.MappingGeneration);
				var changed = nodes.Keys.ToArray();
				if (changed.Length != 0)
				{
					LogService.Info($"资产标签投影已更新：节点数={changed.Length}，映射版本={projection.MappingGeneration[..Math.Min(12, projection.MappingGeneration.Length)]}。");
					ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(changed, ModContentChangeKind.DerivedOnly));
				}
				return true;
			}
			finally
			{
				_derivedRefreshGate.Release();
			}
		}

        private async Task<IReadOnlyList<CoreIssue>> RefreshReferenceGraphsAsync(
            LibrarySnapshot snapshot,
            IEnumerable<ModNodeId> nodeIds,
            CancellationToken cancellationToken)
        {
            var issues = new List<CoreIssue>();
            foreach (var nodeId in nodeIds.Distinct())
            {
                if (!snapshot.Nodes.TryGetValue(nodeId, out var node)) continue;
                var result = await _informationCenter.RequestReferenceGraphAsync(
                    node,
                    _paths.ModsDirectory,
                    new ModInformationRequest(ModInformationKind.ReferenceGraph, "ContentCommit"),
                    cancellationToken).ConfigureAwait(false);
                if (result.Data is not null) continue;

                var nodeIssues = result.Issues.Count != 0
                    ? result.Issues
                    : new[] { new CoreIssue(CoreIssueSeverity.Error, "ReferenceGraphRefreshFailed", "Reference graph refresh returned no data.", node.RelativePath, node.Id) };
                issues.AddRange(nodeIssues);
                LogService.Error($"内容提交后的引用图刷新失败：节点={node.Id.Value:N}，问题={string.Join(" | ", nodeIssues.Select(issue => issue.Message))}");
            }
            return issues;
        }

        public void Save()
        {
            _libraryMutationGate.Wait();
            try
            {
            var store = CoreServices.CreateModLibraryStore(_paths);
            store.SaveAsync(_snapshot).AsTask().GetAwaiter().GetResult();
            Interlocked.Increment(ref _stateVersion);
            RebuildIndex(buildDerivedData: false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task SaveAsync(CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await CoreServices.CreateModLibraryStore(_paths).SaveAsync(_snapshot, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                RebuildIndex(buildDerivedData: false);
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
            finally { _libraryMutationGate.Release(); }
        }

        public bool Add(ModEntity mod)
        {
            _libraryMutationGate.Wait();
            try
            {
            if (!TryParseNodeId(mod.Guid, out var nodeId)) return false;
            if (!_snapshot.Nodes.TryGetValue(nodeId, out var node)) return false;

            var metadata = node.Metadata with
            {
                Name = string.IsNullOrWhiteSpace(mod.Name) ? node.Metadata.Name : mod.Name,
                Notes = mod.Description,
                ModifiedUtc = DateTimeOffset.UtcNow,
            };

            _snapshot = _manager.UpdateNodeMetadataAsync(nodeId, metadata).AsTask().GetAwaiter().GetResult();
            Interlocked.Increment(ref _stateVersion);
            RebuildIndex(buildDerivedData: false);
            return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<bool> AddAsync(ModEntity mod, CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryParseNodeId(mod.Guid, out var nodeId) || !_snapshot.Nodes.TryGetValue(nodeId, out var node)) return false;
                var metadata = node.Metadata with
                {
                    Name = string.IsNullOrWhiteSpace(mod.Name) ? node.Metadata.Name : mod.Name,
                    Notes = mod.Description,
                    ModifiedUtc = DateTimeOffset.UtcNow,
                };
                _snapshot = await _manager.UpdateNodeMetadataAsync(nodeId, metadata, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                RebuildIndex(buildDerivedData: false);
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public bool Remove(string guid)
        {
            _libraryMutationGate.Wait();
            try
            {
            if (!TryParseNodeId(guid, out var nodeId)) return false;
            var removedIsDecoration = _byGuid.TryGetValue(guid, out var entity) && entity.IsDecoration;
            var removedIsOption = entity?.IsOption == true;
            if (removedIsDecoration)
                ApplyDecorationActivationBatchCoreAsync([new DecorationActivationMutation(guid, false)], CancellationToken.None).GetAwaiter().GetResult();
            _snapshot = _manager.DeleteNodeAsync(nodeId, deleteStoredFiles: true).AsTask().GetAwaiter().GetResult();
            if (removedIsDecoration)
            {
                _decorationActivations.RemoveDecorationAsync(guid).GetAwaiter().GetResult();
            }
            else _decorationActivations.RemoveHostAsync(guid).GetAwaiter().GetResult();
            if (removedIsOption) _optionActivations.RemoveAsync(guid).GetAwaiter().GetResult();
            Interlocked.Increment(ref _stateVersion);
            _informationCenter.InvalidateNodeAsync(nodeId).AsTask().GetAwaiter().GetResult();
			ThumbnailService.DeleteCachedThumbnailsForSource(GetDerivedData(guid)?.IconPath);
            var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
            nodes.Remove(nodeId);
            _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, nodes.Values.SelectMany(node => node.Issues).ToArray(), _derivedData.AssetSummaryGeneration);
            RebuildIndex(buildDerivedData: false);
            ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(new[] { nodeId }, ModContentChangeKind.Removed));
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<bool> RemoveAsync(string guid, CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryParseNodeId(guid, out var nodeId)) return false;
                var removedIsDecoration = _byGuid.TryGetValue(guid, out var entity) && entity.IsDecoration;
                var removedIsOption = entity?.IsOption == true;
                if (removedIsDecoration)
                    await ApplyDecorationActivationBatchCoreAsync([new DecorationActivationMutation(guid, false)], cancellationToken).ConfigureAwait(false);
                _snapshot = await _manager.DeleteNodeAsync(nodeId, deleteStoredFiles: true, cancellationToken).ConfigureAwait(false);
                if (removedIsDecoration) await _decorationActivations.RemoveDecorationAsync(guid, cancellationToken).ConfigureAwait(false);
                else await _decorationActivations.RemoveHostAsync(guid, cancellationToken).ConfigureAwait(false);
                if (removedIsOption) await _optionActivations.RemoveAsync(guid, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                await _informationCenter.InvalidateNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
                ThumbnailService.DeleteCachedThumbnailsForSource(GetDerivedData(guid)?.IconPath);
                var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
                nodes.Remove(nodeId);
                _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, nodes.Values.SelectMany(node => node.Issues).ToArray(), _derivedData.AssetSummaryGeneration);
                RebuildIndex(buildDerivedData: false);
                ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(new[] { nodeId }, ModContentChangeKind.Removed));
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public bool Rename(string guid, string newName)
        {
            _libraryMutationGate.Wait();
            try
            {
            if (string.IsNullOrWhiteSpace(newName)) return false;
            if (!TryParseNodeId(guid, out var nodeId)) return false;
            if (!_snapshot.Nodes.TryGetValue(nodeId, out var node)) return false;

            var metadata = node.Metadata with
            {
                Name = newName.Trim(),
                ModifiedUtc = DateTimeOffset.UtcNow,
            };
            _snapshot = _manager.UpdateNodeMetadataAsync(nodeId, metadata).AsTask().GetAwaiter().GetResult();
            Interlocked.Increment(ref _stateVersion);
            RebuildIndex(buildDerivedData: false);
            return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<bool> RenameAsync(string guid, string newName, CancellationToken cancellationToken = default)
        {
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (string.IsNullOrWhiteSpace(newName) || !TryParseNodeId(guid, out var nodeId) || !_snapshot.Nodes.TryGetValue(nodeId, out var node)) return false;
                var metadata = node.Metadata with { Name = newName.Trim(), ModifiedUtc = DateTimeOffset.UtcNow };
                _snapshot = await _manager.UpdateNodeMetadataAsync(nodeId, metadata, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                RebuildIndex(buildDerivedData: false);
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                return true;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<string> ReplaceStoredFilesAsync(ModNodeId nodeId, string generatedDirectory, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(generatedDirectory);
            var sourceDirectory = Path.GetFullPath(generatedDirectory);
            if (!Directory.Exists(sourceDirectory)) throw new DirectoryNotFoundException(sourceDirectory);

            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            string? rollbackDirectory = null;
            string? backupDirectory = null;
            var replacementCommitted = false;
            try
            {
                if (!_snapshot.Nodes.TryGetValue(nodeId, out var node)) throw new InvalidOperationException("当前 Mod 已不在库中。");
                var targetDirectory = Path.GetFullPath(Path.Combine(_paths.ModsDirectory, node.RelativePath));
                var modsRoot = Path.GetFullPath(_paths.ModsDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!targetDirectory.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("当前 Mod 不在受管理的库目录中。");
                if (string.Equals(sourceDirectory, targetDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("输出目录不能与当前 Mod 目录相同。");

                var sourcePatchFiles = EnumeratePatchFiles(sourceDirectory).ToArray();
                if (sourcePatchFiles.Length == 0) throw new InvalidOperationException("生成结果中没有可导入的 Patch 文件。");

                backupDirectory = Path.Combine(_paths.DataDirectory, "mod-replacement-backups", nodeId.Value.ToString("N"), DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
                CopyDirectory(targetDirectory, backupDirectory, cancellationToken);
                rollbackDirectory = targetDirectory + ".replace-rollback-" + Guid.NewGuid().ToString("N");
                SetDirectoryReadOnly(targetDirectory, readOnly: false);
                Directory.Move(targetDirectory, rollbackDirectory);
                try
                {
                    // 保留 Mod 自有的非 Patch 文件，仅以生成结果替换 Patch 及其 sidecar。
                    CopyNonPatchFiles(rollbackDirectory, targetDirectory, cancellationToken);
                    CopyFiles(sourceDirectory, targetDirectory, sourcePatchFiles, cancellationToken);
                    SetDirectoryReadOnly(targetDirectory, readOnly: true);
                }
                catch
                {
                    if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, recursive: true);
                    Directory.Move(rollbackDirectory, targetDirectory);
                    rollbackDirectory = null;
                    throw;
                }

                Directory.Delete(rollbackDirectory, recursive: true);
                rollbackDirectory = null;
                replacementCommitted = true;
                try
                {
                    await _informationCenter.InvalidateNodeAsync(nodeId, CancellationToken.None).ConfigureAwait(false);
                    ThumbnailService.DeleteCachedThumbnailsForSource(GetDerivedData(nodeId.Value.ToString("N"))?.IconPath);
                    await RefreshCommittedContentAsync(
                        new[] { nodeId },
                        ModContentChangeKind.Changed,
                        alreadyInvalidated: true,
                        cancellationToken: CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    LogService.Error($"Mod 文件已替换，但缓存刷新失败：节点={nodeId.Value:N}，错误={exception}");
                }
                return backupDirectory;
            }
            catch
            {
                if (!replacementCommitted && rollbackDirectory is not null && Directory.Exists(rollbackDirectory))
                {
                    var node = _snapshot.Nodes[nodeId];
                    var target = Path.Combine(_paths.ModsDirectory, node.RelativePath);
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                    Directory.Move(rollbackDirectory, target);
                }
                throw;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<int> RemoveManyAsync(IReadOnlyList<string> guids, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(guids);
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var nodeIds = guids
                    .Select(ParseNodeId)
                    .Where(nodeId => nodeId.HasValue)
                    .Select(nodeId => nodeId!.Value)
                    .Distinct()
                    .Where(_snapshot.Nodes.ContainsKey)
                    .ToArray();
                if (nodeIds.Length == 0) return 0;

                var deletedDecorations = nodeIds
                    .Where(nodeId => _snapshot.Nodes.TryGetValue(nodeId, out var node) && node.Metadata.Kind == ModNodeKind.Decoration)
                    .Select(nodeId => nodeId.Value.ToString("N"))
                    .ToArray();
                if (deletedDecorations.Length > 0)
                {
                    await ApplyDecorationActivationBatchCoreAsync(
                        deletedDecorations.Select(id => new DecorationActivationMutation(id, false)).ToArray(),
                        cancellationToken).ConfigureAwait(false);
                }

                var thumbnailPaths = nodeIds
                    .Select(nodeId => GetDerivedData(nodeId.Value.ToString("N"))?.IconPath)
                    .ToArray();
                _snapshot = await _manager.DeleteNodesAsync(nodeIds, deleteStoredFiles: true, cancellationToken).ConfigureAwait(false);
                if (deletedDecorations.Length > 0)
                    await _decorationActivations.RemoveDecorationsAsync(deletedDecorations, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                foreach (var nodeId in nodeIds)
                    await _informationCenter.InvalidateNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
                foreach (var thumbnailPath in thumbnailPaths)
                    ThumbnailService.DeleteCachedThumbnailsForSource(thumbnailPath);

                var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
                foreach (var nodeId in nodeIds) nodes.Remove(nodeId);
                _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, nodes.Values.SelectMany(node => node.Issues).ToArray(), _derivedData.AssetSummaryGeneration);
                RebuildIndex(buildDerivedData: false);
                ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(nodeIds, ModContentChangeKind.Removed));
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                return nodeIds.Length;
            }
            finally { _libraryMutationGate.Release(); }
        }

        public async Task<ModEntity> CreateDecorationAsync(string sourceModId, IReadOnlyList<DecorationSourceUnit> sourceUnits, DecorationPlanDocument plan, string displayName, IReadOnlyDictionary<string, IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>>? preparedEntries = null, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sourceModId);
            ArgumentNullException.ThrowIfNull(sourceUnits);
            ArgumentNullException.ThrowIfNull(plan);
            await _libraryMutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (!TryParseNodeId(sourceModId, out var sourceId) || !_snapshot.Nodes.TryGetValue(sourceId, out var source))
                    throw new InvalidOperationException("装饰来源 Mod 不存在。");

                var nodeId = ModNodeId.New();
                var safeName = string.Concat(displayName.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character)).Trim();
                if (string.IsNullOrWhiteSpace(safeName)) safeName = "Decoration";
                // Decoration packages are peer Mod-library entries. Their marker files, not a
                // special parent directory, distinguish them from deployable patch packages.
                var relativePath = $"{safeName}-{nodeId.Value:N}";
                var directory = Path.Combine(_paths.ModsDirectory, relativePath);
                Directory.CreateDirectory(directory);
                plan.Name = displayName;
                plan.Plan.CreatedUtc = DateTime.UtcNow;
                plan.Payloads = (await new DecorationPayloadCompiler(CoreServices.CreateModFileResolver())
                    .CompileAsync(source, _paths.ModsDirectory, sourceUnits, plan.Plan, directory, preparedEntries, cancellationToken).ConfigureAwait(false)).ToList();
                var planPath = Path.Combine(directory, "decoration.json");
                await File.WriteAllTextAsync(planPath, JsonSerializer.Serialize(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), cancellationToken).ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                var node = new ModNode(nodeId, relativePath, new ModNodeMetadata(displayName, "Decoration model fragment", now, now, ModNodeKind.Decoration), [], []);
                _snapshot = await _manager.UpsertNodeAsync(node, cancellationToken).ConfigureAwait(false);
                Interlocked.Increment(ref _stateVersion);
                RebuildIndex(buildDerivedData: false);
                SnapshotChanged?.Invoke(this, EventArgs.Empty);
                return ToEntity(node, includePatchFiles: false);
            }
            finally { _libraryMutationGate.Release(); }
        }

        public ModEntity? Get(string guid)
        {
            var index = Volatile.Read(ref _byGuid);
            index.TryGetValue(guid, out var m);
            return m;
        }

        public IEnumerable<ModEntity> All() => Volatile.Read(ref _byGuid).Values;

        private IReadOnlySet<string> GetAvailableDecorationHostIds(string decorationId)
        {
            var decoration = Get(decorationId);
            if (decoration is null || !decoration.IsDecoration) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var path = Path.Combine(_paths.ModsDirectory, decoration.SourcePath ?? string.Empty, "decoration.json");
                if (!File.Exists(path)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var plan = JsonSerializer.Deserialize<DecorationPlanDocument>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                var targets = plan?.Plan?.TargetModGuids ?? [];
                var standard = _snapshot.Nodes.Values.Where(node => node.Metadata.Kind == ModNodeKind.Standard).Select(node => node.Id.Value.ToString("N")).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return targets.Where(standard.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        }

        private async Task<DecorationActivationBatchResult> ApplyDecorationActivationBatchCoreAsync(
            IReadOnlyCollection<DecorationActivationMutation> mutations,
            CancellationToken cancellationToken)
        {
            var requested = mutations
                .Where(mutation => !string.IsNullOrWhiteSpace(mutation.DecorationId))
                .GroupBy(mutation => $"{mutation.DecorationId}\u001f{mutation.HostId}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToArray();
            if (requested.Length == 0) return new DecorationActivationBatchResult(0, 0, Array.Empty<string>());

            var originals = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var planned = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mutation in requested)
            {
                var available = GetAvailableDecorationHostIds(mutation.DecorationId);
                if (available.Count == 0) continue;
                if (!originals.TryGetValue(mutation.DecorationId, out var original))
                {
                    original = _decorationActivations.GetEnabledHosts(mutation.DecorationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    originals[mutation.DecorationId] = original;
                    planned[mutation.DecorationId] = new HashSet<string>(original, StringComparer.OrdinalIgnoreCase);
                }

                var hosts = planned[mutation.DecorationId];
                if (string.IsNullOrWhiteSpace(mutation.HostId))
                {
                    if (mutation.Enabled) hosts.UnionWith(available);
                    else hosts.Clear();
                }
                else if (available.Contains(mutation.HostId))
                {
                    if (mutation.Enabled) hosts.Add(mutation.HostId);
                    else hosts.Remove(mutation.HostId);
                }
            }

            var changed = planned
                .Where(item => !item.Value.SetEquals(originals[item.Key]))
                .ToArray();
            if (changed.Length == 0) return new DecorationActivationBatchResult(0, 0, Array.Empty<string>());

            var affectedHosts = changed
                .SelectMany(item => originals[item.Key].Concat(item.Value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            try
            {
                await RebuildDecorationOverwritesAsync(affectedHosts, planned, cancellationToken).ConfigureAwait(false);
                await _decorationActivations.SetEnabledHostsBatchAsync(
                    changed.ToDictionary(item => item.Key, item => (IReadOnlyCollection<string>)item.Value.ToArray(), StringComparer.OrdinalIgnoreCase),
                    cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // A rebuild may have completed for earlier hosts before a later host failed.
                // Rebuild from the durable pre-operation state before surfacing the failure.
                try { await RebuildDecorationOverwritesAsync(affectedHosts, originals, CancellationToken.None).ConfigureAwait(false); }
                catch (Exception rollbackException) { LogService.Error($"装饰批处理回滚 Overwrite 失败：{rollbackException}"); }
                throw;
            }

            var changedNodeIds = affectedHosts
                .Select(ParseNodeId)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToArray();
            if (changedNodeIds.Length > 0)
                ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(changedNodeIds, ModContentChangeKind.Changed));
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            return new DecorationActivationBatchResult(changed.Length, affectedHosts.Length, affectedHosts);
        }

        private async Task<IReadOnlyList<ModNodeId>> RebuildDecorationOverwritesAsync(
            IEnumerable<string> hostIds,
            IReadOnlyDictionary<string, HashSet<string>>? activationOverrides,
            CancellationToken cancellationToken)
        {
            var changedHosts = new List<ModNodeId>();
            foreach (var hostId in hostIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseNodeId(hostId, out var nodeId) || !_snapshot.Nodes.TryGetValue(nodeId, out var host)) continue;
                var enabled = new List<(ModEntity Mod, DecorationPlanDocument Plan)>();
                foreach (var decoration in All().Where(mod => mod.IsDecoration && IsDecorationEnabledForHost(mod.Guid, hostId, activationOverrides)))
                {
                    var path = Path.Combine(_paths.ModsDirectory, decoration.SourcePath ?? string.Empty, "decoration.json");
                    if (!File.Exists(path)) continue;
                    var plan = JsonSerializer.Deserialize<DecorationPlanDocument>(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false), new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (plan is not null) enabled.Add((decoration, plan));
                }
                await _decorationAttachments.RebuildHostAsync(host, enabled, _paths.ModsDirectory, SettingsService.GetGameDataFolder(), cancellationToken).ConfigureAwait(false);
                await _informationCenter.InvalidateNodeAsync(host.Id, cancellationToken).ConfigureAwait(false);
                changedHosts.Add(host.Id);
            }
            return changedHosts;
        }

        private bool IsDecorationEnabledForHost(
            string decorationId,
            string hostId,
            IReadOnlyDictionary<string, HashSet<string>>? activationOverrides)
            => activationOverrides is not null && activationOverrides.TryGetValue(decorationId, out var overridden)
                ? overridden.Contains(hostId)
                : _decorationActivations.GetEnabledHosts(decorationId).Contains(hostId, StringComparer.OrdinalIgnoreCase);

        public DerivedModNodeData? GetDerivedData(string guid)
        {
            return TryParseNodeId(guid, out var nodeId) ? _derivedData.Find(nodeId) : null;
        }

        public string ResolveAbsolutePath(string? maybeRelative)
        {
            if (string.IsNullOrWhiteSpace(maybeRelative)) return string.Empty;
            if (Path.IsPathRooted(maybeRelative)) return maybeRelative;
            return Path.GetFullPath(Path.Combine(_paths.ModsDirectory, maybeRelative.Replace('/', Path.DirectorySeparatorChar)));
        }

        // 缩略图源事实的唯一 UI 入口；ThumbnailService 不负责发现或判定图像来源。
        public ValueTask<ModInformationResult<ModThumbnailFacts>> RequestThumbnailAsync(
            string guid,
            string source = "Manager",
            bool requireFresh = false,
            CancellationToken cancellationToken = default)
        {
            if (!TryParseNodeId(guid, out var nodeId) || !_snapshot.Nodes.TryGetValue(nodeId, out var node))
            {
                var issue = new CoreIssue(CoreIssueSeverity.Warning, "ThumbnailNodeUnavailable", "The requested Mod is not present in the library.", guid);
                return ValueTask.FromResult(new ModInformationResult<ModThumbnailFacts>(null, ModInformationStatus.Unavailable, ModInformationKind.Thumbnail, null, new[] { issue }));
            }

            return _informationCenter.RequestThumbnailAsync(
                node,
                _paths.ModsDirectory,
                new ModInformationRequest(ModInformationKind.Thumbnail, source, RequireFresh: requireFresh),
                cancellationToken);
        }

        public void ReplaceSnapshot(LibrarySnapshot snapshot, bool buildDerivedData = false)
        {
            _snapshot = snapshot ?? EmptySnapshot();
            Interlocked.Increment(ref _stateVersion);
            RebuildIndex(buildDerivedData);
        }

        public void NotifyImportCompleted()
        {
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        private void RebuildIndex(bool buildDerivedData = true)
        {
            if (buildDerivedData)
            {
                _derivedData = _derivedDataService.BuildAsync(_snapshot, _paths.ModsDirectory, SettingsService.GetGameDataFolder(), null).AsTask().GetAwaiter().GetResult();
            }

            RebuildEntityIndex(includePatchFiles: buildDerivedData);
        }

        private void RebuildEntityIndex(bool includePatchFiles = true)
        {
            var rebuilt = new Dictionary<string, ModEntity>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in _snapshot.Nodes.Values.OrderBy(n => n.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                rebuilt[node.Id.Value.ToString("N")] = ToEntity(node, includePatchFiles);
            }
            Volatile.Write(ref _byGuid, rebuilt);
        }

        private void UpdateEntityIndex(IEnumerable<ModNodeId> nodeIds)
        {
            var updated = new Dictionary<string, ModEntity>(Volatile.Read(ref _byGuid), StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in nodeIds)
            {
                if (_snapshot.Nodes.TryGetValue(nodeId, out var node)) updated[nodeId.Value.ToString("N")] = ToEntity(node);
                else updated.Remove(nodeId.Value.ToString("N"));
            }
            Volatile.Write(ref _byGuid, updated);
        }

        private ModEntity ToEntity(ModNode node, bool includePatchFiles = true)
        {
            var derived = _derivedData.Find(node.Id);
            return new ModEntity
            {
                Guid = node.Id.Value.ToString("N"),
                Name = node.Metadata.Name,
                Description = node.Metadata.Notes,
                Image = derived?.IconPath ?? ModIconLocator.TryResolve(ResolveAbsolutePath(node.RelativePath)),
                SourcePath = node.RelativePath,
                IsDecoration = node.Metadata.Kind == ModNodeKind.Decoration,
                IsOption = node.Metadata.Kind == ModNodeKind.Option,
                HostModGuids = node.Metadata.HostModGuids?.ToArray() ?? Array.Empty<string>(),
                HasPatchContent = node.PatchGroups.Count > 0,
                CreatedAt = node.Metadata.CreatedUtc.UtcDateTime,
                UpdatedAt = (node.Metadata.ModifiedUtc ?? node.Metadata.CreatedUtc).UtcDateTime,
                FileGroups = (includePatchFiles ? GetPatchFiles(node, derived) : Array.Empty<IndexedPatchFile>())
                    .Where(f => f.SidecarKind == PatchSidecarKind.Base)
                    .OrderBy(f => f.ArchiveHex16, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.NormalizedOrder)
                    .Select(f => new FileGroup
                    {
                        HexPrefix = f.ArchiveHex16,
                        PatchN = f.SourcePatchIndex,
                        RelativePath = node.RelativePath,
                        Files = new List<string> { f.FileName }
                    }).ToList(),
            };
        }

        private IReadOnlyList<IndexedPatchFile> GetPatchFiles(ModNode node, DerivedModNodeData? derived)
        {
            if (derived?.PatchFiles is { Count: > 0 } files) return files;
            try
            {
                var index = CoreServices.CreatePatchFileIndexBuilder().BuildAsync(_snapshot, _paths.ModsDirectory).AsTask().GetAwaiter().GetResult();
                return index.FilesByNode.TryGetValue(node.Id, out var nodeFiles) ? nodeFiles : Array.Empty<IndexedPatchFile>();
            }
            catch { return Array.Empty<IndexedPatchFile>(); }
        }

        private static bool TryParseNodeId(string? value, out ModNodeId nodeId)
        {
            nodeId = default;
            if (!Guid.TryParse(value, out var guid)) return false;
            nodeId = new ModNodeId(guid);
            return true;
        }

        private static ModNodeId? ParseNodeId(string? value)
            => TryParseNodeId(value, out var nodeId) ? nodeId : null;

        private static LibrarySnapshot EmptySnapshot() => new(
            Version: 1,
            SavedUtc: DateTimeOffset.UtcNow,
            Nodes: new Dictionary<ModNodeId, ModNode>(),
            Profiles: new List<HD2ModCore.Domain.Profile>());

        private static DerivedLibraryData EmptyDerivedData() => new(
            BuiltUtc: DateTimeOffset.UtcNow,
            Nodes: new Dictionary<ModNodeId, DerivedModNodeData>(),
            Issues: Array.Empty<CoreIssue>());

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (var sourcePath in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceDirectory, sourcePath);
                var destinationPath = Path.Combine(destinationDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }

        private static bool SameModGuid(string? left, string? right)
            => Guid.TryParse(left, out var leftGuid) && Guid.TryParse(right, out var rightGuid)
                ? leftGuid == rightGuid
                : string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> EnumeratePatchFiles(string directory)
        {
            var parser = CoreServices.CreatePatchFileNameParser();
            return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(path => parser.TryParse(Path.GetFileName(path), out _));
        }

        private static void CopyNonPatchFiles(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
        {
            var parser = CoreServices.CreatePatchFileNameParser();
            var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories)
                .Where(path => !parser.TryParse(Path.GetFileName(path), out _));
            CopyFiles(sourceDirectory, destinationDirectory, files, cancellationToken);
        }

        private static void CopyFiles(string sourceRoot, string destinationRoot, IEnumerable<string> sourcePaths, CancellationToken cancellationToken)
        {
            foreach (var sourcePath in sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(sourceRoot, sourcePath);
                var destinationPath = Path.Combine(destinationRoot, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                File.Copy(sourcePath, destinationPath, overwrite: true);
            }
        }

        private static void SetDirectoryReadOnly(string directory, bool readOnly)
        {
            if (!Directory.Exists(directory)) return;
            foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(path);
                File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
            }
        }
    }
}
