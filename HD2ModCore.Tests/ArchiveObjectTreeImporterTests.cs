using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证压缩包导入可通过解包到临时目录并生成对象树（使用 zip 作为最小可测样例）。
// Purpose: Verifies archive import extracts to a temp directory then builds an object tree (zip as minimal test sample).
public sealed class ArchiveObjectTreeImporterTests
{
	[Fact]
	public async Task ImportArchiveAsync_Zip_Works()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		var zipPath = Path.Combine(root, "mod.zip");
		try
		{
			using (var zip = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
			{
				zip.CreateEntry("root/9ba626afa44a3aa3.patch_0");
				zip.CreateEntry("root/child/9ba626afa44a3aa3.patch_1.stream");
			}

			var importer = new ArchiveObjectTreeImporter(new ObjectTreeImporter(new PatchFileNameParser()));
			var tree = await importer.ImportArchiveAsync(zipPath);

			Assert.NotNull(tree);
			Assert.True(tree.Nodes.ContainsKey(tree.RootId));
			Assert.Equal("mod.zip", tree.SourceDisplayName);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
