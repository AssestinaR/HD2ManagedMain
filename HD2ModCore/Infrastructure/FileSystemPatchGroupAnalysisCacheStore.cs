using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Stores Adaptation patch facts as per-node JSON cache data.
public sealed class FileSystemPatchGroupAnalysisCacheStore : IPatchGroupAnalysisCacheStore
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
	private readonly StoragePaths _paths;

	public FileSystemPatchGroupAnalysisCacheStore(StoragePaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

	public async ValueTask<PatchGroupAnalysisCacheEntry?> TryLoadAsync(ModNodeId nodeId, CancellationToken cancellationToken = default)
	{
		var path = GetPath(nodeId);
		if (!File.Exists(path)) return null;
		try
		{
			await using var stream = File.OpenRead(path);
			return await JsonSerializer.DeserializeAsync<PatchGroupAnalysisCacheEntry>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		catch (JsonException) { return null; }
		catch (IOException) { return null; }
	}

	public async ValueTask SaveAsync(PatchGroupAnalysisCacheEntry entry, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(entry);
		Directory.CreateDirectory(_paths.PatchGroupAnalysisCacheDirectory);
		var path = GetPath(entry.NodeId);
		var temporaryPath = path + ".tmp";
		await using (var stream = File.Create(temporaryPath))
		{
			await JsonSerializer.SerializeAsync(stream, entry, JsonOptions, cancellationToken).ConfigureAwait(false);
		}
		File.Move(temporaryPath, path, overwrite: true);
	}

	private string GetPath(ModNodeId nodeId) => Path.Combine(_paths.PatchGroupAnalysisCacheDirectory, nodeId.Value + ".json");
}
