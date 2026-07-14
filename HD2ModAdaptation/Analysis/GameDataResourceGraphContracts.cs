using HD2ModAdaptation.PatchReconstruction;

namespace HD2ModAdaptation.Analysis;

// Purpose: Represents verified resource nodes and dependency edges discovered from Game Data payloads.
public sealed record GameDataResourceNode(
	AssetKey AssetKey,
	string ResourceKind,
	string? ArchiveName,
	bool IsResolved);

public sealed record GameDataResourceEdge(
	AssetKey From,
	AssetKey To,
	string Relation,
	bool IsResolved);

public sealed record GameDataResourceGraph(
	IReadOnlyList<GameDataResourceNode> Nodes,
	IReadOnlyList<GameDataResourceEdge> Edges,
	IReadOnlyList<PatchAnalysisIssue> Issues);

public interface IGameDataResourceGraphBuilder
{
	ValueTask<GameDataResourceGraph> BuildAsync(
		GameDataArchiveIndex archiveIndex,
		IReadOnlyCollection<AssetKey> rootAssets,
		CancellationToken cancellationToken = default);
}
