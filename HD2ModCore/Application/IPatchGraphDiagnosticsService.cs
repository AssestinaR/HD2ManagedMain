using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：执行详情页一次性引用链诊断，不读取或写入派生缓存。
// Purpose: Runs one-shot detail-page reference diagnostics without derived-cache access.
public interface IPatchGraphDiagnosticsService
{
	ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeDependencyGraphAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<PatchGroupAnalysis>> AnalyzeFullPatchGraphAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}
