using HD2ModCore.Application;
using HD2ModCore.Domain;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace HD2ModCore.Infrastructure;

// 作用：将导入源扫描为对象树后复制到程序目录 mods/，并合并进 LibrarySnapshot 再持久化保存。
// Purpose: Imports a source by scanning to an object tree, copying into app-local mods/, merging into LibrarySnapshot and persisting.
public sealed class ModLibraryImporter : IModLibraryImporter
{
	private const int SnapshotVersion = 1;

	private readonly StoragePaths _paths;
	private readonly IObjectTreeImporter _folderImporter;
	private readonly IArchiveObjectTreeImporter _archiveImporter;
	private readonly IModLibraryStore _store;
	private readonly PatchFileNormalizer _normalizer;

	public ModLibraryImporter(
		StoragePaths paths,
		IObjectTreeImporter folderImporter,
		IArchiveObjectTreeImporter archiveImporter,
		IModLibraryStore store)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_folderImporter = folderImporter ?? throw new ArgumentNullException(nameof(folderImporter));
		_archiveImporter = archiveImporter ?? throw new ArgumentNullException(nameof(archiveImporter));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_normalizer = new PatchFileNormalizer(new PatchFileNameParser());
	}

	public async ValueTask<ImportResult> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		var full = Path.GetFullPath(folderPath);
		if (!Directory.Exists(full))
		{
			throw new DirectoryNotFoundException(full);
		}

		var sourceName = new DirectoryInfo(full).Name;
		var tree = await _folderImporter.ImportFolderAsync(full, cancellationToken).ConfigureAwait(false);

		// Persist source content under mods/<guid>/...
		var importId = Guid.NewGuid().ToString("N");
		var destRoot = Path.Combine(_paths.ModsDirectory, importId);
		Directory.CreateDirectory(_paths.ModsDirectory);
		DirectoryCopy.CopyRecursively(full, destRoot, cancellationToken);
		NormalizePatchDirectories(destRoot, cancellationToken);

		var storedTree = await _folderImporter.ImportFolderAsync(destRoot, cancellationToken).ConfigureAwait(false);
		var snapshot = await MergeIntoSnapshotAsync(storedTree, destRoot, cancellationToken).ConfigureAwait(false);
		await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

		return new ImportResult(snapshot, storedTree.RootId, sourceName);
	}

	public async ValueTask<ImportResult> ImportArchiveAsync(string archiveFilePath, CancellationToken cancellationToken = default)
	{
		var full = Path.GetFullPath(archiveFilePath);
		if (!File.Exists(full))
		{
			throw new FileNotFoundException("Archive file not found.", full);
		}

		// Persist extracted content under mods/<guid>/...
		var importId = Guid.NewGuid().ToString("N");
		var destRoot = Path.Combine(_paths.ModsDirectory, importId);
		Directory.CreateDirectory(destRoot);

		// Keep a copy of the source archive for reference/debug.
		var destArchivePath = Path.Combine(destRoot, Path.GetFileName(full));
		File.Copy(full, destArchivePath, overwrite: true);

		await Task.Run(() => ExtractToDirectory(full, destRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
		NormalizePatchDirectories(destRoot, cancellationToken);

		// Build the object tree from the stored extracted content.
		var tree = await _folderImporter.ImportFolderAsync(destRoot, cancellationToken).ConfigureAwait(false);
		var snapshot = await MergeIntoSnapshotAsync(tree, destRoot, cancellationToken).ConfigureAwait(false);
		await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

		return new ImportResult(snapshot, tree.RootId, Path.GetFileName(full));
	}

	private void NormalizePatchDirectories(string rootDirectory, CancellationToken cancellationToken)
	{
		foreach (var dir in Directory.EnumerateDirectories(rootDirectory, "*", SearchOption.AllDirectories).Prepend(rootDirectory))
		{
			cancellationToken.ThrowIfCancellationRequested();
			_normalizer.NormalizeDirectory(dir, cancellationToken);
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

	private async ValueTask<LibrarySnapshot> MergeIntoSnapshotAsync(ImportedObjectTree imported, string storedModsRoot, CancellationToken cancellationToken)
	{
		var current = await _store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
		var nodes = current?.Nodes is not null
			? new Dictionary<ModNodeId, ModNode>(current.Nodes)
			: new Dictionary<ModNodeId, ModNode>();

		foreach (var kvp in imported.Nodes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var node = kvp.Value;

			// Rewrite RelativePath so it is rooted under the stored mods folder for this import.
			var newRel = Path.Combine(Path.GetFileName(storedModsRoot), node.RelativePath);
			nodes[kvp.Key] = node with { RelativePath = newRel };
		}

		var profiles = current?.Profiles?.ToList() ?? new List<Profile>();

		return new LibrarySnapshot(
			Version: SnapshotVersion,
			SavedUtc: DateTimeOffset.UtcNow,
			Nodes: nodes,
			Profiles: profiles);
	}
}
