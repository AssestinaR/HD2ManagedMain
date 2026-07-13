namespace HD2ModCore.Domain;

// Purpose: Persists the latest patch-group fingerprints used by startup dirty checks.
public sealed record PatchFileGroupFingerprintManifest(
	int Version,
	DateTimeOffset BuiltUtc,
	IReadOnlyDictionary<ModNodeId, IReadOnlyList<PatchFileGroupFingerprint>> Nodes)
{
	public bool SupportsFileLevelMatching => Version >= 2 && Nodes.Values
		.SelectMany(groups => groups)
		.All(group => group.EffectiveFileFingerprints.Count > 0 || group.Files.Count == 0);
}
