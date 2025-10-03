using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证文件夹导入会拆出每个真实含 patch 的扁平 mod 节点。
// Purpose: Verifies folder import splits each real patch-containing directory into a flat mod node.
public sealed class ObjectTreeImporterTests
{
	[Fact]
	public async Task ImportFolderAsync_BuildsFlatNodes_FromPatchDirectories()
	{
		var root = Path.Combine(Path.GetTempPath(), "hd2coretests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			// root has a patch group
			File.WriteAllText(Path.Combine(root, "9ba626afa44a3aa3.patch_0"), "");

			var child1 = Path.Combine(root, "child1");
			Directory.CreateDirectory(child1);
			File.WriteAllText(Path.Combine(child1, "9ba626afa44a3aa3.patch_1.stream"), "");

			var child2 = Path.Combine(root, "child2");
			Directory.CreateDirectory(child2);

			var grand = Path.Combine(child2, "grand");
			Directory.CreateDirectory(grand);
			File.WriteAllText(Path.Combine(grand, "9ba626afa44a3aa3.patch_2.gpu_resources"), "");

			var importer = new ObjectTreeImporter(new PatchFileNameParser());
			var tree = await importer.ImportFolderAsync(root);

			Assert.NotNull(tree);
			Assert.True(tree.Nodes.ContainsKey(tree.RootId));

			Assert.Equal(3, tree.Nodes.Count);
			Assert.DoesNotContain(tree.Nodes.Values, n => n.RelativePath == "child2");
			Assert.Contains(tree.Nodes.Values, n => n.RelativePath == string.Empty);
			Assert.Contains(tree.Nodes.Values, n => n.RelativePath == "child1");
			Assert.Contains(tree.Nodes.Values, n => n.RelativePath == Path.Combine("child2", "grand"));
		}
		finally
		{
			try { Directory.Delete(root, recursive: true); } catch { }
		}
	}
}
