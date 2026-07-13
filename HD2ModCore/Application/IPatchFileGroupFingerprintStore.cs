using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Persists patch-group fingerprint manifests independently from the mod library JSON.
public interface IPatchFileGroupFingerprintStore
{
	ValueTask<PatchFileGroupFingerprintManifest?> TryLoadAsync(CancellationToken cancellationToken = default);
	ValueTask SaveAsync(PatchFileGroupFingerprintManifest manifest, CancellationToken cancellationToken = default);
}
