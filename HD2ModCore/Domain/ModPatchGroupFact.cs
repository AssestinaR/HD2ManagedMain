namespace HD2ModCore.Domain;

// Purpose: Immutable content fact for one top-level source patch group and its AssetKeys.
public sealed record ModPatchGroupFact(
	ModPatchGroupId Id,
	int NormalizedOrder,
	IReadOnlyList<ModPatchGroupFileFact> Files,
	IReadOnlySet<AssetKey> AssetKeys,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool IsValid => Files.Any(file => file.SidecarKind == PatchSidecarKind.Base)
		&& Issues.All(issue => issue.Severity != CoreIssueSeverity.Error);
}
