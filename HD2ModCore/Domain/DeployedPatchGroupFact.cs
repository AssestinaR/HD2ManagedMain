namespace HD2ModCore.Domain;

// Purpose: Actual deployed target patch group reconstructed from Data and reconciled with activation state.
public sealed record DeployedPatchGroupFact(
	string ArchiveHex16,
	int TargetPatchIndex,
	ModPatchGroupId? SourcePatchGroupId,
	ModNodeId? NodeId,
	IReadOnlyList<DeployedPatchFileFact> Files,
	IReadOnlySet<AssetKey> AssetKeys,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool IsValid => Files.Any(file => file.SidecarKind == PatchSidecarKind.Base)
		&& Issues.All(issue => issue.Severity != CoreIssueSeverity.Error);
}
