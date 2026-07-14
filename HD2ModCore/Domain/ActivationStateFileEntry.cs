namespace HD2ModCore.Domain;

// Purpose: Public schema for one deployed source/target file recorded in activation-state.json.
public sealed record ActivationStateFileEntry(
	string TargetPath,
	string SourcePath,
	DeploymentMethod Method,
	string ArchiveHex16,
	int SourcePatchIndex,
	int TargetPatchIndex,
	PatchSidecarKind SidecarKind,
	ModNodeId? NodeId,
	long Length,
	string ContentSha256);
