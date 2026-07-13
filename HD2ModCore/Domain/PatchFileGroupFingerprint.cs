namespace HD2ModCore.Domain;

// Purpose: Identifies the on-disk contents of one archive/patch file group.
public sealed record PatchFileGroupFingerprint(
	string GroupName,
	string ContentHash,
	IReadOnlyList<string> Files,
	IReadOnlyList<PatchFileFingerprint>? FileFingerprints = null)
{
	public IReadOnlyList<PatchFileFingerprint> EffectiveFileFingerprints
		=> FileFingerprints ?? Array.Empty<PatchFileFingerprint>();
}
