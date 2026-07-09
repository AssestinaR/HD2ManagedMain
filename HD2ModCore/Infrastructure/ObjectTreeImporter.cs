using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：从文件夹递归扫描 patch 文件，并将每个真实含 patch 的目录拆成一个扁平 mod 节点。
// Purpose: Recursively scans patch files from a folder and splits each real patch-containing directory into a flat mod node.
public sealed class ObjectTreeImporter : IObjectTreeImporter
{
	private readonly IPatchFileNameParser _fileNameParser;

	public ObjectTreeImporter(IPatchFileNameParser fileNameParser)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
	}

	public ValueTask<ImportedObjectTree> ImportFolderAsync(string folderPath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderPath));
		}

		var rootDir = new DirectoryInfo(folderPath);
		if (!rootDir.Exists)
		{
			throw new DirectoryNotFoundException(folderPath);
		}

		var nodes = new Dictionary<ModNodeId, ModNode>();
		foreach (var dir in EnumerateDirectoriesDepthFirst(rootDir))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var patchGroups = CollectPatchGroups(dir);
			if (patchGroups.Count == 0)
			{
				continue;
			}

			var rel = Path.GetRelativePath(rootDir.FullName, dir.FullName);
			if (string.IsNullOrEmpty(rel) || rel == ".")
			{
				rel = string.Empty;
			}

			var id = ModNodeId.New();
			var metadata = new ModNodeMetadata(
				Name: BuildFlatName(rootDir.Name, rel),
				Notes: null,
				UserTags: Array.Empty<string>(),
				CreatedUtc: DateTimeOffset.UtcNow,
				ModifiedUtc: null);

			nodes[id] = new ModNode(
				Id: id,
				RelativePath: rel,
				Metadata: metadata,
				PatchGroups: patchGroups,
				Children: Array.Empty<ModNodeId>());
		}

		var rootId = nodes.Count > 0 ? nodes.Keys.First() : ModNodeId.New();

		var tree = new ImportedObjectTree(
			RootId: rootId,
			Nodes: nodes,
			SourceDisplayName: rootDir.Name);

		return ValueTask.FromResult(tree);
	}

	private List<PatchGroupKey> CollectPatchGroups(DirectoryInfo dir)
	{
		var patchGroups = new HashSet<PatchGroupKey>();
		foreach (var file in SafeEnumerateFiles(dir))
		{
			if (!_fileNameParser.TryParse(file.Name, out var info) || info is null)
			{
				continue;
			}

			patchGroups.Add(new PatchGroupKey(info.ArchiveHex16, info.PatchIndex));
		}
		return patchGroups.OrderBy(g => g.ArchiveHex16, StringComparer.OrdinalIgnoreCase).ThenBy(g => g.PatchIndex).ToList();
	}

	private static IEnumerable<DirectoryInfo> EnumerateDirectoriesDepthFirst(DirectoryInfo root)
	{
		yield return root;
		foreach (var child in SafeEnumerateDirectories(root))
		{
			foreach (var nested in EnumerateDirectoriesDepthFirst(child))
			{
				yield return nested;
			}
		}
	}

	private static string BuildFlatName(string sourceName, string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath))
		{
			return sourceName;
		}

		var parts = relativePath.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
		return string.Join('-', new[] { sourceName }.Concat(parts));
	}

	private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo dir)
	{
		try
		{
			return dir.EnumerateFiles();
		}
		catch
		{
			return Array.Empty<FileInfo>();
		}
	}

	private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo dir)
	{
		try
		{
			return dir.EnumerateDirectories();
		}
		catch
		{
			return Array.Empty<DirectoryInfo>();
		}
	}
}
