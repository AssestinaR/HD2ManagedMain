using System.Text.Json;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Caches Adaptation patch facts while leaving Core projections in the existing analyzer.
public sealed class CachedPatchGroupAnalysisProvider : IPatchGroupAnalysisProvider
{
	private const int CacheVersion = 4;
	private const string AnalyzerVersion = "patch-group-v4-section-materials";
	private readonly IPatchGroupAnalysisProvider _inner;
	private readonly IPatchGroupAnalysisCacheStore _cacheStore;
	private readonly IPatchFileNameParser _fileNameParser;

	public CachedPatchGroupAnalysisProvider(
		IPatchGroupAnalysisProvider inner,
		IPatchGroupAnalysisCacheStore cacheStore,
		IPatchFileNameParser fileNameParser)
	{
		_inner = inner ?? throw new ArgumentNullException(nameof(inner));
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeNodeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var fingerprints = BuildSourceFingerprints(node, modsRootDirectory);
		var cached = await _cacheStore.TryLoadAsync(node.Id, cancellationToken).ConfigureAwait(false);
		if (cached is not null && cached.Version == CacheVersion &&
			string.Equals(cached.RelativePath, node.RelativePath, StringComparison.OrdinalIgnoreCase) &&
			cached.Analyses.All(analysis => string.Equals(analysis.AnalyzerVersion, AnalyzerVersion, StringComparison.Ordinal)) &&
			JsonSerializer.Serialize(cached.SourceFiles) == JsonSerializer.Serialize(fingerprints))
		{
			return cached.Analyses;
		}

		var analyses = await _inner.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		await _cacheStore.SaveAsync(new PatchGroupAnalysisCacheEntry(CacheVersion, node.Id, node.RelativePath, fingerprints, DateTimeOffset.UtcNow, analyses), cancellationToken).ConfigureAwait(false);
		return analyses;
	}

	private IReadOnlyList<PatchAssetSourceFileFingerprint> BuildSourceFingerprints(ModNode node, string modsRootDirectory)
	{
		var directory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(directory)) return Array.Empty<PatchAssetSourceFileFingerprint>();
		return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
			.Where(path => _fileNameParser.TryParse(Path.GetFileName(path), out var info) && info is not null && info.SidecarKind == PatchSidecarKind.Base)
			.SelectMany(path => EnumeratePatchGroupFiles(path))
			.Select(path =>
			{
				var file = new FileInfo(path);
				return new PatchAssetSourceFileFingerprint(Path.Combine(node.RelativePath, file.Name).Replace(Path.DirectorySeparatorChar, '/'), file.Length, file.LastWriteTimeUtc);
			})
			.OrderBy(x => x.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToList();
	}

	private static IEnumerable<string> EnumeratePatchGroupFiles(string basePath)
	{
		yield return basePath;

		var streamPath = basePath + ".stream";
		if (File.Exists(streamPath)) yield return streamPath;

		var gpuResourcesPath = basePath + ".gpu_resources";
		if (File.Exists(gpuResourcesPath)) yield return gpuResourcesPath;
	}
}
