using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies Game Data mapping retains every archive target and exposes index/metadata generations.
public sealed class GameDataMappingFactsServiceTests
{
	[Fact]
	public async Task MapAsync_RetainsAllTargetsAndChangesWithIndexGeneration()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-mapping-facts-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var paths = new StoragePaths(root);
			Directory.CreateDirectory(paths.ResourcesDirectory);
			File.WriteAllText(paths.FriendlyNamesPath, "20 content/armor/body\n");
			File.WriteAllText(paths.TypeHashesPath, "000000000000000a unit\n");
			File.WriteAllText(paths.ArchiveHashesPath, "{}");
			var key = new AssetKey(10, 20);
			var index = new StubIndexService("index-1", new Dictionary<AssetKey, IReadOnlyList<ArchiveMetadata>>
			{
				[key] =
				[
					new ArchiveMetadata("target-b", "Armor", "Armor B", 0, 1),
					new ArchiveMetadata("target-a", "Armor", "Armor A", 0, 0),
				],
			});
			var service = new GameDataMappingFactsService(index, new FileSystemAssetMetadataCatalogProvider(paths), paths);

			var first = await service.MapAsync(new HashSet<AssetKey> { key });
			index.Generation = "index-2";
			var second = await service.MapAsync(new HashSet<AssetKey> { key });

			var mapped = first.Assets[key];
			Assert.Equal(new[] { "target-a", "target-b" }, mapped.TargetArchives.Select(archive => archive.ArchiveId));
			Assert.Equal("content/armor/body", mapped.FileDisplayName);
			Assert.Equal("unit", mapped.TypeDisplayName);
			Assert.Equal("index-1", first.IndexGeneration);
			Assert.NotEqual(first.MappingGeneration, second.MappingGeneration);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	private sealed class StubIndexService : IAssetArchiveIndexService
	{
		public ValueTask<IReadOnlyList<GameDataStreamLayoutFact>> FindStreamLayoutsAsync(IReadOnlyList<GameDataStreamComponentFact> components, uint vertexStride, bool requireSkinned = false, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<GameDataStreamLayoutFact>>([]);
		public ValueTask<IReadOnlyList<GameDataStreamLayoutFact>> GetStreamLayoutsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<GameDataStreamLayoutFact>>([]);
		private readonly IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>> _matches;
		public string Generation { get; set; }
		public StubIndexService(string generation, IReadOnlyDictionary<AssetKey, IReadOnlyList<ArchiveMetadata>> matches) { Generation = generation; _matches = matches; }
		public ValueTask<bool> IndexExistsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
		public ValueTask<GameDataIndexFingerprint?> GetFingerprintAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataIndexFingerprint?>(new GameDataIndexFingerprint("data", DateTimeOffset.UtcNow, 2, 2, 1, Generation));
		public ValueTask<IReadOnlyList<GameDataArchiveSummary>> GetArchiveSummariesAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<GameDataArchiveSummary>>([]);
		public ValueTask<GameDataArchiveDetails?> GetArchiveDetailsAsync(string packageName, CancellationToken cancellationToken = default) => ValueTask.FromResult<GameDataArchiveDetails?>(null);
		public ValueTask<IReadOnlyDictionary<AssetKey, IReadOnlyList<GameDataUnitPartFact>>> GetUnitPartFactsAsync(IReadOnlySet<AssetKey> unitAssetKeys, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyDictionary<AssetKey, IReadOnlyList<GameDataUnitPartFact>>>(new Dictionary<AssetKey, IReadOnlyList<GameDataUnitPartFact>>());
		public ValueTask<GameDataIndexStatus> GetIndexStatusAsync(string gameDataDirectory, string archiveHashesJson, CancellationToken cancellationToken = default) => ValueTask.FromResult(new GameDataIndexStatus(GameDataIndexState.Current, null, gameDataDirectory, Generation));
		public ValueTask BuildOrRebuildAsync(string gameDataDirectory, string archiveHashesJson, IProgress<IndexBuildProgress>? progress = null, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
		public ValueTask<IReadOnlyList<AssetArchiveMatch>> FindAssetArchivesAsync(IReadOnlySet<AssetKey> assetKeys, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyList<AssetArchiveMatch>>(assetKeys.Select(key => new AssetArchiveMatch(key, _matches.GetValueOrDefault(key) ?? [])).ToList());
		public ValueTask<IReadOnlyDictionary<string, int>> VoteArchivesAsync(IReadOnlySet<AssetKey> assetKeys, IndexFilterSettings filterSettings, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());
	}
}
