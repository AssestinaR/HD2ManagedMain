using System.IO.Compression;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证压缩包导入会直接解压到程序目录 mods/ 下，并将解压内容纳入对象树与快照。
// Purpose: Verifies archive import extracts directly under app-local mods/ and extracted content is included in the tree/snapshot.
public sealed class ModLibraryImporterArchiveTests
{
	[Fact]
	public async Task ImportArchiveAsync_ExtractsToMods_AndPersistsSnapshot()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		Directory.CreateDirectory(appRoot);

		var zipPath = Path.Combine(root, "mod.zip");
		try
		{
			using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				zip.CreateEntry("pack/9ba626afa44a3aa3.patch_0");
				zip.CreateEntry("pack/sub/9ba626afa44a3aa3.patch_1.stream");
			}

			var paths = new StoragePaths(appRoot);
			var importer = new ModLibraryImporter(
				paths,
				new ObjectTreeImporter(new PatchFileNameParser()),
				new ArchiveObjectTreeImporter(new ObjectTreeImporter(new PatchFileNameParser())),
				new JsonModLibraryStore(paths));

			var result = await importer.ImportArchiveAsync(zipPath);

			Assert.True(Directory.Exists(paths.ModsDirectory));
			Assert.True(File.Exists(Path.Combine(paths.ModsDirectory, "library.json")));
			Assert.False(Directory.Exists(paths.ImportTempDirectory) && Directory.EnumerateDirectories(paths.ImportTempDirectory, "import-*", SearchOption.TopDirectoryOnly).Any());

			// The source archive should not be copied into the portable mods library.
			Assert.False(Directory.EnumerateFiles(paths.ModsDirectory, "mod.zip", SearchOption.AllDirectories).Any());

			// The extracted patch should exist in a flattened mod directory below mods.
			Assert.True(Directory.EnumerateFiles(paths.ModsDirectory, "*.patch_0", SearchOption.AllDirectories).Any());

			Assert.Equal("mod", result.SourceDisplayName);
			Assert.True(result.Snapshot.Nodes.Count > 0);
			Assert.All(result.Snapshot.Nodes.Values, node => Assert.Single(node.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
