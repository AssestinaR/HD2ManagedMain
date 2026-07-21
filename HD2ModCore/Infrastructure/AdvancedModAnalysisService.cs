using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：集中执行完整 Patch 结构分析，并以基础文件指纹校验 SQLite 高级缓存。
public sealed class AdvancedModAnalysisService : IAdvancedModAnalysisService
{
	private const int CacheVersion = 8;
	private const string AnalyzerVersion = "patch-group-v4-section-materials";
	private readonly IAdvancedModAnalysisCacheStore _cacheStore;
	private readonly IPatchGroupAnalysisProvider _fullAnalysisProvider;
	private readonly IPatchFileNameParser _fileNameParser;

	public AdvancedModAnalysisService(IAdvancedModAnalysisCacheStore cacheStore, IPatchGroupAnalysisProvider fullAnalysisProvider, IPatchFileNameParser fileNameParser)
	{
		_cacheStore = cacheStore ?? throw new ArgumentNullException(nameof(cacheStore));
		_fullAnalysisProvider = fullAnalysisProvider ?? throw new ArgumentNullException(nameof(fullAnalysisProvider));
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
	}

	public async ValueTask<AdvancedModAnalysisState> GetStateAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var cached = await _cacheStore.TryLoadAdvancedAsync(node.Id, cancellationToken).ConfigureAwait(false);
		var currentFiles = BuildFingerprints(node, modsRootDirectory);
		var isReady = cached is not null && cached.Version == CacheVersion && string.Equals(cached.RelativePath, node.RelativePath, StringComparison.OrdinalIgnoreCase)
			&& cached.Analyses.All(analysis => analysis.Depth == PatchAnalysisDepth.Full && string.Equals(analysis.AnalyzerVersion, AnalyzerVersion, StringComparison.Ordinal) && analysis.Entries.Count != 0)
			&& cached.SourceFiles.SequenceEqual(currentFiles);
		return new AdvancedModAnalysisState(node.Id, isReady, isReady, cached?.BuiltAtUtc, Array.Empty<CoreIssue>());
	}

	public async ValueTask<AdvancedModAnalysisState> AnalyzeAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var analyses = await _fullAnalysisProvider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		var fingerprints = BuildFingerprints(node, modsRootDirectory);
		await _cacheStore.SaveAdvancedAsync(new PatchGroupAnalysisCacheEntry(CacheVersion, node.Id, node.RelativePath, fingerprints, DateTimeOffset.UtcNow, analyses), cancellationToken).ConfigureAwait(false);
		var issues = analyses.SelectMany(analysis => analysis.Issues)
			.Select(issue => new CoreIssue(CoreIssueSeverity.Warning, issue.Code, issue.Message, issue.SourceFilePath, node.Id))
			.ToArray();
		return new AdvancedModAnalysisState(node.Id, true, true, DateTimeOffset.UtcNow, issues);
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> GetRequiredAnalysesAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
	{
		var state = await GetStateAsync(node, modsRootDirectory, cancellationToken).ConfigureAwait(false);
		if (!state.IsReady) throw new InvalidOperationException("请先执行高级分析以建立完整 Unit 和材质引用缓存。");
		return (await _cacheStore.TryLoadAdvancedAsync(node.Id, cancellationToken).ConfigureAwait(false))!.Analyses;
	}

	private IReadOnlyList<PatchAssetSourceFileFingerprint> BuildFingerprints(ModNode node, string modsRootDirectory)
	{
		var directory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(directory)) return Array.Empty<PatchAssetSourceFileFingerprint>();
		return Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
			.Where(path => _fileNameParser.TryParse(Path.GetFileName(path), out var info) && info is not null && info.SidecarKind == PatchSidecarKind.Base)
			.SelectMany(EnumerateGroupFiles)
			.Select(path => { var file = new FileInfo(path); return new PatchAssetSourceFileFingerprint(Path.Combine(node.RelativePath, file.Name).Replace(Path.DirectorySeparatorChar, '/'), file.Length, file.LastWriteTimeUtc); })
			.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static IEnumerable<string> EnumerateGroupFiles(string basePath)
	{
		yield return basePath;
		if (File.Exists(basePath + ".stream")) yield return basePath + ".stream";
		if (File.Exists(basePath + ".gpu_resources")) yield return basePath + ".gpu_resources";
	}

}