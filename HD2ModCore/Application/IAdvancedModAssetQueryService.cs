using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Queries the unified advanced AssetKey table without rescanning immutable Mod files.
public interface IAdvancedModAssetQueryService
{
	ValueTask<IReadOnlyList<AdvancedModAssetRow>> QueryAsync(ModNodeId nodeId, LibrarySnapshot librarySnapshot, ProfileOverrideGraph? profileGraph, ProfileMaterialDiagnostics? diagnostics, CancellationToken cancellationToken = default);
}