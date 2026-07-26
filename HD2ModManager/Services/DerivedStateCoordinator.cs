using System.IO;
using HD2ModCore.Domain;
using HD2ModCore.Application;
using HD2ModCore.Infrastructure;

namespace HD2ModManager.Services;

// Purpose: Shares immutable cached Mod-derived facts across pages and rebuilds only generations invalidated by library, profile, deployment or mapping changes.
public sealed class DerivedStateCoordinator : IAsyncDisposable
{
    private readonly ModLibraryService _library;
    private readonly ProfileService _profiles;
    private readonly StoragePaths _paths;
    private readonly IModInformationCenter _informationCenter;
    private readonly IProfileOverrideGraphService _profileGraph;
	private readonly IProfileMaterialDiagnosticsService _profileMaterialDiagnostics;
    private readonly IDeployedOverrideGraphService _deployedGraph;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private DerivedStateSnapshot _snapshot = DerivedStateSnapshot.Empty;
    private bool _contentDirty = true;
    private bool _expectedDirty = true;
    private bool _deployedDirty = true;
    private CancellationTokenSource? _refreshCancellation;
    private (ProfileId? Id, long Revision) _activeProfileSignature;
    private long _refreshVersion;

    public DerivedStateSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public IModInformationCenter InformationCenter => _informationCenter;
    public event EventHandler<DerivedStateSnapshot>? SnapshotChanged;

