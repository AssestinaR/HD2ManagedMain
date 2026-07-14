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
    private readonly IModContentFactsService _contentFacts;
    private readonly IProfileOverrideGraphService _profileGraph;
    private readonly IDeployedOverrideGraphService _deployedGraph;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _sync = new();
    private DerivedStateSnapshot _snapshot = DerivedStateSnapshot.Empty;
    private bool _contentDirty = true;
    private bool _expectedDirty = true;
    private bool _deployedDirty = true;
    private CancellationTokenSource? _refreshCancellation;
    private (ProfileId? Id, long Revision) _activeProfileSignature;

    public DerivedStateSnapshot Snapshot { get { lock (_sync) return _snapshot; } }
    public event EventHandler<DerivedStateSnapshot>? SnapshotChanged;

    public DerivedStateCoordinator(ModLibraryService library, ProfileService profiles)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        _profiles = profiles ?? throw new ArgumentNullException(nameof(profiles));
        _paths = SettingsService.CreateStoragePaths();
        _contentFacts = CoreServices.CreateModContentFactsService(_paths);
        _profileGraph = CoreServices.CreateProfileOverrideGraphService(_paths);
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
            _refreshCancellation?.Cancel();
            _refreshCancellation?.Dispose();
            _refreshCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            return RefreshCoreAsync(_refreshCancellation.Token);
        }
    }

    public IReadOnlyDictionary<ModNodeId, ModUserStatus> ProjectStatuses(ProfileId? selectedProfileId)
    {
        var snapshot = Snapshot;
        return ModUserStatusProjector.Project(_profiles.Snapshot, selectedProfileId, snapshot.ContentFacts, snapshot.ExpectedGraph, snapshot.DeployedGraph);
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var profileSnapshot = _profiles.Snapshot;
            var current = Snapshot;
            IReadOnlyDictionary<ModNodeId, ModContentFacts> content = current.ContentFacts;
            ProfileOverrideGraph? expected = current.ExpectedGraph;
            DeployedOverrideGraph? deployed = current.DeployedGraph;
            bool contentDirty;
            bool expectedDirty;
            bool deployedDirty;
            lock (_sync)
            {
                contentDirty = _contentDirty || !SameNodeSet(content.Keys, profileSnapshot.Nodes.Keys);
                expectedDirty = _expectedDirty;
                deployedDirty = _deployedDirty;
            }

            if (contentDirty)
            {
                content = await _contentFacts.GetLibraryFactsAsync(profileSnapshot, _library.ModsRootDirectory, null, cancellationToken).ConfigureAwait(false);
                expectedDirty = true;
            }

            var active = profileSnapshot.ActiveProfileId is { } activeId ? profileSnapshot.Profiles.FirstOrDefault(profile => profile.Id == activeId) : null;
            if (active is null)
            {
                expected = null;
            }
            else if (expectedDirty || !IsExpectedCurrent(expected, active, content))
            {
                expected = await _profileGraph.BuildAsync(active, profileSnapshot, _library.ModsRootDirectory, cancellationToken).ConfigureAwait(false);
            }

            var gameData = SettingsService.GetGameDataFolder();
            if (string.IsNullOrWhiteSpace(gameData) || !Directory.Exists(gameData))
            {
                deployed = null;
            }
            else if (deployedDirty || !IsDeploymentCurrent(deployed, gameData))
            {
                deployed = await _deployedGraph.BuildAsync(gameData, cancellationToken).ConfigureAwait(false);
            }

            var next = new DerivedStateSnapshot(content, expected, deployed, DateTimeOffset.UtcNow, null);
            lock (_sync)
            {
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
            var failed = Snapshot with { LastError = exception.Message };
            lock (_sync) _snapshot = failed;
            SnapshotChanged?.Invoke(this, failed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private void OnProfilesChanged(object? sender, EventArgs e)
    {
        var current = GetActiveProfileSignature(_profiles.Snapshot);
        var activeChanged = current != _activeProfileSignature;
        _activeProfileSignature = current;
        MarkDirty(content: false, expected: activeChanged, deployed: false);
    }
    private void OnContentChanged(object? sender, EventArgs e) => MarkDirty(content: true, expected: true, deployed: false);

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
    DeployedOverrideGraph? DeployedGraph,
    DateTimeOffset BuiltUtc,
    string? LastError)
{
    public static DerivedStateSnapshot Empty { get; } = new(new Dictionary<ModNodeId, ModContentFacts>(), null, null, DateTimeOffset.MinValue, null);
}
