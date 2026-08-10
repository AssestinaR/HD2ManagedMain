using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Derives an explainable library role from Game Data mappings and the persistent reference graph.
public interface IModAssetRoleFactsService
{
	ValueTask<ModAssetRoleFacts> GetAsync(
		ModNode node,
		string modsRootDirectory,
		CancellationToken cancellationToken = default);
}