    public DerivedStateCoordinator(ModLibraryService library, ProfileService profiles, IModInformationCenter informationCenter)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _paths = SettingsService.CreateStoragePaths();
        _informationCenter = informationCenter ?? throw new ArgumentNullException(nameof(informationCenter));
        _profileGraph = CoreServices.CreateProfileOverrideGraphService(_paths, _informationCenter);
        _profileMaterialDiagnostics = CoreServices.CreateProfileMaterialDiagnosticsService(_paths, _informationCenter);
        _deployedGraph = CoreServices.CreateDeployedOverrideGraphService();
        _activeProfileSignature = GetActiveProfileSignature(_profiles.Snapshot);
        _profiles.Changed += OnProfilesChanged;
        _library.ModContentFactsChanged += OnContentChanged;
    }

    public void MarkDeploymentDirty() => MarkDirty(content: false, expected: false, deployed: true);
    public void MarkMappingDirty() => MarkDirty(content: false, expected: true, deployed: false);
    public void MarkContentDirty() => MarkDirty(content: true, expected: true, deployed: false);

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            var version = ++_refreshVersion;
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return RefreshCoreAsync(_refreshCancellation.Token, version);
        }
    }

    public IReadOnlyDictionary<ModNodeId, ModUserStatus> ProjectStatuses(ProfileId? selectedProfileId)
    {
        var snapshot = Snapshot;
        return ModUserStatusProjector.Project(_profiles.Snapshot, selectedProfileId, snapshot.ContentFacts, snapshot.ExpectedGraph, snapshot.MaterialDiagnostics, snapshot.DeployedGraph);
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken, long refreshVersion)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profileSnapshot = _profiles.Snapshot;
            var profileSignature = GetActiveProfileSignature(profileSnapshot);
            var librarySnapshotSignature = _library.Snapshot.SavedUtc;
            var current = Snapshot;
            IReadOnlyDictionary<ModNodeId, ModContentFacts> content = current.ContentFacts;
            ProfileOverrideGraph? expected = current.ExpectedGraph;
			ProfileMaterialDiagnostics? materialDiagnostics = current.MaterialDiagnostics;
            DeployedOverrideGraph? deployed = current.DeployedGraph;
            bool contentDirty;
            bool expectedDirty;
            bool deployedDirty;
            lock (_sync)
            {
                contentDirty = _contentDirty;
                expectedDirty = _expectedDirty;
                deployedDirty = _deployedDirty;
            }

            var active = profileSnapshot.ActiveProfileId is { } activeId ? profileSnapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
            var activeNodeIds = active?.Entries.Select(entry => entry.NodeId).Where(profileSnapshot.Nodes.ContainsKey).ToHashSet()
                ?? new HashSet<ModNodeId>();
            contentDirty |= !SameNodeSet(content.Keys, activeNodeIds);

            if (contentDirty)
            {
                content = activeNodeIds.Count == 0
                    ? new Dictionary<ModNodeId, ModContentFacts>()
                    : await GetAssetInventoryAsync(profileSnapshot, activeNodeIds, cancellationToken).ConfigureAwait(false);
                expectedDirty = true;
            }

            if (active is null)
            {
                expected = null;
				materialDiagnostics = null;
				deployed = null;
            }
            else if (expectedDirty || !IsExpectedCurrent(expected, active, content))
            {
                expected = await _profileGraph.BuildAsync(active, profileSnapshot, _library.ModsRootDirectory, cancellationToken).ConfigureAwait(false);
				materialDiagnostics = await _profileMaterialDiagnostics.BuildAsync(active, profileSnapshot, _library.ModsRootDirectory, cancellationToken).ConfigureAwait(false);
            }

            var gameData = SettingsService.GetGameDataFolder();
            if (active is null || string.IsNullOrWhiteSpace(gameData) || !Directory.Exists(gameData))
            {
                deployed = null;
            }
            else if (deployedDirty || !IsDeploymentCurrent(deployed, gameData))
            {
                deployed = await _deployedGraph.BuildAsync(gameData, cancellationToken).ConfigureAwait(false);
            }

            var next = new DerivedStateSnapshot(content, expected, materialDiagnostics, deployed, DateTimeOffset.UtcNow, null);
            lock (_sync)
            {
                if (refreshVersion != _refreshVersion
                    || profileSignature != GetActiveProfileSignature(_profiles.Snapshot)
                    || librarySnapshotSignature != _library.Snapshot.SavedUtc)
                    return;
                _contentDirty = false;
                _expectedDirty = false;
                _deployedDirty = false;
                _snapshot = next;
            }
            SnapshotChanged?.Invoke(this, next);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            lock (_sync)
            {
                if (refreshVersion != _refreshVersion) return;
            }
            var failed = Snapshot with { LastError = exception.Message };
            lock (_sync) _snapshot = failed;
            SnapshotChanged?.Invoke(this, failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetAssetInventoryAsync(
        LibrarySnapshot snapshot,
        IReadOnlySet<ModNodeId> nodeIds,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<ModNodeId, ModContentFacts>();
        foreach (var nodeId in nodeIds)
        {
            if (!snapshot.Nodes.TryGetValue(nodeId, out var node)) continue;
            var inventory = await _informationCenter.RequestAssetInventoryAsync(
                node,
                _library.ModsRootDirectory,
                new ModInformationRequest(ModInformationKind.AssetInventory, "LibraryRefresh"),
                cancellationToken).ConfigureAwait(false);
            if (inventory.Data is not null) result[nodeId] = inventory.Data;
        }
        return result;
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        var current = GetActiveProfileSignature(_profiles.Snapshot);
        var activeChanged = current != _activeProfileSignature;
        _activeProfileSignature = current;
        MarkDirty(content: false, expected: activeChanged, deployed: false);
    }
    private void OnContentChanged(object? sender, ModContentFactsChangedEventArgs e)
    {
        var active = _profiles.ActiveProfile;
        var affectsActiveProfile = active is not null && e.NodeIds.Any(nodeId => active.Entries.Any(entry => entry.NodeId == nodeId));
        if (affectsActiveProfile)
        {
            MarkDirty(content: true, expected: true, deployed: false);
        }
    }

    private void MarkDirty(bool content, bool expected, bool deployed)
    {
        lock (_sync)
        {
            _contentDirty |= content;
            _expectedDirty |= expected;
            _deployedDirty |= deployed;
        }
        _ = RefreshAsync();
    }

    private static bool SameNodeSet(IEnumerable<ModNodeId> left, IEnumerable<ModNodeId> right)
        => left.OrderBy(id => id.Value).SequenceEqual(right.OrderBy(id => id.Value));

    private static bool IsExpectedCurrent(ProfileOverrideGraph? graph, Profile active, IReadOnlyDictionary<ModNodeId, ModContentFacts> content)
    {
        if (graph is null || graph.ProfileId != active.Id || graph.ProfileRevision != active.Revision) return false;
        return graph.ContentGenerations.Count == content.Count && graph.ContentGenerations.All(pair => content.TryGetValue(pair.Key, out var facts) && string.Equals(pair.Value, facts.ContentGeneration, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsDeploymentCurrent(DeployedOverrideGraph? graph, string gameData)
        => graph is not null && string.Equals(Path.GetFullPath(graph.GameDataDirectory), Path.GetFullPath(gameData), StringComparison.OrdinalIgnoreCase);

    private static (ProfileId? Id, long Revision) GetActiveProfileSignature(LibrarySnapshot snapshot)
    {
        var active = snapshot.ActiveProfileId is { } activeId ? snapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
        return active is null ? (null, 0) : (active.Id, active.Revision);
    }

    public async ValueTask DisposeAsync()
    {
        _profiles.Changed -= OnProfilesChanged;
        _library.ModContentFactsChanged -= OnContentChanged;
        lock (_sync)
        {
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = null;
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
        _gate.Dispose();
    }
}

public sealed record DerivedStateSnapshot(
    IReadOnlyDictionary<ModNodeId, ModContentFacts> ContentFacts,
    ProfileOverrideGraph? ExpectedGraph,
	ProfileMaterialDiagnostics? MaterialDiagnostics,
    DeployedOverrideGraph? DeployedGraph,
    DateTimeOffset BuiltUtc,
    string? LastError)
{
    public static DerivedStateSnapshot Empty { get; } = new(new Dictionary<ModNodeId, ModContentFacts>(), null, null, null, DateTimeOffset.MinValue, null);
}
