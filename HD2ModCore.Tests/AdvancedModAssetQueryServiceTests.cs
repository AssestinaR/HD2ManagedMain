using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using AdaptationAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

namespace HD2ModCore.Tests;

// Purpose: Verifies advanced asset targets prefer exact Mod Unit consumers over coincident Game Data mappings.
public sealed class AdvancedModAssetQueryServiceTests
{
	private const ulong UnitType = 0xe0a48d0be9a7453f;
	private const ulong MaterialType = 0xeac0b497876adedf;
	private const ulong TextureType = 0xcd4238c6a0c69e32;

	[Fact]
	public async Task QueryAsync_PrefersReverseModUnitConsumers_ForMaterialAndTexture()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-advanced-facts-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			var paths = new StoragePaths(root);
			var model = Node("Model");
			var material = Node("Material pack");
			var unit = new AdaptationAssetKey(UnitType, 1);
			var materialKey = new AdaptationAssetKey(MaterialType, 2);
			var texture = new AdaptationAssetKey(TextureType, 3);
			var store = new SqliteModFactsStore(paths);
			await store.SaveAsync(Cache(model, Facts(unit, [Reference(unit, materialKey, PatchReferenceKind.UnitMaterial)])));
			await store.SaveAsync(Cache(material, Facts(materialKey, texture, [Reference(materialKey, texture, PatchReferenceKind.MaterialTexture)])));
			var library = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [model.Id] = model, [material.Id] = material }, [], null);
			var mappings = new StubMappingService(materialKey, texture);
			var service = new AdvancedModAssetQueryService(store, mappings);

			var rows = await service.QueryAsync(material.Id, library, null, null);

			Assert.Contains(rows, row => row.AssetKey == new HD2ModCore.Domain.AssetKey(materialKey.TypeId, materialKey.FileId) && row.TargetSummary == "Mod 引用：Model / Unit 0x0000000000000001");
			Assert.Contains(rows, row => row.AssetKey == new HD2ModCore.Domain.AssetKey(texture.TypeId, texture.FileId) && row.TargetSummary == "Mod 引用：Model / Unit 0x0000000000000001");
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	private static ModNode Node(string name) => new(ModNodeId.New(), name, new ModNodeMetadata(name, null, DateTimeOffset.UtcNow, null), [], []);
	private static PatchGroupAnalysisCacheEntry Cache(ModNode node, params PatchGroupAnalysis[] analyses) => new(2, node.Id, node.RelativePath, [], DateTimeOffset.UtcNow, analyses);
	private static PatchGroupAnalysis Facts(AdaptationAssetKey first, IReadOnlyList<PatchAssetReference> references) => Facts([first], references);
	private static PatchGroupAnalysis Facts(AdaptationAssetKey first, AdaptationAssetKey second, IReadOnlyList<PatchAssetReference> references) => Facts([first, second], references);
	private static PatchGroupAnalysis Facts(IReadOnlyList<AdaptationAssetKey> assets, IReadOnlyList<PatchAssetReference> references)
		=> new(new PatchGroupInput(Guid.NewGuid() + ".patch_0"), assets.Select(key => new PatchAssetFact(key, "facts.patch_0", 1, 0, 0, key.TypeId == UnitType, false, key.TypeId == MaterialType, key.TypeId == TextureType)).ToArray(), references, [], DateTimeOffset.UtcNow, "patch-group-v2");
	private static PatchAssetReference Reference(AdaptationAssetKey source, AdaptationAssetKey target, PatchReferenceKind kind) => new(source, target, kind, 0);

	private sealed class StubMappingService(params AdaptationAssetKey[] mappedKeys) : IGameDataMappingFactsService
	{
		public ValueTask<GameDataMappingFacts> MapAsync(IReadOnlySet<HD2ModCore.Domain.AssetKey> assetKeys, CancellationToken cancellationToken = default)
		{
			var mapped = assetKeys.ToDictionary(
				key => key,
				key => new GameDataMappedAssetFact(key, "GameData name", "Material", AssetTypeCategory.Material, mappedKeys.Contains(new AdaptationAssetKey(key.TypeId, key.FileId)) ? [new ArchiveMetadata("unrelated", "Armor", "Unrelated armor")] : []));
			return ValueTask.FromResult(new GameDataMappingFacts("mapping", "index", "metadata", DateTimeOffset.UtcNow, mapped, []));
		}
	}
}