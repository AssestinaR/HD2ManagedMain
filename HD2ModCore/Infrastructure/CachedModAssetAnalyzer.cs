using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Reuses persisted mod asset analysis until patch files or asset metadata have changed.
public sealed class CachedModAssetAnalyzer : IModAssetAnalyzer
{
	private const int CacheVersion = 7;
	private readonly IModAssetAnalyzer _inner;
	private readonly IModAssetAnalysisCacheStore _cacheStore;
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly StoragePaths _paths;

	public CachedModAssetAnalyzer(
		IModAssetAnalyzer inner,
		IModAssetAnalysisCacheStore cacheStore,
		IPatchFileNameParser fileNameParser,
		StoragePaths paths)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_paths = paths ?? throw new ArgumentNullException(nameof(paths));
	}

	public async ValueTask<ModAssetSummary> AnalyzeNodeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		if (node is null)
		{
			throw new ArgumentNullException(nameof(node));
		}

		var sourceFiles = BuildSourceFingerprints(node, modsRootDirectory);
		var metadataFingerprint = await BuildMetadataFingerprintAsync(cancellationToken).ConfigureAwait(false);
		var cached = await _cacheStore.TryLoadAsync(node.Id, cancellationToken).ConfigureAwait(false);
		if (cached is not null && IsValid(cached, node, sourceFiles, metadataFingerprint))
		{
			return cached.Summary;
		}

		var summary = await _inner.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var entry = new ModAssetAnalysisCacheEntry(
			CacheVersion,
			node.Id,
			node.RelativePath,
			metadataFingerprint,
			DateTimeOffset.UtcNow,
			sourceFiles,
			summary);
		await _cacheStore.SaveAsync(entry, cancellationToken).ConfigureAwait(false);
		return summary;
	}

	private IReadOnlyList<PatchAssetSourceFileFingerprint> BuildSourceFingerprints(ModNode node, string modsRootDirectory)
	{
		var nodeDir = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(nodeDir))
		{
			return Array.Empty<PatchAssetSourceFileFingerprint>();
		}

		var result = new List<PatchAssetSourceFileFingerprint>();
		foreach (var path in Directory.EnumerateFiles(nodeDir, "*", SearchOption.TopDirectoryOnly))
		{
			var fileName = Path.GetFileName(path);
			if (!_fileNameParser.TryParse(fileName, out var info) || info is null)
			{
				continue;
			}

			var file = new FileInfo(path);
			result.Add(new PatchAssetSourceFileFingerprint(
				Path.Combine(node.RelativePath, file.Name).Replace(Path.DirectorySeparatorChar, '/'),
				file.Length,
				file.LastWriteTimeUtc));
		}

		return result
			.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private async ValueTask<string> BuildMetadataFingerprintAsync(CancellationToken cancellationToken)
	{
		var builder = new StringBuilder();
		if (File.Exists(_paths.AssetMetadataManifestPath))
		{
			builder.Append(Path.GetFileName(_paths.AssetMetadataManifestPath))
				.Append(':')
				.Append(await ComputeFileSha256Async(_paths.AssetMetadataManifestPath, cancellationToken).ConfigureAwait(false))
				.AppendLine();
		}
		else
		{
			foreach (var path in new[] { _paths.ArchiveHashesPath, _paths.FriendlyNamesPath, _paths.TypeHashesPath })
			{
				AppendFileStamp(builder, path);
			}
		}

		AppendFileStamp(builder, _paths.DbPath);
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
	}

	private static void AppendFileStamp(StringBuilder builder, string path)
	{
		if (!File.Exists(path))
		{
			return;
		}

		var file = new FileInfo(path);
		builder.Append(Path.GetFileName(path)).Append(':').Append(file.Length).Append(':').Append(file.LastWriteTimeUtc.Ticks).AppendLine();
	}

	private static async ValueTask<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
	{
		await using var stream = File.OpenRead(path);
		var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
		return Convert.ToHexString(hash).ToLowerInvariant();
	}

	private static bool IsValid(
		ModAssetAnalysisCacheEntry entry,
		ModNode node,
		IReadOnlyList<PatchAssetSourceFileFingerprint> sourceFiles,
		string metadataFingerprint)
	{
		return entry.Version == CacheVersion
			&& entry.NodeId == node.Id
			&& string.Equals(entry.RelativePath, node.RelativePath, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(entry.MetadataFingerprint, metadataFingerprint, StringComparison.OrdinalIgnoreCase)
			&& JsonSerializer.Serialize(entry.SourceFiles) == JsonSerializer.Serialize(sourceFiles);
	}
}