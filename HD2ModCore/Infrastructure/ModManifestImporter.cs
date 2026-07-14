using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using HD2ModCore.Application;
using HD2ModCore.Domain;
using HD2ModCore.Domain.Manifest;

namespace HD2ModCore.Infrastructure;

// 作用：导入包含 manifest.json 的导出 zip：解压到 mods/<guid>/，应用名称/备注，并写入库快照。
// Purpose: Imports an exported zip, applies manifest names and notes, and persists the library snapshot.
public sealed class ModManifestImporter : IModManifestImporter
{
	private readonly StoragePaths _paths;
	private readonly IObjectTreeImporter _folderImporter;
	private readonly IModLibraryStore _store;

	public ModManifestImporter(StoragePaths paths, IObjectTreeImporter folderImporter, IModLibraryStore store)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
		_folderImporter = folderImporter ?? throw new ArgumentNullException(nameof(folderImporter));
		_store = store ?? throw new ArgumentNullException(nameof(store));
	}

	public async ValueTask<ImportResult> ImportExportZipAsync(string zipFilePath, CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(zipFilePath))
		{
			throw new ArgumentException("Value cannot be null or whitespace.", nameof(zipFilePath));
		}

		var full = Path.GetFullPath(zipFilePath);
		if (!File.Exists(full))
		{
			throw new FileNotFoundException("Zip file not found.", full);
		}

		var importId = Guid.NewGuid().ToString("N");
		Directory.CreateDirectory(_paths.ModsDirectory);
		var destRoot = Path.Combine(_paths.ModsDirectory, importId);
		Directory.CreateDirectory(destRoot);

		ExportManifest? manifest = null;

		try
		{
			await Task.Run(() => ExtractZip(full, destRoot, cancellationToken), cancellationToken).ConfigureAwait(false);

			var manifestPath = Path.Combine(destRoot, "manifest.json");
			if (File.Exists(manifestPath))
			{
				manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
				try { File.Delete(manifestPath); } catch { }
			}

			var tree = await _folderImporter.ImportFolderAsync(destRoot, cancellationToken).ConfigureAwait(false);
			var snapshot = await MergeIntoSnapshotAsync(tree, destRoot, manifest, cancellationToken).ConfigureAwait(false);
			await _store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);

			var sourceName = Path.GetFileName(full);
			return new ImportResult(snapshot, tree.RootId, sourceName);
		}
		catch
		{
			try { Directory.Delete(destRoot, recursive: true); } catch { }
			throw;
		}
	}

	private static void ExtractZip(string zipPath, string destinationDirectory, CancellationToken cancellationToken)
	{
		using var zip = ZipFile.OpenRead(zipPath);
		foreach (var entry in zip.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (string.IsNullOrEmpty(entry.Name))
			{
				continue;
			}

			var fullDest = Path.Combine(destinationDirectory, entry.FullName);
			var fullDir = Path.GetDirectoryName(fullDest);
			if (!string.IsNullOrWhiteSpace(fullDir))
			{
				Directory.CreateDirectory(fullDir);
			}
			entry.ExtractToFile(fullDest, overwrite: true);
		}
	}

 private static async ValueTask<ExportManifest?> ReadManifestAsync(string manifestPath, CancellationToken cancellationToken)
	{
		try
		{
			var json = await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false);
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				AllowTrailingCommas = true,
				ReadCommentHandling = JsonCommentHandling.Skip,
				PropertyNameCaseInsensitive = true,
			};

			// Try typed deserialize first.
			var typed = JsonSerializer.Deserialize<ExportManifest>(json, options);
			if (typed is not null)
			{
				// Normalize null lists.
				return typed with
				{
					Nodes = typed.Nodes ?? Array.Empty<ExportManifestNode>(),
				};
			}

			// Fallback: tolerate weird manifests by using JsonNode.
			var root = JsonNode.Parse(json, new JsonNodeOptions { PropertyNameCaseInsensitive = true }, new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip,
			});
			if (root is null)
			{
				return null;
			}

			var rootName = root["rootName"]?.GetValue<string>() ?? string.Empty;
			var nodes = new List<ExportManifestNode>();
			if (root["nodes"] is JsonArray arr)
			{
				foreach (var item in arr)
				{
					var rel = item?["relativePath"]?.GetValue<string>() ?? string.Empty;
					var name = item?["name"]?.GetValue<string>() ?? string.Empty;
					var notes = item?["notes"]?.GetValue<string>();
					nodes.Add(new ExportManifestNode(rel, name, notes));
				}
			}

			return new ExportManifest(Version: 1, RootName: rootName, ExportedUtc: DateTimeOffset.UtcNow, Nodes: nodes);
		}
		catch
		{
			return null;
		}
	}

	private async ValueTask<LibrarySnapshot> MergeIntoSnapshotAsync(ImportedObjectTree imported, string storedModsRoot, ExportManifest? manifest, CancellationToken cancellationToken)
	{
		var current = await _store.TryLoadAsync(cancellationToken).ConfigureAwait(false);
		var nodes = current?.Nodes is not null
			? new Dictionary<ModNodeId, ModNode>(current.Nodes)
			: new Dictionary<ModNodeId, ModNode>();

		var manifestMap = manifest?.Nodes?.ToDictionary(n => NormalizePath(n.RelativePath), StringComparer.OrdinalIgnoreCase)
			?? new Dictionary<string, ExportManifestNode>(StringComparer.OrdinalIgnoreCase);

		var exportRootFolder = TryGetSingleRootFolderName(storedModsRoot);

		foreach (var kvp in imported.Nodes)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var node = kvp.Value;

			var newRel = Path.Combine(Path.GetFileName(storedModsRoot), node.RelativePath);
          var relForManifest = NormalizePath(node.RelativePath);
			var manifestNode = default(ExportManifestNode);
			if (!manifestMap.TryGetValue(relForManifest, out manifestNode) && !string.IsNullOrWhiteSpace(exportRootFolder))
			{
				var withRoot = NormalizePath(Path.Combine(exportRootFolder!, node.RelativePath));
				manifestMap.TryGetValue(withRoot, out manifestNode);
			}

			if (manifestNode is not null)
			{
				var md = node.Metadata with
				{
					Name = string.IsNullOrWhiteSpace(manifestNode.Name) ? node.Metadata.Name : manifestNode.Name,
					Notes = manifestNode.Notes,
				};
				nodes[kvp.Key] = node with { RelativePath = newRel, Metadata = md };
			}
			else
			{
				nodes[kvp.Key] = node with { RelativePath = newRel };
			}
		}

		var profiles = current?.Profiles?.ToList() ?? new List<Profile>();

		return new LibrarySnapshot(
			Version: current?.Version ?? 1,
			SavedUtc: DateTimeOffset.UtcNow,
			Nodes: nodes,
			Profiles: profiles);
	}

	private static string NormalizePath(string path)
		=> (path ?? string.Empty).Replace('\\', '/').Trim('/');

	private static string? TryGetSingleRootFolderName(string extractedRoot)
	{
		try
		{
			var dirs = Directory.EnumerateDirectories(extractedRoot).Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
			var files = Directory.EnumerateFiles(extractedRoot).ToList();
			if (files.Count == 0 && dirs.Count == 1)
			{
				return dirs[0];
			}
		}
		catch
		{
			// ignore
		}
		return null;
	}
}
