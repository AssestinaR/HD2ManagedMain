using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从目录递归收集严格 .patch_数字 TOC 文件，避免把 .stream/.gpu_resources sidecar 当作 TOC。
// Purpose: Recursively collects strict .patch_number TOC files while avoiding .stream/.gpu_resources sidecars.
public sealed class PatchTocFileCollector : IPatchTocFileCollector
{
	public PatchTocFileSet Collect(string patchDirectoryPath)
	{
		if (string.IsNullOrWhiteSpace(patchDirectoryPath))
		{
			throw new ArgumentException("Patch directory path cannot be null or whitespace.", nameof(patchDirectoryPath));
		}

		var root = Path.GetFullPath(patchDirectoryPath);
		if (!Directory.Exists(root))
		{
			throw new DirectoryNotFoundException($"Patch directory does not exist: {root}");
		}

		var patchFiles = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
			.Where(path => IsPatchTocFile(Path.GetFileName(path)))
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToArray();

		return new PatchTocFileSet(root, patchFiles);
	}

	private static bool IsPatchTocFile(string fileName)
	{
		var marker = fileName.LastIndexOf(".patch_", StringComparison.OrdinalIgnoreCase);
		if (marker < 0)
		{
			return false;
		}

		var suffix = fileName.AsSpan(marker + ".patch_".Length);
		return suffix.Length > 0 && suffix.IndexOf('.') < 0 && suffix.IndexOfAnyExceptInRange('0', '9') < 0;
	}
}
