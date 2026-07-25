using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        private readonly HD2ModCore.Application.IModInformationCenter _informationCenter;
        private readonly HD2ModCore.Application.IModLibrarySynchronizer _synchronizer;
        private LibrarySnapshot _snapshot;
        private DerivedLibraryData _derivedData;
        private readonly Dictionary<string, ModEntity> _byGuid = new();
        private readonly SemaphoreSlim _derivedRefreshGate = new(1, 1);

        public ReadOnlyDictionary<string, ModEntity> ByGuid => new(_byGuid);
        public LibrarySnapshot Snapshot => _snapshot;
        public DerivedLibraryData DerivedData => _derivedData;
        public string ModsRootDirectory => _paths.ModsDirectory;
        public HD2ModCore.Application.IModInformationCenter InformationCenter => _informationCenter;
        public event EventHandler<ModContentFactsChangedEventArgs>? ModContentFactsChanged;
        public event EventHandler? SnapshotChanged;

        public ModLibraryService(string libraryPath, HD2ModCore.Application.IModInformationCenter informationCenter)
        {
            _paths = SettingsService.CreateStoragePaths();
            _manager = CoreServices.CreateModLibraryManager(_paths);
            _informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
            _synchronizer = CoreServices.CreateModLibrarySynchronizer();
            _derivedDataService = CoreServices.CreateLibraryDerivedDataService(_paths, _informationCenter);
            _snapshot = EmptySnapshot();
            _derivedData = EmptyDerivedData();
        }

        public void Load(bool buildDerivedData = true)
        {
            _snapshot = _manager.LoadOrCreateAsync().AsTask().GetAwaiter().GetResult();
            RebuildIndex(buildDerivedData);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        public Task RefreshDerivedDataAsync(CancellationToken cancellationToken = default)
            => RefreshDerivedDataAsync(guids: null, ModContentChangeKind.Changed, cancellationToken);

        public async Task<bool> SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            var result = await _synchronizer.SynchronizeAsync(_snapshot, _paths.ModsDirectory, cancellationToken).ConfigureAwait(false);
            if (!result.FilesystemChanged) return false;

            _snapshot = result.Snapshot;
            await CoreServices.CreateModLibraryStore(_paths).SaveAsync(_snapshot, cancellationToken).ConfigureAwait(false);
            foreach (var nodeId in result.ChangedNodeIds.Concat(result.MissingNodeIds))
                await _informationCenter.InvalidateNodeAsync(nodeId, cancellationToken).ConfigureAwait(false);
            RebuildIndex(buildDerivedData: false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            await RefreshDerivedDataAsync(result.AddedNodeIds.Concat(result.ChangedNodeIds).Select(id => id.Value.ToString("N")), ModContentChangeKind.Changed, cancellationToken).ConfigureAwait(false);
            return true;
        }

        public async Task RefreshDerivedDataAsync(IEnumerable<string>? guids, CancellationToken cancellationToken = default)
            => await RefreshDerivedDataAsync(guids, ModContentChangeKind.Changed, cancellationToken).ConfigureAwait(false);

        public async Task RefreshDerivedDataAsync(IEnumerable<string>? guids, ModContentChangeKind changeKind, CancellationToken cancellationToken = default)
        {
            await _derivedRefreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var snapshot = _snapshot;
                var nodeIds = guids is null
                    ? null
                    : guids.Select(ParseNodeId).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
                var rebuilt = await _derivedDataService.BuildAsync(snapshot, _paths.ModsDirectory, SettingsService.GetGameDataFolder(), nodeIds, cancellationToken).AsTask().ConfigureAwait(false);

                if (nodeIds is null)
                {
                    _derivedData = rebuilt;
                }
                else
                {
                    var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
                    foreach (var pair in rebuilt.Nodes) nodes[pair.Key] = pair.Value;
					var issues = _derivedData.Issues.Where(issue => issue.NodeId is null || !nodeIds!.Contains(issue.NodeId.Value)).Concat(rebuilt.Issues).ToList();
                    _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, issues);
                }
				if (nodeIds is null) RebuildEntityIndex(); else UpdateEntityIndex(nodeIds);
                IReadOnlyCollection<ModNodeId> changedNodeIds = nodeIds is null ? rebuilt.Nodes.Keys.ToArray() : nodeIds;
                if (changedNodeIds.Count > 0)
                {
                    ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(changedNodeIds, changeKind));
                }
            }
            finally
            {
                _derivedRefreshGate.Release();
            }
        }

        public void Save()
        {
            var store = CoreServices.CreateModLibraryStore(_paths);
            store.SaveAsync(_snapshot).AsTask().GetAwaiter().GetResult();
            RebuildIndex(buildDerivedData: false);
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool Add(ModEntity mod)
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
            RebuildIndex(buildDerivedData: false);
            return true;
        }

        public bool Remove(string guid)
        {
            if (!TryParseNodeId(guid, out var nodeId)) return false;
            _snapshot = _manager.DeleteNodeAsync(nodeId, deleteStoredFiles: true).AsTask().GetAwaiter().GetResult();
            _informationCenter.InvalidateNodeAsync(nodeId).AsTask().GetAwaiter().GetResult();
			ThumbnailService.DeleteCachedThumbnailsForSource(GetDerivedData(guid)?.IconPath);
            var nodes = _derivedData.Nodes.ToDictionary(pair => pair.Key, pair => pair.Value);
            nodes.Remove(nodeId);
            _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, nodes.Values.SelectMany(node => node.Issues).ToArray());
            RebuildIndex(buildDerivedData: false);
            ModContentFactsChanged?.Invoke(this, new ModContentFactsChangedEventArgs(new[] { nodeId }, ModContentChangeKind.Removed));
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public bool Rename(string guid, string newName)
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
            RebuildIndex(buildDerivedData: false);
            return true;
        }

        public ModEntity? Get(string guid)
        {
            _byGuid.TryGetValue(guid, out var m);
            return m;
        }

        public IEnumerable<ModEntity> All() => _byGuid.Values;

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

            RebuildEntityIndex();
        }

        private void RebuildEntityIndex()
        {
            _byGuid.Clear();
            foreach (var node in _snapshot.Nodes.Values.OrderBy(n => n.Metadata.Name, StringComparer.OrdinalIgnoreCase))
            {
                _byGuid[node.Id.Value.ToString("N")] = ToEntity(node);
            }
        }

        private void UpdateEntityIndex(IEnumerable<ModNodeId> nodeIds)
        {
            foreach (var nodeId in nodeIds)
            {
                if (_snapshot.Nodes.TryGetValue(nodeId, out var node)) _byGuid[nodeId.Value.ToString("N")] = ToEntity(node);
            }
        }

        private ModEntity ToEntity(ModNode node)
        {
            var derived = _derivedData.Find(node.Id);
            return new ModEntity
            {
                Guid = node.Id.Value.ToString("N"),
                Name = node.Metadata.Name,
                Description = node.Metadata.Notes,
                Image = derived?.IconPath ?? ModIconLocator.TryResolve(ResolveAbsolutePath(node.RelativePath)),
                SourcePath = node.RelativePath,
                CreatedAt = node.Metadata.CreatedUtc.UtcDateTime,
                UpdatedAt = (node.Metadata.ModifiedUtc ?? node.Metadata.CreatedUtc).UtcDateTime,
                FileGroups = (GetPatchFiles(node, derived).Where(f => f.SidecarKind == PatchSidecarKind.Base)
                    .OrderBy(f => f.ArchiveHex16, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.NormalizedOrder)
                    .Select(f => new FileGroup
                {
                    HexPrefix = f.ArchiveHex16,
                    PatchN = f.SourcePatchIndex,
                    RelativePath = node.RelativePath,
                    Files = new List<string> { f.FileName }
                }) ?? Enumerable.Empty<FileGroup>()).ToList(),
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
    }
}
