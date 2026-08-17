namespace HD2ModCore.Domain;

// Purpose: Holds a consistent derived view of the library built from the snapshot and current file system state.
public sealed record DerivedLibraryData(
	DateTimeOffset BuiltUtc,
	IReadOnlyDictionary<ModNodeId, DerivedModNodeData> Nodes,
	IReadOnlyList<CoreIssue> Issues,
	string? AssetSummaryGeneration = null)
{
	public DerivedModNodeData? Find(ModNodeId nodeId)
		=> Nodes.TryGetValue(nodeId, out var data) ? data : null;
}
