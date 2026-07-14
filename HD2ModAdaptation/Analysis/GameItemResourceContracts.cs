using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Defines neutral Item-to-resource relationship facts without coupling them to Core or Manager.
public sealed record GameItemInput(
	string ItemName,
	string Category,
	IReadOnlyList<string> ArchiveNames,
	IReadOnlyList<AssetKey>? DirectAssets = null,
	IReadOnlyList<AssetKey>? CandidateUnitAssets = null);

public sealed record ResourceDependencyFact(
	AssetKey AssetKey,
	string ResourceKind,
	string? SourceArchiveName,
	bool IsDirect,
	bool IsResolved);

public sealed record GameItemResourceInfo(
	string ItemName,
	string Category,
	IReadOnlyList<string> ArchiveNames,
	IReadOnlyList<ResourceDependencyFact> Resources,
	IReadOnlyList<AssetKey> CandidateUnitAssets,
	IReadOnlyList<PatchAnalysisIssue> Issues)
{
	public IReadOnlyList<ResourceDependencyFact> DirectAssets => Resources.Where(resource => resource.IsDirect).ToArray();
	public IReadOnlyList<ResourceDependencyFact> DependencyAssets => Resources.Where(resource => !resource.IsDirect).ToArray();
}

public interface IGameItemResourceRelationBuilder
{
	ValueTask<IReadOnlyList<GameItemResourceInfo>> BuildAsync(
		GameDataArchiveIndex archiveIndex,
		IReadOnlyList<GameItemInput> items,
		CancellationToken cancellationToken = default);
}
