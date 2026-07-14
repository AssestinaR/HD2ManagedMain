namespace HD2ModCore.Domain;

// Purpose: One actual file observed in a deployed target patch group, optionally traced to activation state.
public sealed record DeployedPatchFileFact(
	string TargetPath,
	PatchSidecarKind SidecarKind,
	long Length,
	DateTimeOffset LastWriteTimeUtc,
	ActivationStateFileEntry? ActivationEntry);
