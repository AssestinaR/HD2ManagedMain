using HD2ModCore.Application;
using HD2ModCore.Domain;
using System.Runtime.InteropServices;

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
	private readonly IModFactsStore? _modFactsStore;
	private readonly PatchFileNormalizer _normalizer;
	private readonly SevenZipArchiveExtractor _archiveExtractor;
	private readonly ImportTemporaryDirectoryManager _temporaryDirectories;

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
		_modFactsStore = modFactsStore;
		_normalizer = new PatchFileNormalizer(new PatchFileNameParser());
		_archiveExtractor = new SevenZipArchiveExtractor();
		_temporaryDirectories = new ImportTemporaryDirectoryManager(_paths);
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
		var (storedTree, snapshot) = await CommitImportAsync(tree, full, sourceName, preferHardLinks: false, cancellationToken).ConfigureAwait(false);
		SetStoredTreeReadOnly(storedTree);

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
		var extractRoot = _temporaryDirectories.Create();

		ImportedObjectTree storedTree;
		LibrarySnapshot snapshot;
		try
		{
			await _archiveExtractor.ExtractAsync(full, extractRoot, cancellationToken).ConfigureAwait(false);
			var tree = await _folderImporter.ImportFolderAsync(extractRoot, cancellationToken).ConfigureAwait(false);
			(storedTree, snapshot) = await CommitImportAsync(tree, extractRoot, sourceName, preferHardLinks: true, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			try { _temporaryDirectories.Delete(extractRoot); } catch { }
		}
		SetStoredTreeReadOnly(storedTree);

		return new ImportResult(snapshot, storedTree.RootId, sourceName);
	}

	private async ValueTask<(ImportedObjectTree Tree, LibrarySnapshot Snapshot)> CommitImportAsync(ImportedObjectTree tree, string sourceRoot, string sourceName, bool preferHardLinks, CancellationToken cancellationToken)
	{
		ImportedObjectTree? storedTree = null;
		try
		{
			storedTree = PersistFlattenedTree(tree, sourceRoot, sourceName, preferHardLinks, cancellationToken);
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

	private ImportedObjectTree PersistFlattenedTree(ImportedObjectTree imported, string sourceRoot, string sourceDisplayName, bool preferHardLinks, CancellationToken cancellationToken)
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
			CopyTopLevelFiles(sourceNodeDir, destDir, preferHardLinks, cancellationToken);
			NormalizePatchDirectories(destDir, cancellationToken);

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

	private void SetStoredTreeReadOnly(ImportedObjectTree storedTree)
	{
		foreach (var node in storedTree.Nodes.Values)
		{
			SetReadOnlyRecursive(Path.Combine(_paths.ModsDirectory, node.RelativePath));
		}
	}

	private static void CopyTopLevelFiles(string sourceDir, string destDir, bool preferHardLinks, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(destDir);
		foreach (var file in Directory.EnumerateFiles(sourceDir))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var destination = Path.Combine(destDir, Path.GetFileName(file));
			if (preferHardLinks)
			{
				try
				{
					if (!CreateHardLink(destination, file, IntPtr.Zero)) throw new IOException("Unable to create hard link.");
					continue;
				}
				catch (IOException) { }
				catch (UnauthorizedAccessException) { }
			}
			File.Copy(file, destination, overwrite: true);
		}
	}

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

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
