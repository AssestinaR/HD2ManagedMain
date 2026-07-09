using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds standardized derived library facts from persisted metadata and the current mod files.
public interface ILibraryDerivedDataService
{
	ValueTask<DerivedLibraryData> BuildAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		string? gameDataDirectory = null,
		CancellationToken cancellationToken = default);
}