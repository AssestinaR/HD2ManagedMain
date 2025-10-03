using System.IO.Compression;
using System.Text.Json;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证导出 zip（含 manifest.json）可以被导入，并且自定义标签会写入节点元数据。
// Purpose: Verifies an exported zip (with manifest.json) can be imported and user tags are applied to node metadata.
public sealed class ModManifestImporterTests
{
	[Fact]
	public async Task ImportExportZipAsync_AppliesManifestTags()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		Directory.CreateDirectory(appRoot);

		var zipPath = Path.Combine(root, "export.zip");
		try
		{
			var manifest = new
			{
				version = 1,
				rootName = "Root",
				exportedUtc = DateTimeOffset.UtcNow,
				nodes = new[]
				{
					new { relativePath = "pack", name = "pack", notes = "n", tags = new[] { "chiffon" } }
				}
			};
			var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));

          using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				var m = zip.CreateEntry("manifest.json");
				await using (var s = m.Open())
				await using (var sw = new StreamWriter(s))
				{
					await sw.WriteAsync(manifestJson);
				}

				var e = zip.CreateEntry("pack/9ba626afa44a3aa3.patch_0");
				await using var es = e.Open();
				await es.WriteAsync(new byte[] { 0x00 }, 0, 1);
			}

			var paths = new StoragePaths(appRoot);
			var store = new JsonModLibraryStore(paths);
			var importer = new ModManifestImporter(paths, new ObjectTreeImporter(new PatchFileNameParser()), store);

			var result = await importer.ImportExportZipAsync(zipPath);
			Assert.True(result.Snapshot.Nodes.Count > 0);

			// Find the 'pack' node and verify its tag from manifest.
			var pack = result.Snapshot.Nodes.Values.FirstOrDefault(n => n.Metadata.Name == "pack");
			Assert.NotNull(pack);
			Assert.Contains("chiffon", pack!.Metadata.UserTags);
			Assert.Equal("n", pack.Metadata.Notes);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}

	[Fact]
	public async Task ImportExportZipAsync_ToleratesTrailingCommasAndComments()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		Directory.CreateDirectory(appRoot);

		var zipPath = Path.Combine(root, "export.zip");
		try
		{
          var manifestJson = """
{
  // comment
  "version": 1,
  "rootName": "Root",
  "exportedUtc": "2025-01-01T00:00:00Z",
  "nodes": [
	{
             "relativePath": "pack",
	  "name": "pack",
	  "notes": "n",
	  "tags": ["chiffon",],
	},
  ],
}
""";

			using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
			{
				var m = zip.CreateEntry("manifest.json");
				await using (var s = m.Open())
				await using (var sw = new StreamWriter(s))
				{
					await sw.WriteAsync(manifestJson);
				}

				zip.CreateEntry("pack/9ba626afa44a3aa3.patch_0");
			}

			var paths = new StoragePaths(appRoot);
			var store = new JsonModLibraryStore(paths);
			var importer = new ModManifestImporter(paths, new ObjectTreeImporter(new PatchFileNameParser()), store);

			var result = await importer.ImportExportZipAsync(zipPath);
			var pack = result.Snapshot.Nodes.Values.FirstOrDefault(n => n.Metadata.Name == "pack");
			Assert.NotNull(pack);
			Assert.Contains("chiffon", pack!.Metadata.UserTags);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
