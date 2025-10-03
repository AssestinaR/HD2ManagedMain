namespace HD2ModCore.Infrastructure;

// 作用：最小目录复制工具（用于将导入内容落库到 mods/），只复制文件并保持目录结构。
// Purpose: Minimal directory copy helper used to persist imported content under mods/, preserving structure.
internal static class DirectoryCopy
{
	public static void CopyRecursively(string sourceDir, string destDir, CancellationToken cancellationToken)
	{
		var source = new DirectoryInfo(sourceDir);
		if (!source.Exists)
		{
			throw new DirectoryNotFoundException(sourceDir);
		}

		Directory.CreateDirectory(destDir);

		foreach (var file in source.EnumerateFiles())
		{
			cancellationToken.ThrowIfCancellationRequested();
			var targetPath = Path.Combine(destDir, file.Name);
			file.CopyTo(targetPath, overwrite: true);
		}

		foreach (var subdir in source.EnumerateDirectories())
		{
			cancellationToken.ThrowIfCancellationRequested();
			CopyRecursively(subdir.FullName, Path.Combine(destDir, subdir.Name), cancellationToken);
		}
	}
}
