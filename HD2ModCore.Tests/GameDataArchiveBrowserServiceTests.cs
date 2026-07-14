using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies Game Data archive rows are aggregated in Core from library and deployed facts.
public sealed class GameDataArchiveBrowserServiceTests
{
	[Fact]
	public async Task BuildAsync_ProjectsLibraryActiveAndEffectiveModsPerTargetArchive()
	{
		var key = new AssetKey(10, 20);
		var nodeId = ModNodeId.New();
		var profile = new Profile(ProfileId.New(), "Active", DateTimeOffset.UtcNow, null, [new ProfileEntry(nodeId, 0)]);
		var node = new ModNode(nodeId, "mod", new ModNodeMetadata("Mod A", null, DateTimeOffset.UtcNow, null), [], []);
		var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [nodeId] = node }, [profile], profile.Id);
		var content = new ModContentFacts(nodeId, "mod", "generation", DateTimeOffset.UtcNow,
		[
			new ModPatchGroupFact(new ModPatchGroupId(nodeId, "source", 4), 0, [], new HashSet<AssetKey> { key }, []),
		], []);
		var service = new GameDataArchiveBrowserService(
			new FakeIndexService(),
			new FakeContentService(content),
			new FakeMappingService(key),
			new FakeDeployedService(key, nodeId));

		var browser = await service.BuildAsync(snapshot, "mods", "data");

		Assert.NotNull(browser);
		var overlay = Assert.Single(browser!.Archives).Overlay;
		Assert.Contains(nodeId, overlay.LibraryModIds);
		Assert.Contains(nodeId, overlay.ActiveModIds);
		Assert.Contains(nodeId, overlay.EffectiveModIds);
		Assert.Equal(2, Assert.Single(overlay.EffectiveAssets).TargetPatchIndex);
	}

	private sealed class FakeIndexService : IAssetArchiveIndexService
	{
		public ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
		public ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataIndexFingerprint?>(new GameDataIndexFingerprint("data", DateTimeOffset.UtcNow, 1, 1, 1, "index"));
		public ValueTask<IReadOnlyList<GameDataArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<GameDataArchiveSummary>>([new GameDataArchiveSummary("target", "Target", "Armor", 1, "正常")]);
		public ValueTask<GameDataArchiveDetails?> GetArchiveDetailsAsync(string packageName, CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataArchiveDetails?>(null);
		public ValueTask<GameDataIndexStatus> GetIndexStatusAsync(string gameDataDirectory, string archiveHashesJson, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public ValueTask BuildOrRebuildAsync(string gameDataDirectory, string archiveHashesJson, IProgress<IndexBuildProgress>? progress = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(IReadOnlySet<AssetKey> assetKeys, CancellationToken cancellationToken = default) => throw new NotSupportedException();
		public ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(IReadOnlySet<AssetKey> assetKeys, IndexFilterSettings filterSettings, CancellationToken cancellationToken = default) => throw new NotSupportedException();
	}

	private sealed class FakeContentService : IModContentFactsService
	{
		private readonly ModContentFacts _facts;
		public FakeContentService(ModContentFacts facts) => _facts = facts;
		public ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(_facts);
		public ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(LibrarySnapshot snapshot, string modsRootDirectory, IReadOnlySet<ModNodeId>? nodeIds = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyDictionary<ModNodeId, ModContentFacts>>(new Dictionary<ModNodeId, ModContentFacts> { [_facts.NodeId] = _facts });
	}

	private sealed class FakeMappingService : IGameDataMappingFactsService
	{
		private readonly AssetKey _key;
		public FakeMappingService(AssetKey key) => _key = key;
		public ValueTask<GameDataMappingFacts> MapAsync(IReadOnlySet<AssetKey> assetKeys, CancellationToken cancellationToken = default) => ValueTask.FromResult(new GameDataMappingFacts("mapping", "index", "metadata", DateTimeOffset.UtcNow, new Dictionary<AssetKey, GameDataMappedAssetFact> { [_key] = new(_key, "body", "unit", AssetTypeCategory.Model, [new ArchiveMetadata("target", "Armor", "Target")]) }, []));
	}

	private sealed class FakeDeployedService : IDeployedOverrideGraphService
	{
		private readonly AssetKey _key;
		private readonly ModNodeId _nodeId;
		public FakeDeployedService(AssetKey key, ModNodeId nodeId) { _key = key; _nodeId = nodeId; }
		public ValueTask<DeployedOverrideGraph> BuildAsync(string gameDataDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(new DeployedOverrideGraph(gameDataDirectory, "deployed", DateTimeOffset.UtcNow, null, 0, [], [new DeployedAssetOverrideChain(_key, [new DeployedAssetOverrideEntry("source", 2, new ModPatchGroupId(_nodeId, "source", 4), _nodeId, true)])], []));
	}
}
