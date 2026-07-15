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
	private readonly IPatchGroupAnalysisProvider? _patchFactsProvider;
	private readonly IModFactsStore? _modFactsStore;
	private readonly PatchFileNormalizer _normalizer;

	public ModLibraryImporter(
		StoragePaths paths,
		IObjectTreeImporter folderImporter,
		IArchiveObjectTreeImporter archiveImporter,
		IModLibraryStore store,
		IPatchGroupAnalysisProvider? patchFactsProvider = null,
		IModFactsStore? modFactsStore = null)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_folderImporter = folderImporter ?? throw new ArgumentNullException(nameof(folderImporter));
		_archiveImporter = archiveImporter ?? throw new ArgumentNullException(nameof(archiveImporter));
		_store = store ?? throw new ArgumentNullException(nameof(store));
		_patchFactsProvider = patchFactsProvider;
		_modFactsStore = modFactsStore;
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
		var (storedTree, snapshot) = await CommitImportAsync(tree, full, sourceName, cancellationToken).ConfigureAwait(false);

		return new ImportResult(snapshot, storedTree.RootId, sourceName);
	}

	public async ValueTask<ImportResult> ImportArchiveAsync(string archiveFilePath, CancellationToken cancellationToken = default)
	{
		var full = Path.GetFullPath(archiveFilePath);
		if (!File.Exists(full))
		{
			throw new FileNotFoundException("Archive file not found.", full);
		}

		var sourceName = Path.GetFileNameWithoutExtension(full);
		var extractRoot = Path.Combine(Path.GetTempPath(), "HD2ModCore", "import", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(extractRoot);

		ImportedObjectTree storedTree;
		LibrarySnapshot snapshot;
		try
		{
			await Task.Run(() => ExtractToDirectory(full, extractRoot, cancellationToken), cancellationToken).ConfigureAwait(false);
			var tree = await _folderImporter.ImportFolderAsync(extractRoot, cancellationToken).ConfigureAwait(false);
			(storedTree, snapshot) = await CommitImportAsync(tree, extractRoot, sourceName, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			try { Directory.Delete(extractRoot, recursive: true); } catch { }
		}

		return new ImportResult(snapshot, storedTree.RootId, sourceName);
	}

	private async ValueTask<(ImportedObjectTree Tree, LibrarySnapshot Snapshot)> CommitImportAsync(ImportedObjectTree tree, string sourceRoot, string sourceName, CancellationToken cancellationToken)
	{
		ImportedObjectTree? storedTree = null;
		try
		{
			storedTree = PersistFlattenedTree(tree, sourceRoot, sourceName, cancellationToken);
			await PersistStableFactsAsync(storedTree, cancellationToken).ConfigureAwait(false);
			var snapshot = await MergeIntoSnapshotAsync(storedTree, cancellationToken).ConfigureAwait(false);
			await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
			return (storedTree, snapshot);
		}
		catch
		{
			if (storedTree is not null) await RollbackStoredTreeAsync(storedTree).ConfigureAwait(false);
			throw;
		}
	}

	private async ValueTask PersistStableFactsAsync(ImportedObjectTree storedTree, CancellationToken cancellationToken)
	{
		if (_patchFactsProvider is null) return;
		foreach (var node in storedTree.Nodes.Values)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await _patchFactsProvider.AnalyzeNodeAsync(node, _paths.ModsDirectory, cancellationToken).ConfigureAwait(false);
		}
	}

	private async ValueTask RollbackStoredTreeAsync(ImportedObjectTree storedTree)
	{
		foreach (var node in storedTree.Nodes.Values)
		{
			try
			{
				if (_modFactsStore is not null) await _modFactsStore.DeleteAsync(node.Id).ConfigureAwait(false);
				var directory = Path.Combine(_paths.ModsDirectory, node.RelativePath);
				if (Directory.Exists(directory))
				{
					SetReadOnlyRecursive(directory, readOnly: false);
					Directory.Delete(directory, recursive: true);
				}
			}
			catch { }
		}
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

	private ImportedObjectTree PersistFlattenedTree(ImportedObjectTree imported, string sourceRoot, string sourceDisplayName, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(_paths.ModsDirectory);
		var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var nodes = new Dictionary<ModNodeId, ModNode>();

		foreach (var kvp in imported.Nodes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var node = kvp.Value;
			var sourceNodeDir = string.IsNullOrWhiteSpace(node.RelativePath)
				? sourceRoot
				: Path.Combine(sourceRoot, node.RelativePath);

			var displayName = BuildDisplayName(sourceDisplayName, node.RelativePath);
			var flatDirName = CreateUniqueDirectoryName(_paths.ModsDirectory, displayName, usedNames);
			var destDir = Path.Combine(_paths.ModsDirectory, flatDirName);
			CopyTopLevelFiles(sourceNodeDir, destDir, cancellationToken);
			NormalizePatchDirectories(destDir, cancellationToken);
			SetReadOnlyRecursive(destDir);

			nodes[kvp.Key] = node with
			{
				RelativePath = flatDirName,
				Metadata = node.Metadata with { Name = displayName },
				Children = Array.Empty<ModNodeId>(),
			};
		}

		return imported with
		{
			Nodes = nodes,
			SourceDisplayName = sourceDisplayName,
		};
	}

	private async ValueTask<LibrarySnapshot> MergeIntoSnapshotAsync(ImportedObjectTree imported, CancellationToken cancellationToken)
	{
		var current = await _store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
		var nodes = current?.Nodes is not null
			? new Dictionary<ModNodeId, ModNode>(current.Nodes)
			: new Dictionary<ModNodeId, ModNode>();

		foreach (var kvp in imported.Nodes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			nodes[kvp.Key] = kvp.Value;
		}

		var profiles = current?.Profiles?.ToList() ?? new List<Profile>();

		return new LibrarySnapshot(
			Version: current?.Version ?? SnapshotVersion,
			SavedUtc: DateTimeOffset.UtcNow,
			Nodes: nodes,
			Profiles: profiles,
			ActiveProfileId: current?.ActiveProfileId);
	}

	private static void CopyTopLevelFiles(string sourceDir, string destDir, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(destDir);
		foreach (var file in Directory.EnumerateFiles(sourceDir))
		{
			cancellationToken.ThrowIfCancellationRequested();
			File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
		}
	}

	private static string BuildDisplayName(string sourceDisplayName, string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
		{
			return sourceDisplayName;
		}

		var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
		return string.Join('-', new[] { sourceDisplayName }.Concat(parts));
	}

	private static string CreateUniqueDirectoryName(string parent, string displayName, HashSet<string>? reservedNames = null)
	{
		var baseName = SanitizeFileName(displayName);
		var candidate = baseName;
		var index = 2;
		while ((reservedNames is not null && !reservedNames.Add(candidate)) || Directory.Exists(Path.Combine(parent, candidate)))
		{
			candidate = $"{baseName}_{index}";
			index++;
		}

		return candidate;
	}

	private static string SanitizeFileName(string value)
	{
		var sanitized = string.IsNullOrWhiteSpace(value) ? "ImportedMod" : value.Trim();
		foreach (var ch in Path.GetInvalidFileNameChars())
		{
			sanitized = sanitized.Replace(ch, '_');
		}

		return string.IsNullOrWhiteSpace(sanitized) ? "ImportedMod" : sanitized;
	}

	private static void SetReadOnlyRecursive(string directory, bool readOnly = true)
	{
		foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			var attributes = File.GetAttributes(path);
			File.SetAttributes(path, readOnly ? attributes | FileAttributes.ReadOnly : attributes & ~FileAttributes.ReadOnly);
		}
	}
}
