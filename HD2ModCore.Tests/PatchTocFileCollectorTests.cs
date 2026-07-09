using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 patch TOC 文件夹收集器只收集严格 .patch_数字 文件并排除 sidecar。
// Purpose: Verifies the patch TOC file collector only collects strict .patch_number files and excludes sidecars.
public sealed class PatchTocFileCollectorTests
{
	[Fact]
	public void Collect_DirectoryWithPatchFiles_ReturnsOnlyStrictPatchTocs()
	{
		var root = CreateTempDirectory();
		try
		{
			var nested = Directory.CreateDirectory(Path.Combine(root, "nested")).FullName;
			var patch0 = Path.Combine(root, "mod.patch_0");
			var patch10 = Path.Combine(nested, "mod.patch_10");
			File.WriteAllBytes(patch0, []);
			File.WriteAllBytes(patch10, []);
			File.WriteAllBytes(Path.Combine(root, "mod.patch_0.gpu_resources"), []);
			File.WriteAllBytes(Path.Combine(root, "mod.patch_0.stream"), []);
			File.WriteAllBytes(Path.Combine(root, "mod.patch_backup"), []);
			File.WriteAllBytes(Path.Combine(root, "mod.patch_a"), []);

			var fileSet = new PatchTocFileCollector().Collect(root);

			Assert.Equal(Path.GetFullPath(root), fileSet.RootDirectoryPath);
			Assert.Equal(2, fileSet.Count);
			Assert.Equal([patch0, patch10], fileSet.PatchTocFilePaths);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void Collect_MissingDirectory_Throws()
	{
		var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

		Assert.Throws<DirectoryNotFoundException>(() => new PatchTocFileCollector().Collect(missing));
	}

	private static string CreateTempDirectory()
	{
		var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}
}
