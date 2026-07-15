using HD2ModCore.Domain;

// Purpose: Projects persisted Mod asset/reference facts into material delivery modes without re-reading Patch payloads.
public interface IMaterialDeliveryFactsService
{
	ValueTask<MaterialDeliveryFacts> GetAsync(
		ModNodeId nodeId,
		LibrarySnapshot librarySnapshot,
		CancellationToken cancellationToken = default);
}