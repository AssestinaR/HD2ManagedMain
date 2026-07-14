using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds strict AssetKey winners and coarse archive overlaps for a profile's expected deployment.
public interface IProfileOverrideGraphService
{
	ValueTask<ProfileOverrideGraph> BuildAsync(
		Profile profile,
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
