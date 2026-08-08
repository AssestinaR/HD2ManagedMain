using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2ModAdaptation.PatchReconstruction.PatchWorkspace;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Builds and persists complete Canonical hidden Units for reuse by tool workflows.
public sealed record CanonicalHiddenUnitOutput(
	CanonicalPatchSessionEntry Entry,
	int HiddenMeshCount);

public sealed class CanonicalHiddenUnitBuilder
{
	private readonly SameKeyCanonicalUnitRebuilder rebuilder = new();

	public CanonicalHiddenUnitOutput Build(GameDataUnitMesh target, UnitTransformInfo avatarTransforms)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(avatarTransforms);
		var source = new PatchUnitMesh(target.Payload.Entry, target.Payload, target.Model, target.CompositePayload);
		var result = rebuilder.Rebuild(new SameKeyCanonicalUnitRebuildRequest(source, target, [])
		{
			AvatarTransformInfo = avatarTransforms
		});
		if (!result.IsValid || result.Job is null || result.Job.Outputs.Count != 1)
			throw new InvalidDataException($"Canonical full-hide build failed for Unit 0x{target.AssetKey.FileId:x16}: {string.Join(" | ", result.Diagnostics.Select(item => item.Message))}");
		return new(result.Job.Outputs[0], result.HiddenMeshCount);
	}
}

public interface ICanonicalHiddenUnitOutputCache
{
	ValueTask InitializeAsync(string gameDataFingerprint, bool gameDataIndexIsCurrent, CancellationToken cancellationToken = default);
	ValueTask<CanonicalHiddenUnitOutput?> TryReadAsync(string archiveName, AssetKey unitKey, CancellationToken cancellationToken = default);
	ValueTask StoreAsync(string archiveName, CanonicalHiddenUnitOutput output, CancellationToken cancellationToken = default);
}

public sealed class CanonicalHiddenUnitOutputCache : ICanonicalHiddenUnitOutputCache
{
	private const string CacheVersion = "canonical-hidden-unit-v1";
	private readonly string rootDirectory;
	private string? activeDirectory;

