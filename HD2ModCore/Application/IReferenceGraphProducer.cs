using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：独立生产 Unit/Material/Texture 引用图，不参与基础部署。
// Purpose: Produces the independent Unit/Material/Texture reference graph outside deployment.
public interface IReferenceGraphProducer
{
	ValueTask<ReferenceGraphFacts> ProduceAsync(ModNode node, string modsRootDirectory, CancellationToken cancellationToken = default);
}

public sealed record ReferenceGraphFacts(
	ModNodeId NodeId,
	string RelativePath,
	string Generation,
	DateTimeOffset BuiltUtc,
	IReadOnlyList<PatchGroupAnalysis> Analyses,
	IReadOnlyList<CoreIssue> Issues);
