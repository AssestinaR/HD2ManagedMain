using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Classifies patch storage changes before derived-data parsing is attempted.
public interface IPatchStorageIntegrityValidator
{
	ValueTask<IReadOnlyList<PatchStorageIntegrityReport>> ValidateAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		PatchFileGroupFingerprintManifest? previousManifest,
		CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyList<PatchStorageIntegrityReport>> ValidateAndRepairAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		PatchFileGroupFingerprintManifest? previousManifest,
		CancellationToken cancellationToken = default);
}
