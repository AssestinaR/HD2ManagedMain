using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Models;

namespace HD2ModManager.Services
{
    // 作用：为现有 WPF UI 提供基于 HD2ModCore LibrarySnapshot 的模组库外观。
    public class ModLibraryService
    {
        private readonly StoragePaths _paths;
        private readonly HD2ModCore.Application.IModLibraryManager _manager;
        private readonly HD2ModCore.Application.ILibraryDerivedDataService _derivedDataService;
        private LibrarySnapshot _snapshot;
        private DerivedLibraryData _derivedData;
        private readonly Dictionary<string, ModEntity> _byGuid = new();
        private readonly SemaphoreSlim _derivedRefreshGate = new(1, 1);

        public ReadOnlyDictionary<string, ModEntity> ByGuid => new(_byGuid);
        public LibrarySnapshot Snapshot => _snapshot;
        public DerivedLibraryData DerivedData => _derivedData;
        public string ModsRootDirectory => _paths.ModsDirectory;
        public event EventHandler? ModContentFactsChanged;
        public event EventHandler? SnapshotChanged;

        public ModLibraryService(string libraryPath)
        {
            _paths = SettingsService.CreateStoragePaths();
            _manager = CoreServices.CreateModLibraryManager(_paths);
            _derivedDataService = CoreServices.CreateLibraryDerivedDataService(_paths);
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
            => RefreshDerivedDataAsync(guids: null, cancellationToken);

        public async Task RefreshDerivedDataAsync(IEnumerable<string>? guids, CancellationToken cancellationToken = default)
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
                    var issues = nodes.Values.SelectMany(node => node.Issues).ToList();
                    _derivedData = new DerivedLibraryData(DateTimeOffset.UtcNow, nodes, issues);
                }
                RebuildEntityIndex();
                if (nodeIds is null || nodeIds.Count > 0)
                {
                    ModContentFactsChanged?.Invoke(this, EventArgs.Empty);
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
            RebuildIndex(buildDerivedData: false);
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

        public void ReplaceSnapshot(LibrarySnapshot snapshot, bool buildDerivedData = false)
        {
            _snapshot = snapshot ?? EmptySnapshot();
            RebuildIndex(buildDerivedData);
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

        private ModEntity ToEntity(ModNode node)
        {
            var derived = _derivedData.Find(node.Id);
            return new ModEntity
            {
                Guid = node.Id.Value.ToString("N"),
                Name = node.Metadata.Name,
                Description = node.Metadata.Notes,
                Image = derived?.IconPath,
                SourcePath = node.RelativePath,
                CreatedAt = node.Metadata.CreatedUtc.UtcDateTime,
                UpdatedAt = (node.Metadata.ModifiedUtc ?? node.Metadata.CreatedUtc).UtcDateTime,
                FileGroups = (derived?.PatchFiles.Where(f => f.SidecarKind == PatchSidecarKind.Base)
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
