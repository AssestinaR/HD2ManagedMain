using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：按需生产完整 Unit/材质结构分析，供高级工具通过信息中心消费。
// Purpose: Produces full Unit/material structure analysis for advanced tools through the information center.
public interface IAdvancedUnitAnalysisProducer
{
	ValueTask<AdvancedUnitAnalysisFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}

public sealed record AdvancedUnitAnalysisFacts(
	ModNodeId NodeId,
	string RelativePath,
	string Generation,
	DateTimeOffset BuiltUtc,
	IReadOnlyList<PatchGroupAnalysis> Analyses,
	IReadOnlyList<CoreIssue> Issues);
