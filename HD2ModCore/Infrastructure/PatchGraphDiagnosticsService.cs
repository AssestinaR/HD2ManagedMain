using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：为详情页提供明确的一次性轻量/完整引用链诊断。
// Purpose: Provides explicit one-shot lightweight/full reference diagnostics for the detail page.
public sealed class PatchGraphDiagnosticsService : IPatchGraphDiagnosticsService
{
	private readonly IPatchGroupAnalysisProvider _dependencyGraphProvider;
	private readonly IPatchGroupAnalysisProvider _fullPatchProvider;

	public PatchGraphDiagnosticsService(IPatchGroupAnalysisProvider dependencyGraphProvider, IPatchGroupAnalysisProvider fullPatchProvider)
	{
		_dependencyGraphProvider = dependencyGraphProvider ?? throw new ArgumentNullException(nameof(dependencyGraphProvider));
		_fullPatchProvider = fullPatchProvider ?? throw new ArgumentNullException(nameof(fullPatchProvider));
	}

	// 作用：详情诊断也复用统一读取器及其 Patch index/payload 生命周期。
	// Purpose: Detail diagnostics also reuse the unified reader and its Patch index/payload lifetimes.
	public PatchGraphDiagnosticsService(IModInformationReader informationReader)
	{
		ArgumentNullException.ThrowIfNull(informationReader);
		var provider = new ModInformationPatchGroupAnalysisProvider(informationReader);
		_dependencyGraphProvider = provider.ForDepth(PatchAnalysisDepth.DependencyGraph);
		_fullPatchProvider = provider.ForDepth(PatchAnalysisDepth.Full);
	}

	public ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeDependencyGraphAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
		=> _dependencyGraphProvider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken);

	public ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeFullPatchGraphAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default)
		=> _fullPatchProvider.AnalyzeNodeAsync(node, modsRootDirectory, cancellationToken);
}
