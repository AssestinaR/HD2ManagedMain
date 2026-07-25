using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// 作用：承载补丁组分析结果及其源文件指纹，供信息中心消费者之间传递分析快照。
// Purpose: Carries patch-group analysis results and source fingerprints between information-center consumers.
public sealed record PatchGroupAnalysisCacheEntry(
	int Version,
	ModNodeId NodeId,
	string RelativePath,
	IReadOnlyList<PatchAssetSourceFileFingerprint> SourceFileFingerprints,
	DateTimeOffset BuiltAtUtc,
	IReadOnlyList<PatchGroupAnalysis> Analyses);
