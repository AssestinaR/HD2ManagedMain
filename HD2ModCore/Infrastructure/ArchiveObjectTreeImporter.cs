using HD2ModCore.Application;
using HD2ModCore.Domain;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace HD2ModCore.Infrastructure;

// 作用：使用 SharpCompress 从 zip/7z/rar 解包到临时目录，然后复用文件夹导入生成对象树。
// Purpose: Uses SharpCompress to extract zip/7z/rar archives to a temp directory, then reuses folder import to build an object tree.
public sealed class ArchiveObjectTreeImporter : IArchiveObjectTreeImporter
{
	private readonly IObjectTreeImporter _folderImporter;

	public ArchiveObjectTreeImporter(IObjectTreeImporter folderImporter)
	{
		_folderImporter = folderImporter ?? throw new ArgumentNullException(nameof(folderImporter));
	}

	public async ValueTask<ImportedObjectTree> ImportArchiveAsync(string archiveFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(archiveFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(archiveFilePath));
		}

		var fullPath = Path.GetFullPath(archiveFilePath);
		if (!File.Exists(fullPath))
		{
			throw new FileNotFoundException("Archive not found.", fullPath);
		}

		var extractRoot = Path.Combine(Path.GetTempPath(), "HD2ModManager", "import", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractRoot);

		try
		{
			await Task.Run(() => ExtractToDirectory(fullPath, extractRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
			var tree = await _folderImporter.ImportFolderAsync(extractRoot, cancellationToken).ConfigureAwait(false);
			return tree with { SourceDisplayName = Path.GetFileName(fullPath) };
		}
		finally
		{
			try { Directory.Delete(extractRoot, recursive: true); } catch { }
		}
	}

	private static void ExtractToDirectory(string archiveFilePath, string destinationDirectory, CancellationToken cancellationToken)
	{
		using var archive = ArchiveFactory.Open(archiveFilePath);
		foreach (var entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (entry.IsDirectory)
			{
				continue;
			}

			entry.WriteToDirectory(destinationDirectory, new ExtractionOptions
			{
				ExtractFullPath = true,
				Overwrite = true,
			});
		}
	}
}
