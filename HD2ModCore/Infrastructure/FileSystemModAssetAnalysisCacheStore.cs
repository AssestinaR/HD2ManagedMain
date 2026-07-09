using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Stores mod asset analysis cache entries as JSON files under the app data directory.
public sealed class FileSystemModAssetAnalysisCacheStore : IModAssetAnalysisCacheStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	private readonly StoragePaths _paths;

	public FileSystemModAssetAnalysisCacheStore(StoragePaths paths)
	{
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async ValueTask<ModAssetAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		var path = GetPath(nodeId);
		if (!File.Exists(path))
		{
			return null;
		}

		try
		{
			await using var stream = File.OpenRead(path);
			return await JsonSerializer.DeserializeAsync<ModAssetAnalysisCacheEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			return null;
		}
	}

	public async ValueTask SaveAsync(ModAssetAnalysisCacheEntry entry, CancellationToken cancellationToken = default)
	{
		if (entry is null)
		{
			throw new ArgumentNullException(nameof(entry));
		}

		Directory.CreateDirectory(_paths.AssetAnalysisCacheDirectory);
		var path = GetPath(entry.NodeId);
		var tempPath = path + ".tmp";
		await using (var stream = File.Create(tempPath))
		{
			await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		File.Move(tempPath, path, overwrite: true);
	}

	private string GetPath(ModNodeId nodeId)
		=> Path.Combine(_paths.AssetAnalysisCacheDirectory, nodeId.Value.ToString("N") + ".json");
}