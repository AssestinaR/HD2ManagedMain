using HD2ModCore.Domain;

namespace HD2ModCore.Tests;

// 作用：验证 FileFacts JSON 缓存的原子写入与读取。
// Purpose: Verifies atomic JSON FileFacts cache write and read behavior.
public sealed class JsonModFileFactsCacheTests
{
	[Fact]
	public async Task SaveAndLoadAsync_RoundTripsFacts()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-file-facts-cache-" + Guid.NewGuid().ToString("N"));
		var cache = new HD2ModCore.Infrastructure.JsonModFileFactsCache(new HD2ModCore.Infrastructure.StoragePaths(root));
		var nodeId = ModNodeId.New();
		var facts = new PatchFileIndex(DateTimeOffset.UtcNow, new Dictionary<ModNodeId, IReadOnlyList<IndexedPatchFile>> { [nodeId] = [] }, []);
		try
		{
			await cache.SaveAsync("generation:test", facts);
			var loaded = await cache.TryLoadAsync("generation:test");
			Assert.NotNull(loaded);
			Assert.True(loaded!.FilesByNode.ContainsKey(nodeId));
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task DeleteNodeAsync_IgnoresEmptyOrMalformedCacheEntries()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2-file-facts-cache-delete-" + Guid.NewGuid().ToString("N"));
		var paths = new HD2ModCore.Infrastructure.StoragePaths(root);
		var cache = new HD2ModCore.Infrastructure.JsonModFileFactsCache(paths);
		var nodeId = ModNodeId.New();
		try
		{
			await cache.SaveAsync("valid", new PatchFileIndex(DateTimeOffset.UtcNow, new Dictionary<ModNodeId, IReadOnlyList<IndexedPatchFile>> { [nodeId] = [] }, []));
			Directory.CreateDirectory(paths.IndexDirectory + "\\mod-information");
			File.WriteAllText(Path.Combine(paths.IndexDirectory, "mod-information", "empty.json"), "{\"facts\":null}");
			File.WriteAllText(Path.Combine(paths.IndexDirectory, "mod-information", "broken.json"), "not-json");

			await cache.DeleteNodeAsync(nodeId);

			Assert.False(File.Exists(Path.Combine(paths.IndexDirectory, "mod-information", "valid.json")));
			Assert.True(File.Exists(Path.Combine(paths.IndexDirectory, "mod-information", "empty.json")));
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}
}
