using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds archive-browser rows from Core facts so Manager code-behind performs display logic only.
public interface IGameDataArchiveBrowserService
{
	ValueTask<GameDataArchiveBrowserSnapshot?> BuildAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		string gameDataDirectory,
		CancellationToken cancellationToken = default);
}
