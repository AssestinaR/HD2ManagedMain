using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 JSON 模组库持久化存储的读写一致性与库/Profile 拆分。
// Purpose: Verifies JSON mod library store save/load roundtrip and library/Profile split.
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
				Metadata: new ModNodeMetadata("obj", "n", new[] { "tag1" }, DateTimeOffset.UtcNow, null),
				PatchGroups: new[] { new PatchGroupKey("9ba626afa44a3aa3", 0) },
				Children: Array.Empty<ModNodeId>());

			var profile = new Profile(ProfileId.New(), "p", DateTimeOffset.UtcNow, null, new[] { new ProfileEntry(nodeId, 0, true) });
			var snapshot = new LibrarySnapshot(Version: 1, SavedUtc: DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [nodeId] = node }, new[] { profile });

			await store.SaveAsync(snapshot);
			var loaded = await store.TryLoadAsync();
			var libraryJson = await File.ReadAllTextAsync(paths.LibraryPath);
			var profilesJson = await File.ReadAllTextAsync(paths.ProfilesPath);

			Assert.NotNull(loaded);
			Assert.Single(loaded!.Nodes);
			Assert.Single(loaded.Profiles);
			Assert.True(loaded.Nodes.ContainsKey(nodeId));
			Assert.Equal("p", loaded.Profiles[0].Name);
			Assert.Equal("obj", loaded.Nodes[nodeId].Metadata.Name);
			Assert.True(File.Exists(Path.Combine(paths.ModsDirectory, "library.json")));
			Assert.DoesNotContain("profiles", libraryJson, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("\"p\"", profilesJson);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task SaveAsync_KeepsChineseReadable()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			var paths = new StoragePaths(root);
			var store = new JsonModLibraryStore(paths);
			var nodeId = ModNodeId.New();
			var snapshot = new LibrarySnapshot(
				Version: 1,
				SavedUtc: DateTimeOffset.UtcNow,
				Nodes: new Dictionary<ModNodeId, ModNode>
				{
					[nodeId] = new ModNode(
						nodeId,
						Path.Combine("中文包", "护甲"),
						new ModNodeMetadata("中文模组", null, Array.Empty<string>(), DateTimeOffset.UtcNow, null),
						Array.Empty<PatchGroupKey>(),
						Array.Empty<ModNodeId>()),
				},
				Profiles: new[]
				{
					new Profile(ProfileId.New(), "中文配置", DateTimeOffset.UtcNow, null, Array.Empty<ProfileEntry>()),
				});

			await store.SaveAsync(snapshot);

			var libraryJson = await File.ReadAllTextAsync(paths.LibraryPath);
			var profilesJson = await File.ReadAllTextAsync(paths.ProfilesPath);

			Assert.Contains("中文模组", libraryJson);
			Assert.Contains("中文包", libraryJson);
			Assert.DoesNotContain("\\u4e2d", libraryJson, StringComparison.OrdinalIgnoreCase);
			Assert.Contains("中文配置", profilesJson);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
