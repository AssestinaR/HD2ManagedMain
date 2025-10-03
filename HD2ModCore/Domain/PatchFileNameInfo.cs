namespace HD2ModCore.Domain;

public sealed record PatchFileNameInfo(
	string ArchiveHex16,
	int PatchIndex,
	PatchSidecarKind SidecarKind,
	string FullFileName);
