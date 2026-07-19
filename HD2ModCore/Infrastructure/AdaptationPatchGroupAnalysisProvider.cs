using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：按请求层级生成 Patch 资产清单或完整引用事实。
// Purpose: Discovers patch groups for a mod and delegates patch fact reading to Adaptation.
public sealed class AdaptationPatchGroupAnalysisProvider : IPatchGroupAnalysisProvider
{
	private readonly IPatchFileNameParser _fileNameParser;
	private readonly IPatchGroupAnalyzer _analyzer;
	private readonly PatchAnalysisDepth _depth;

	public AdaptationPatchGroupAnalysisProvider(
		IPatchFileNameParser fileNameParser,
		IPatchGroupAnalyzer analyzer,
		PatchAnalysisDepth depth = PatchAnalysisDepth.Inventory)
	{
		_fileNameParser = fileNameParser ?? throw new ArgumentNullException(nameof(fileNameParser));
		_analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
		_depth = depth;
	}

	public async ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeNodeAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(node);
		ArgumentException.ThrowIfNullOrWhiteSpace(modsRootDirectory);

		var nodeDirectory = Path.Combine(modsRootDirectory, node.RelativePath);
		if (!Directory.Exists(nodeDirectory))
		{
			return Array.Empty<PatchGroupAnalysis>();
		}

		var results = new List<PatchGroupAnalysis>();
		foreach (var path in Directory.EnumerateFiles(nodeDirectory, "*", SearchOption.TopDirectoryOnly))
		{
			cancellationToken.ThrowIfCancellationRequested();
			var fileName = Path.GetFileName(path);
			if (!_fileNameParser.TryParse(fileName, out var parsed) || parsed is null || parsed.SidecarKind != PatchSidecarKind.Base)
			{
				continue;
			}

			var input = new PatchGroupInput(
					path,
					File.Exists(path + ".stream") ? path + ".stream" : null,
					File.Exists(path + ".gpu_resources") ? path + ".gpu_resources" : null);
			results.Add(_depth switch
			{
				PatchAnalysisDepth.Inventory when _analyzer is IInventoryPatchGroupAnalyzer inventoryAnalyzer => await inventoryAnalyzer.AnalyzeInventoryAsync(input, cancellationToken).ConfigureAwait(false),
				PatchAnalysisDepth.DependencyGraph when _analyzer is IDependencyGraphPatchGroupAnalyzer dependencyAnalyzer => await dependencyAnalyzer.AnalyzeDependencyGraphAsync(input, cancellationToken).ConfigureAwait(false),
				_ => await _analyzer.AnalyzeAsync(input, cancellationToken).ConfigureAwait(false)
			});
		}

		return results;
	}
}
