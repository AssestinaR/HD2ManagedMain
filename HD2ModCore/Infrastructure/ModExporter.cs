using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Domain.Manifest;

namespace HD2ModCore.Infrastructure;

// 作用：将库中指定根对象导出为 zip，并内置 manifest.json（仅保存自定义标签/备注等，不保存资产标签）。
// Purpose: Exports a specified root object from the library as a zip with embedded manifest.json (stores user tags/notes only, not derived asset tags).
public sealed class ModExporter : IModExporter
{
	private const int ManifestVersion = 1;
	private static readonly JsonSerializerOptions ManifestSerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
        AllowTrailingCommas = false,
		PropertyNameCaseInsensitive = true,
		Converters =
		{
			new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
		},
	};

	private readonly StoragePaths _paths;

	public ModExporter(StoragePaths paths)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async ValueTask<string> ExportToZipAsync(ModNodeId rootNodeId, LibrarySnapshot snapshot, string destinationDirectory, CancellationToken cancellationToken = default)
	{
		if (snapshot is null)
		{
			throw new ArgumentNullException(nameof(snapshot));
		}
		if (string.IsNullOrWhiteSpace(destinationDirectory))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(destinationDirectory));
		}

		if (!snapshot.Nodes.TryGetValue(rootNodeId, out var root))
		{
			throw new KeyNotFoundException($"Root node not found: {rootNodeId}");
		}

		Directory.CreateDirectory(destinationDirectory);

		var zipName = SanitizeFileName(root.Metadata.Name);
		if (string.IsNullOrWhiteSpace(zipName))
		{
			zipName = "export";
		}

		var zipPath = Path.Combine(destinationDirectory, zipName + ".zip");
		if (File.Exists(zipPath))
		{
			File.Delete(zipPath);
		}

		var nodesInExport = CollectSubtree(snapshot, rootNodeId);
		var manifest = CreateManifest(root, nodesInExport);
		var manifestJson = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);

		using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
		{
			// manifest at root
			var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
			await using (var s = manifestEntry.Open())
			await using (var sw = new StreamWriter(s))
			{
				await sw.WriteAsync(manifestJson.AsMemory(), cancellationToken).ConfigureAwait(false);
			}

			foreach (var node in nodesInExport)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var nodeDir = Path.Combine(_paths.ModsDirectory, node.RelativePath);
				if (!Directory.Exists(nodeDir))
				{
					continue;
				}

				foreach (var file in Directory.EnumerateFiles(nodeDir, "*", SearchOption.TopDirectoryOnly))
				{
					cancellationToken.ThrowIfCancellationRequested();
					var entryPath = string.IsNullOrEmpty(node.RelativePath)
						? Path.GetFileName(file)
						: Path.Combine(node.RelativePath, Path.GetFileName(file));
					entryPath = entryPath.Replace('\\', '/');

					zip.CreateEntryFromFile(file, entryPath, CompressionLevel.Optimal);
				}
			}
		}

		return zipPath;
	}

	private static IReadOnlyList<ModNode> CollectSubtree(LibrarySnapshot snapshot, ModNodeId rootId)
	{
		var result = new List<ModNode>();
		var stack = new Stack<ModNodeId>();
		stack.Push(rootId);

		while (stack.Count > 0)
		{
			var id = stack.Pop();
			if (!snapshot.Nodes.TryGetValue(id, out var node))
			{
				continue;
			}
			result.Add(node);

			foreach (var child in node.Children)
			{
				stack.Push(child);
			}
		}

		return result;
	}

	private static ExportManifest CreateManifest(ModNode root, IReadOnlyList<ModNode> nodes)
	{
		var items = nodes
			.Select(n => new ExportManifestNode(
				RelativePath: n.RelativePath.Replace('\\', '/'),
				Name: n.Metadata.Name,
				Notes: n.Metadata.Notes,
             Tags: n.Metadata.UserTags.ToList()))
			.OrderBy(n => n.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return new ExportManifest(
			Version: ManifestVersion,
			RootName: root.Metadata.Name,
			ExportedUtc: DateTimeOffset.UtcNow,
			Nodes: items);
	}

	private static string SanitizeFileName(string name)
	{
		foreach (var c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '_');
		}
		return name.Trim();
	}
}
