using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Scans on-disk patch groups and computes stable content fingerprints.
public interface IPatchFileGroupFingerprintScanner
{
	ValueTask<IReadOnlyDictionary<ModNodeId, IReadOnlyList<PatchFileGroupFingerprint>>> ScanAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
