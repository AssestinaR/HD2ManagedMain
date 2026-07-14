using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies simple player status projection without exposing technical override identities.
public sealed class ModUserStatusServiceTests
{
	[Fact]
	public async Task GetStatusesAsync_ProjectsStoredCurrentAndActiveOverrideStates()
	{
		var firstId = ModNodeId.New();
		var secondId = ModNodeId.New();
		var thirdId = ModNodeId.New();
		var profile = new Profile(ProfileId.New(), "Active", DateTimeOffset.UtcNow, null, [new ProfileEntry(firstId, 0), new ProfileEntry(secondId, 1)]);
		var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode>
		{
			[firstId] = CreateNode(firstId, "First"),
			[secondId] = CreateNode(secondId, "Second"),
			[thirdId] = CreateNode(thirdId, "Stored"),
		}, [profile], profile.Id);
		var content = new FakeContentFactsService(snapshot.Nodes.Keys.ToDictionary(id => id, id => Facts(snapshot.Nodes[id])));
		var graph = new FakeProfileGraphService(new ProfileOverrideGraph(profile.Id, profile.Revision, "graph", "mapping", DateTimeOffset.UtcNow, new Dictionary<ModNodeId, string>(), [], [],
		[
			new ProfileModCoverage(firstId, "First", 2, 0, 2),
			new ProfileModCoverage(secondId, "Second", 2, 1, 1),
		], []));
		var service = new ModUserStatusService(content, graph, new FakeDeployedGraphService());

		var statuses = await service.GetStatusesAsync(snapshot, profile.Id, "mods", null);

		Assert.Equal(ModUserStatusKind.FullyOverridden, statuses[firstId].Kind);
		Assert.Equal(ModUserStatusKind.PartiallyOverridden, statuses[secondId].Kind);
		Assert.Equal(ModUserStatusKind.Stored, statuses[thirdId].Kind);
		Assert.DoesNotContain("AssetKey", statuses[firstId].Summary, StringComparison.OrdinalIgnoreCase);
	}

	private static ModNode CreateNode(ModNodeId id, string name)
		=> new(id, name, new ModNodeMetadata(name, null, DateTimeOffset.UtcNow, null), [], []);

	private static ModContentFacts Facts(ModNode node)
		=> new(node.Id, node.RelativePath, "content", DateTimeOffset.UtcNow, [], []);

	private sealed class FakeContentFactsService : IModContentFactsService
	{
		private readonly IReadOnlyDictionary<ModNodeId, ModContentFacts> _facts;
		public FakeContentFactsService(IReadOnlyDictionary<ModNodeId, ModContentFacts> facts) => _facts = facts;
		public ValueTask<ModContentFacts> GetNodeFactsAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(_facts[node.Id]);
		public ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(LibrarySnapshot snapshot, string modsRootDirectory, IReadOnlySet<ModNodeId>? nodeIds = null, CancellationToken cancellationToken = default) => ValueTask.FromResult(_facts);
	}

	private sealed class FakeProfileGraphService : IProfileOverrideGraphService
	{
		private readonly ProfileOverrideGraph _graph;
		public FakeProfileGraphService(ProfileOverrideGraph graph) => _graph = graph;
		public ValueTask<ProfileOverrideGraph> BuildAsync(Profile profile, LibrarySnapshot snapshot, string modsRootDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(_graph);
	}

	private sealed class FakeDeployedGraphService : IDeployedOverrideGraphService
	{
		public ValueTask<DeployedOverrideGraph> BuildAsync(string gameDataDirectory, CancellationToken cancellationToken = default) => ValueTask.FromResult(new DeployedOverrideGraph(gameDataDirectory, "actual", DateTimeOffset.UtcNow, null, 0, [], [], []));
	}
}
