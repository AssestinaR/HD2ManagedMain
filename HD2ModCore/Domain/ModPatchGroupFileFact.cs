namespace HD2ModCore.Domain;

// Purpose: Immutable file fact for one base, stream or gpu_resources member of a source patch group.
public sealed record ModPatchGroupFileFact(
	PatchSidecarKind SidecarKind,
	string FilePath,
	string FileName,
	long Length,
	DateTimeOffset LastWriteTimeUtc);
