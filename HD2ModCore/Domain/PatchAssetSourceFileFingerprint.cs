namespace HD2ModCore.Domain;

// Purpose: Captures patch file identity used to determine whether cached asset analysis is still valid.
public sealed record PatchAssetSourceFileFingerprint(
	string RelativePath,
	long Length,
	DateTimeOffset LastWriteTimeUtc);