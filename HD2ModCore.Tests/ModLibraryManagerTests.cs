using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证库管理器可删除节点并同步清理 Profile 引用。
// Purpose: Verifies library manager can delete nodes and cleans up profile references.
public sealed class ModLibraryManagerTests
{
	[Fact]
	public async Task DeleteNodeAsync_RemovesNode_AndProfileEntries()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			var paths = new StoragePaths(root);
			var store = new JsonModLibraryStore(paths);
			var mgr = new ModLibraryManager(paths, store);

			var nodeId = ModNodeId.New();
			var node = new ModNode(
				Id: nodeId,
				RelativePath: Path.Combine("import1", "obj"),
				Metadata: new ModNodeMetadata("obj", null, Array.Empty<string>(), null, DateTimeOffset.UtcNow, null),
				PatchGroups: Array.Empty<PatchGroupKey>(),
				Children: Array.Empty<ModNodeId>());

			var profile = new Profile(ProfileId.New(), "p", DateTimeOffset.UtcNow, null, new[] { new ProfileEntry(nodeId, 0, true) });
			var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [nodeId] = node }, new[] { profile });
			await store.SaveAsync(snapshot);

			var updated = await mgr.DeleteNodeAsync(nodeId, deleteStoredFiles: false);
			Assert.False(updated.Nodes.ContainsKey(nodeId));
			Assert.Empty(updated.Profiles[0].Entries);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
