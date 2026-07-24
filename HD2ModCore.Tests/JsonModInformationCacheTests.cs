using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证统一信息缓存的 generation 隔离、旧条目回退和节点删除。
// Purpose: Verifies generation isolation, stale retrieval, and node deletion for unified information caching.
public sealed class JsonModInformationCacheTests
{
	[Fact]
	public async Task AssetInventory_RoundTripsReadOnlyAssetKeySet()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-cache-" + Guid.NewGuid().ToString("N"));
		try
		{
			var cache = new JsonModInformationCache(new StoragePaths(root));
			var nodeId = new ModNodeId(Guid.NewGuid());
			var key = new AssetKey(1, 2);
			var group = new ModPatchGroupFact(new ModPatchGroupId(nodeId, "group", 0), 0, [], new HashSet<AssetKey> { key }, []);
			var facts = new ModContentFacts(nodeId, "mod", "generation", DateTimeOffset.UtcNow, [group], []);
			await cache.SaveAsync(ModInformationKind.AssetInventory, nodeId, facts.ContentGeneration, facts);
			var loaded = await cache.TryLoadAsync<ModContentFacts>(ModInformationKind.AssetInventory, nodeId, facts.ContentGeneration);
			Assert.Contains(key, Assert.Single(loaded!.PatchGroups).AssetKeys);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public async Task CorruptCache_IsIgnoredAndDeleted()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-cache-" + Guid.NewGuid().ToString("N"));
		try
		{
			var cache = new JsonModInformationCache(new StoragePaths(root));
			var nodeId = new ModNodeId(Guid.NewGuid());
			await cache.SaveAsync(ModInformationKind.AssetInventory, nodeId, "bad", "not facts");
			Assert.Null(await cache.TryLoadAsync<ModContentFacts>(ModInformationKind.AssetInventory, nodeId, "bad"));
			Assert.Null(await cache.TryLoadLatestAsync<ModContentFacts>(ModInformationKind.AssetInventory, nodeId));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}
	[Fact]
	public async Task TryLoadLatestAsync_ReturnsMostRecentlySavedGeneration()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-cache-" + Guid.NewGuid().ToString("N"));
		try
		{
			var cache = new JsonModInformationCache(new StoragePaths(root));
			var nodeId = new ModNodeId(Guid.NewGuid());
			await cache.SaveAsync(ModInformationKind.Thumbnail, nodeId, "first", "old");
			await Task.Delay(20);
			await cache.SaveAsync(ModInformationKind.Thumbnail, nodeId, "second", "new");

			Assert.Null(await cache.TryLoadAsync<string>(ModInformationKind.Thumbnail, nodeId, "missing"));
			var latest = await cache.TryLoadLatestAsync<string>(ModInformationKind.Thumbnail, nodeId);
			Assert.NotNull(latest);
			Assert.Equal("second", latest.Generation);
			Assert.Equal("new", latest.Data);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task DeleteNodeAsync_OnlyDeletesRequestedNodeEntries()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-information-cache-" + Guid.NewGuid().ToString("N"));
		try
		{
			var cache = new JsonModInformationCache(new StoragePaths(root));
			var removedNode = new ModNodeId(Guid.NewGuid());
			var retainedNode = new ModNodeId(Guid.NewGuid());
			await cache.SaveAsync(ModInformationKind.Thumbnail, removedNode, "generation", "removed");
			await cache.SaveAsync(ModInformationKind.Thumbnail, retainedNode, "generation", "retained");

			await cache.DeleteNodeAsync(removedNode);

			Assert.Null(await cache.TryLoadLatestAsync<string>(ModInformationKind.Thumbnail, removedNode));
			Assert.Equal("retained", (await cache.TryLoadLatestAsync<string>(ModInformationKind.Thumbnail, retainedNode))!.Data);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}
}