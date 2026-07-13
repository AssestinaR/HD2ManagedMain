using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Stores the patch-group fingerprint manifest as an atomic JSON file.
public sealed class FileSystemPatchFileGroupFingerprintStore : IPatchFileGroupFingerprintStore
{
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
	private readonly StoragePaths _paths;

	public FileSystemPatchFileGroupFingerprintStore(StoragePaths paths) => _paths = paths ?? throw new ArgumentNullException(nameof(paths));

	public async ValueTask<PatchFileGroupFingerprintManifest?> TryLoadAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(_paths.PatchFileGroupFingerprintManifestPath)) return null;
		try
		{
			await using var stream = File.OpenRead(_paths.PatchFileGroupFingerprintManifestPath);
			return await JsonSerializer.DeserializeAsync<PatchFileGroupFingerprintManifest>(stream, Options, cancellationToken).ConfigureAwait(false);
		}
		catch { return null; }
	}

	public async ValueTask SaveAsync(PatchFileGroupFingerprintManifest manifest, CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(_paths.DataDirectory);
		var path = _paths.PatchFileGroupFingerprintManifestPath;
		var tempPath = path + ".tmp";
		await using (var stream = File.Create(tempPath)) await JsonSerializer.SerializeAsync(stream, manifest, Options, cancellationToken).ConfigureAwait(false);
		File.Move(tempPath, path, overwrite: true);
	}
}