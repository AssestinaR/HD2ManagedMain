using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Builds authoritative top-level patch-group and AssetKey content facts for flat internal mods.
public interface IModContentFactsService
{
	ValueTask<ModContentFacts> GetNodeFactsAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);

	ValueTask<IReadOnlyDictionary<ModNodeId, ModContentFacts>> GetLibraryFactsAsync(
		LibrarySnapshot snapshot,
		string modsRootDirectory,
		IReadOnlySet<ModNodeId>? nodeIds = null,
		CancellationToken cancellationToken = default);
}
