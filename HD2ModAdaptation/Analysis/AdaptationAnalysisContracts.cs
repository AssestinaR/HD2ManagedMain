namespace HD2ModAdaptation.Analysis;

// Purpose: Defines the neutral cross-analysis snapshot consumed later by Core and Manager adapters.
public sealed record AdaptationAnalysisInput(
	GameDataArchiveIndex? GameDataIndex,
	IReadOnlyList<PatchGroupInput> PatchGroups,
	IReadOnlyList<GameItemInput> Items,
	string SourceFingerprint);

public sealed record AdaptationAnalysisSnapshot(
	AdaptationAnalysisInput Input,
	IReadOnlyList<PatchGroupAnalysis> PatchGroups,
	IReadOnlyList<GameItemResourceInfo> Items,
	IReadOnlyList<ResourceReuseGroup> ReuseGroups,
	IReadOnlyList<ReplacementCoverageFinding> ReplacementFindings,
	IReadOnlyList<PatchAnalysisIssue> Issues,
	DateTimeOffset BuiltUtc,
	string SchemaVersion,
	string AnalyzerVersion);

public interface IAdaptationAnalysisBuilder
{
	ValueTask<AdaptationAnalysisSnapshot> BuildAsync(
		AdaptationAnalysisInput input,
		CancellationToken cancellationToken = default);
}