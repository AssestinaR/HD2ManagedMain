using HD2ModAdaptation.Analysis;
using HD2ModAdaptation.PatchReconstruction;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using Microsoft.Data.Sqlite;
using PatchAssetKey = HD2ModAdaptation.PatchReconstruction.AssetKey;

namespace HD2ModCore.Tests;

// 作用：验证引用图索引的原子替换、反向查询和节点删除。
// Purpose: Verifies atomic reference-graph replacement, reverse queries and node deletion.
public sealed class SqliteModFactsStoreTests
{
	[Fact]
	public async Task ReplaceNodeAsync_ReplacesReferences_AndDeleteRemovesThem()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-mod-facts-" + Guid.NewGuid().ToString("N"));
		var nodeId = new ModNodeId(Guid.NewGuid());
		var unit = new PatchAssetKey(1, 10);
		var material = new PatchAssetKey(2, 20);
		var replacementMaterial = new PatchAssetKey(2, 21);
		var texture = new PatchAssetKey(3, 30);
		try
		{
			var store = new HD2ModCore.Infrastructure.SqliteModFactsStore(new HD2ModCore.Infrastructure.StoragePaths(root));
			await store.ReplaceNodeAsync(CreateFacts(nodeId, new[]
			{
				new PatchAssetReference(unit, material, PatchReferenceKind.UnitMaterial, 1),
				new PatchAssetReference(material, texture, PatchReferenceKind.MaterialTexture, 2)
			}));

			Assert.Single(await store.FindConsumerFactsAsync(material));
			Assert.Equal(unit, (await store.FindConsumerFactsAsync(material))[0].Reference.SourceAssetKey);
			Assert.Equal(material, (await store.FindConsumerFactsAsync(texture))[0].Reference.SourceAssetKey);
			var providers = await store.FindProviderFactsAsync(new[] { material, texture });
			Assert.Equal(2, providers.Count);
			Assert.Contains(providers, provider => provider.AssetKey == new HD2ModCore.Domain.AssetKey(material.TypeId, material.FileId));
			Assert.Contains(providers, provider => provider.AssetKey == new HD2ModCore.Domain.AssetKey(texture.TypeId, texture.FileId));
			var batchConsumers = await store.FindConsumerFactsAsync(new[] { material, texture, new PatchAssetKey(9, 90) });
			Assert.Equal(2, batchConsumers.Count);
			Assert.Contains(batchConsumers, consumer => consumer.Reference.SourceAssetKey == unit && consumer.Reference.TargetAssetKey == material);
			Assert.Contains(batchConsumers, consumer => consumer.Reference.SourceAssetKey == material && consumer.Reference.TargetAssetKey == texture);

			await store.ReplaceNodeAsync(CreateFacts(nodeId, new[]
			{
				new PatchAssetReference(unit, replacementMaterial, PatchReferenceKind.UnitMaterial, 3)
			}));

			Assert.Empty(await store.FindConsumerFactsAsync(material));
			Assert.Single(await store.FindConsumerFactsAsync(replacementMaterial));
			Assert.Empty(await store.FindConsumerFactsAsync(texture));

			await store.DeleteNodeAsync(nodeId);
			Assert.Empty(await store.FindConsumerFactsAsync(replacementMaterial));
		}
		finally
		{
			SqliteConnection.ClearAllPools();
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static ReferenceGraphFacts CreateFacts(ModNodeId nodeId, IReadOnlyList<PatchAssetReference> references)
	{
		var assets = references
			.SelectMany(reference => new[] { reference.SourceAssetKey, reference.TargetAssetKey })
			.Distinct()
			.Select(key => new PatchAssetFact(key, "test.patch.toc", 0, 0, 0, false, false, false, false))
			.ToArray();
		var analysis = new PatchGroupAnalysis(
			new PatchGroupInput("test.patch.toc"),
			assets,
			references,
			[],
			DateTimeOffset.UtcNow,
			"test");
		return new ReferenceGraphFacts(nodeId, "mod", "generation", DateTimeOffset.UtcNow, [analysis], []);
	}
}
