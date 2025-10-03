using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 JSON 模组库持久化存储的读写一致性。
// Purpose: Verifies JSON mod library store save/load roundtrip.
public sealed class JsonModLibraryStoreTests
{
	[Fact]
	public async Task SaveAndLoad_Roundtrip_Works()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			var paths = new StoragePaths(root);
			var store = new JsonModLibraryStore(paths);

			var nodeId = ModNodeId.New();
			var node = new ModNode(
				Id: nodeId,
				RelativePath: "obj",
				Metadata: new ModNodeMetadata("obj", "n", new[] { "tag1" }, null, DateTimeOffset.UtcNow, null),
				PatchGroups: new[] { new PatchGroupKey("9ba626afa44a3aa3", 0) },
				Children: Array.Empty<ModNodeId>());

			var profile = new Profile(ProfileId.New(), "p", DateTimeOffset.UtcNow, null, new[] { new ProfileEntry(nodeId, 0, true) });
			var snapshot = new LibrarySnapshot(Version: 1, SavedUtc: DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [nodeId] = node }, new[] { profile });

			await store.SaveAsync(snapshot);
			var loaded = await store.TryLoadAsync();

			Assert.NotNull(loaded);
			Assert.Single(loaded!.Nodes);
			Assert.Single(loaded.Profiles);
			Assert.True(loaded.Nodes.ContainsKey(nodeId));
			Assert.Equal("p", loaded.Profiles[0].Name);
			Assert.Equal("obj", loaded.Nodes[nodeId].Metadata.Name);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
