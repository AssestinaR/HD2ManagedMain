using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证导入器会把复杂目录拆成扁平 mod，并归一化 patch 编号。
// Purpose: Verifies importer splits complex directories into flat mods and normalizes patch numbering.
public sealed class ModLibraryImporterTests
{
	[Fact]
	public async Task ImportFolderAsync_SplitsPatchDirectories_AndNormalizesPatchNames()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		var src = Path.Combine(root, "src");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(src);

		try
		{
			Directory.CreateDirectory(Path.Combine(src, "variantA"));
			Directory.CreateDirectory(Path.Combine(src, "variantB"));
			File.WriteAllText(Path.Combine(src, "variantA", "9ba626afa44a3aa3.patch_5"), "A");
			File.WriteAllText(Path.Combine(src, "variantA", "9ba626afa44a3aa3.patch_5.stream"), "AS");
			File.WriteAllText(Path.Combine(src, "variantB", "9ba626afa44a3aa3.patch_9"), "B");

			var paths = new StoragePaths(appRoot);
			var importer = new ModLibraryImporter(
				paths,
				new ObjectTreeImporter(new PatchFileNameParser()),
				new ArchiveObjectTreeImporter(new ObjectTreeImporter(new PatchFileNameParser())),
				new JsonModLibraryStore(paths));

			var result = await importer.ImportFolderAsync(src);

			Assert.True(Directory.Exists(paths.ModsDirectory));
			Assert.True(File.Exists(Path.Combine(paths.LibraryDirectory, "library.json")));
			Assert.Equal(2, result.Snapshot.Nodes.Count);

			foreach (var node in result.Snapshot.Nodes.Values)
			{
				var storedDir = Path.Combine(appRoot, "mods", node.RelativePath);
				Assert.True(Directory.Exists(storedDir));
				Assert.True(File.Exists(Path.Combine(storedDir, "9ba626afa44a3aa3.patch_0")));
				Assert.DoesNotContain(Directory.EnumerateFiles(storedDir), p => Path.GetFileName(p).Contains("patch_5") || Path.GetFileName(p).Contains("patch_9"));
			}
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