	public CanonicalHiddenUnitOutputCache(string? rootDirectory = null)
	{
		this.rootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(AppContext.BaseDirectory, "data", "hidden-unit-cache"));
	}

	public async ValueTask InitializeAsync(string gameDataFingerprint, bool gameDataIndexIsCurrent, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(gameDataFingerprint);
		if (!gameDataIndexIsCurrent)
		{
			activeDirectory = null;
			TryDeleteDirectory(rootDirectory);
			return;
		}

		Directory.CreateDirectory(rootDirectory);
		var manifestPath = Path.Combine(rootDirectory, "manifest.json");
		var manifestExists = File.Exists(manifestPath);
		var manifest = await ReadManifestAsync(manifestPath, cancellationToken).ConfigureAwait(false);
		if (manifestExists && (manifest is null || !string.Equals(manifest.CacheVersion, CacheVersion, StringComparison.Ordinal) || !string.Equals(manifest.GameDataFingerprint, gameDataFingerprint, StringComparison.Ordinal)))
		{
			TryDeleteDirectory(rootDirectory);
			Directory.CreateDirectory(rootDirectory);
		}
		activeDirectory = rootDirectory;
		await WriteAtomicallyAsync(manifestPath, JsonSerializer.SerializeToUtf8Bytes(new CacheManifest(CacheVersion, gameDataFingerprint)), cancellationToken).ConfigureAwait(false);
	}

	public async ValueTask<CanonicalHiddenUnitOutput?> TryReadAsync(string archiveName, AssetKey unitKey, CancellationToken cancellationToken = default)
	{
		if (activeDirectory is null) return null;
		var paths = GetPaths(archiveName, unitKey);
		try
		{
			if (!File.Exists(paths.Metadata) || !File.Exists(paths.Toc) || !File.Exists(paths.Gpu) || !File.Exists(paths.Stream)) return null;
			var metadata = JsonSerializer.Deserialize<CacheEntryMetadata>(await File.ReadAllBytesAsync(paths.Metadata, cancellationToken).ConfigureAwait(false));
			if (metadata is null || metadata.TypeId != unitKey.TypeId || metadata.FileId != unitKey.FileId) throw new InvalidDataException("Hidden Unit cache metadata is invalid.");
			var entry = new CanonicalPatchSessionEntry(unitKey, CanonicalPatchEntryOwnership.TargetOutput,
				await File.ReadAllBytesAsync(paths.Toc, cancellationToken).ConfigureAwait(false),
				await File.ReadAllBytesAsync(paths.Gpu, cancellationToken).ConfigureAwait(false),
				await File.ReadAllBytesAsync(paths.Stream, cancellationToken).ConfigureAwait(false),
				metadata.Unknown1, metadata.Unknown2, metadata.Unknown3, metadata.Unknown4);
			return new(entry, metadata.HiddenMeshCount);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
		{
			TryDelete(paths);
			return null;
		}
	}

	public async ValueTask StoreAsync(string archiveName, CanonicalHiddenUnitOutput output, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(output);
		if (activeDirectory is null) return;
		var paths = GetPaths(archiveName, output.Entry.Key);
		Directory.CreateDirectory(Path.GetDirectoryName(paths.Metadata)!);
		await WriteAtomicallyAsync(paths.Toc, output.Entry.EffectiveTocData, cancellationToken).ConfigureAwait(false);
		await WriteAtomicallyAsync(paths.Gpu, output.Entry.EffectiveGpuData, cancellationToken).ConfigureAwait(false);
		await WriteAtomicallyAsync(paths.Stream, output.Entry.EffectiveStreamData, cancellationToken).ConfigureAwait(false);
		var metadata = new CacheEntryMetadata(output.Entry.Key.TypeId, output.Entry.Key.FileId, output.HiddenMeshCount, output.Entry.Unknown1, output.Entry.Unknown2, output.Entry.Unknown3, output.Entry.Unknown4);
		await WriteAtomicallyAsync(paths.Metadata, JsonSerializer.SerializeToUtf8Bytes(metadata), cancellationToken).ConfigureAwait(false);
	}

	private CachePaths GetPaths(string archiveName, AssetKey key)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(archiveName);
		var archiveHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(archiveName))).ToLowerInvariant()[..16];
		var prefix = $"{archiveHash}-{key.TypeId:x16}-{key.FileId:x16}";
		var directory = activeDirectory ?? rootDirectory;
		return new(Path.Combine(directory, prefix + ".json"), Path.Combine(directory, prefix + ".toc"), Path.Combine(directory, prefix + ".gpu"), Path.Combine(directory, prefix + ".stream"));
	}

	private static async ValueTask<CacheManifest?> ReadManifestAsync(string path, CancellationToken cancellationToken)
	{
		try { return File.Exists(path) ? JsonSerializer.Deserialize<CacheManifest>(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false)) : null; }
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException) { return null; }
	}

	private static async ValueTask WriteAtomicallyAsync(string path, byte[] data, CancellationToken cancellationToken)
	{
		var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
		try
		{
			await File.WriteAllBytesAsync(temporaryPath, data, cancellationToken).ConfigureAwait(false);
			File.Move(temporaryPath, path, overwrite: true);
		}
		finally { TryDeleteFile(temporaryPath); }
	}

	private static void TryDelete(CachePaths paths)
	{
		TryDeleteFile(paths.Metadata); TryDeleteFile(paths.Toc); TryDeleteFile(paths.Gpu); TryDeleteFile(paths.Stream);
	}
	private static void TryDeleteFile(string path) { try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
	private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }

	private sealed record CacheManifest(string CacheVersion, string GameDataFingerprint);
	private sealed record CacheEntryMetadata(ulong TypeId, ulong FileId, int HiddenMeshCount, ulong Unknown1, ulong Unknown2, uint Unknown3, uint Unknown4);
	private sealed record CachePaths(string Metadata, string Toc, string Gpu, string Stream);
}
