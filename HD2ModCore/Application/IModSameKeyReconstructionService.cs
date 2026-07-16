using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Creates and writes a non-destructive same-AssetKey current-version reconstruction for one flat library Mod.
public interface IModSameKeyReconstructionService
{
	ValueTask<ModSameKeyReconstructionState> InspectAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default);

	ValueTask<SameKeyReconstructionOperationResult> WriteTestCopyAsync(
		ModNode source,
		string modsRootDirectory,
		string gameDataDirectory,
		string outputRootDirectory,
		CancellationToken cancellationToken = default);
}
