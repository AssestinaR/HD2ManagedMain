using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// Purpose: Verifies readable library summaries are projected from stable content facts without reopening Patch files.
public sealed class ModAssetSummaryProjectorTests
{
	[Fact]
	public async Task ProjectAsync_UsesStableFactsAndGameDataMapping()
	{
		var node = new ModNode(ModNodeId.New(), "model", new ModNodeMetadata("Model", null, DateTimeOffset.UtcNow, null), [], []);
		var key = new AssetKey(0xe0a48d0be9a7453f, 100);
		var group = new ModPatchGroupFact(new ModPatchGroupId(node.Id, "0123456789abcdef", 0), 0, [], new HashSet<AssetKey> { key }, []);
		var facts = new ModContentFacts(node.Id, node.RelativePath, "facts", DateTimeOffset.UtcNow, [group], []);
		var projector = new ModAssetSummaryProjector(new MappingService(key), new EmptyCatalogProvider());

		var summary = await projector.ProjectAsync(node, facts);

		var asset = Assert.Single(summary.Assets);
		Assert.Equal("B-08 Light Gunner", asset.ArchiveDisplayName);
		Assert.Equal("body", asset.FileDisplayName);
		Assert.Contains("armor", summary.DerivedTags);
		Assert.Contains("model", summary.DerivedTags);
	}

	private sealed class MappingService(AssetKey key) : IGameDataMappingFactsService
	{
		public ValueTask<GameDataMappingFacts> MapAsync(IReadOnlySet<AssetKey> assetKeys, CancellationToken cancellationToken = default)
		{
			var assets = assetKeys.ToDictionary(asset => asset, asset => asset == key
				? new GameDataMappedAssetFact(asset, "body", "Unit", AssetTypeCategory.Model, [new ArchiveMetadata("armor", "Armor", "B-08 Light Gunner")])
				: new GameDataMappedAssetFact(asset, $"0x{asset.FileId:x16}", "Unknown", AssetTypeCategory.Unknown, []));
			return ValueTask.FromResult(new GameDataMappingFacts("mapping", "index", "metadata", DateTimeOffset.UtcNow, assets, []));
		}
	}

	private sealed class EmptyCatalogProvider : IAssetMetadataCatalogProvider
	{
		public ValueTask<AssetMetadataCatalog> LoadAsync(CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(AssetMetadataCatalog.Empty);
	}
}