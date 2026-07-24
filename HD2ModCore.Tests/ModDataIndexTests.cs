using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModCore.Application;
using HD2ModAdaptation.Analysis;

namespace HD2ModCore.Tests;

// 作用：验证跨 Mod 资产提供者/消费者索引及节点删除隔离。
// Purpose: Verifies cross-Mod provider/consumer indexing and node-removal isolation.
public sealed class ModDataIndexTests
{
	[Fact]
	public async Task UpdateAndRemoveNode_KeepProviderAndConsumerRelationsSeparate()
	{
		var index = new ModDataIndex();
		var providerNode = new ModNodeId(Guid.NewGuid());
		var consumerNode = new ModNodeId(Guid.NewGuid());
		var asset = new AssetKey(1, 2);
		var providerInventory = new ModContentFacts(
			providerNode,
			"provider",
			"provider-generation",
			DateTimeOffset.UtcNow,
			[new ModPatchGroupFact(new ModPatchGroupId(providerNode, "archive", 3), 0, [], new HashSet<AssetKey> { asset }, [])],
			[]);
		var consumerGraph = new ReferenceGraphFacts(
			consumerNode,
			"consumer",
			"consumer-generation",
			DateTimeOffset.UtcNow,
			[new PatchGroupAnalysis(new PatchGroupInput("consumer.toc"), [], [new PatchAssetReference(new HD2ModAdaptation.PatchReconstruction.AssetKey(asset.TypeId, asset.FileId), new HD2ModAdaptation.PatchReconstruction.AssetKey(asset.TypeId, asset.FileId), PatchReferenceKind.MaterialTexture, 0)], [], DateTimeOffset.UtcNow, "test")],
			[]);

		index.Update(providerInventory);
		index.Update(consumerGraph);

		Assert.Single(await index.FindProvidersAsync(asset));
		Assert.Single(await index.FindConsumersAsync(asset));

		await index.RemoveNodeAsync(providerNode);
		Assert.Empty(await index.FindProvidersAsync(asset));
		Assert.Single(await index.FindConsumersAsync(asset));
	}

	[Fact]
	public async Task PersistentIndex_RestoresAndResolvesLastProfileProvider()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-data-index-" + Guid.NewGuid().ToString("N"));
		try
		{
			var paths = new StoragePaths(root);
			var first = new ModDataIndex(paths);
			var earlier = new ModNodeId(Guid.NewGuid());
			var later = new ModNodeId(Guid.NewGuid());
			var asset = new AssetKey(3, 4);
			first.Update(CreateInventory(earlier, asset, "earlier"));
			first.Update(CreateInventory(later, asset, "later"));

			var restored = new ModDataIndex(paths);
			var profile = new Profile(ProfileId.New(), "test", DateTimeOffset.UtcNow, null, [new ProfileEntry(earlier, 0), new ProfileEntry(later, 1)]);
			var provider = await restored.ResolveFinalProviderAsync(asset, profile);

			Assert.NotNull(provider);
			Assert.Equal(later, provider.NodeId);
			Assert.Equal(2, (await restored.FindProvidersAsync(asset)).Count);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	private static ModContentFacts CreateInventory(ModNodeId nodeId, AssetKey asset, string path)
		=> new(nodeId, path, Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow,
			[new ModPatchGroupFact(new ModPatchGroupId(nodeId, path, 0), 0, [], new HashSet<AssetKey> { asset }, [])], []);
}