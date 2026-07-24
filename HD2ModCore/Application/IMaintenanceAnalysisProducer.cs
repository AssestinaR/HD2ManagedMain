using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：独立生产 Mod 维护/兼容性分析，不参与基础部署。
// Purpose: Produces optional Mod maintenance and compatibility analysis outside deployment.
public interface IMaintenanceAnalysisProducer
{
	ValueTask<MaintenanceAnalysisFacts> ProduceAsync(ModNode node, ModContentFacts assetInventory, CancellationToken cancellationToken = default);
}

public sealed record MaintenanceAnalysisFacts(
	ModNodeId NodeId,
	string RelativePath,
	string Generation,
	DateTimeOffset BuiltUtc,
	ModCompatibilityReport? Compatibility,
	IReadOnlyList<CoreIssue> Issues);
