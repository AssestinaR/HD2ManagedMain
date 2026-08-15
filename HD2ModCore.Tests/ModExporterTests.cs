using System.IO.Compression;
using System.Text.Json;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证导出会生成 zip 并包含 manifest.json，且 zip 名称为根节点名。
// Purpose: Verifies export produces a zip containing manifest.json and zip name equals the root node name.
public sealed class ModExporterTests
{
	[Fact]
	public async Task ExportToZipAsync_WritesZip_WithManifest()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		var appRoot = Path.Combine(root, "app");
		var dest = Path.Combine(root, "out");
		Directory.CreateDirectory(appRoot);
		Directory.CreateDirectory(dest);

		try
		{
			var paths = new StoragePaths(appRoot);
			Directory.CreateDirectory(paths.ModsDirectory);

			var importId = "import1";
			var storedDir = Path.Combine(paths.ModsDirectory, importId);
			Directory.CreateDirectory(storedDir);
			File.WriteAllText(Path.Combine(storedDir, "9ba626afa44a3aa3.patch_0"), "");

			var rootId = ModNodeId.New();
			var node = new ModNode(
				Id: rootId,
				RelativePath: importId,
				Metadata: new ModNodeMetadata("MyRoot", "n", DateTimeOffset.UtcNow, null),
				PatchGroups: Array.Empty<PatchGroupKey>(),
				Children: Array.Empty<ModNodeId>());

			var snapshot = new LibrarySnapshot(1, DateTimeOffset.UtcNow, new Dictionary<ModNodeId, ModNode> { [rootId] = node }, Array.Empty<Profile>());
			var exporter = new ModExporter(paths);

			var zipPath = await exporter.ExportToZipAsync(rootId, snapshot, dest);
			Assert.True(File.Exists(zipPath));
			Assert.EndsWith("MyRoot.zip", zipPath, StringComparison.OrdinalIgnoreCase);

			using var zip = ZipFile.OpenRead(zipPath);
			Assert.NotNull(zip.GetEntry("manifest.json"));

			var entry = zip.GetEntry("manifest.json")!;
			using var s = entry.Open();
			using var sr = new StreamReader(s);
			var json = await sr.ReadToEndAsync();

			using var doc = JsonDocument.Parse(json);
			Assert.True(doc.RootElement.TryGetProperty("nodes", out _));
			var exportedNode = doc.RootElement.GetProperty("nodes")[0];
			Assert.Equal(rootId.Value.ToString("D"), exportedNode.GetProperty("guid").GetString());
			Assert.True(doc.RootElement.TryGetProperty("rootName", out var rn));
			Assert.Equal("MyRoot", rn.GetString());
			Assert.Equal(rootId.Value.ToString("D"), doc.RootElement.GetProperty("guid").GetString());
			Assert.DoesNotContain("tags", json, StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
