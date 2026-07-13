namespace HD2ModCore.Domain;

// Purpose: Stores the role, name, and content hash of one patch-group file.
public sealed record PatchFileFingerprint(
	PatchSidecarKind SidecarKind,
	string FileName,
	string ContentHash);
