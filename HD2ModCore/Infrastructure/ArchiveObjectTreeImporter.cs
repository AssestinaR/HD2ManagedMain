using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：使用随程序分发的 7-Zip 解包到临时目录，然后复用文件夹导入生成对象树。
public sealed class ArchiveObjectTreeImporter : IArchiveObjectTreeImporter
{
	private readonly IObjectTreeImporter _folderImporter;
	private readonly SevenZipArchiveExtractor _archiveExtractor = new();

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
			await _archiveExtractor.ExtractAsync(fullPath, extractRoot, cancellationToken).ConfigureAwait(false);
			var tree = await _folderImporter.ImportFolderAsync(extractRoot, cancellationToken).ConfigureAwait(false);
			return tree with { SourceDisplayName = Path.GetFileName(fullPath) };
		}
		finally
		{
			try { Directory.Delete(extractRoot, recursive: true); } catch { }
		}
	}

}
