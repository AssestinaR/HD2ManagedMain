using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;

namespace HD2ModCore.Application;

// Purpose: Reads indexed Armor and Helmet Unit-part facts and produces a no-write cross-equipment transfer preview.
public interface IEquipmentUnitCatalogService
{
	ValueTask<IReadOnlyList<EquipmentUnitCatalogEntry>> GetEntriesAsync(
		IReadOnlySet<AssetKey>? unitAssetKeys = null,
		CancellationToken cancellationToken = default);

	[Obsolete("Use IModEquipmentSourceFactsReader so source geometry is read through the unified Mod information reader.")]
	ValueTask<IReadOnlyList<EquipmentUnitCatalogEntry>> FilterTransferableSourcePartsAsync(
		IReadOnlyList<EquipmentUnitCatalogEntry> candidates,
		IReadOnlyList<string> patchTocPaths,
		CancellationToken cancellationToken = default);

	[Obsolete("Use IModEquipmentSourceFactsReader so source geometry is read through the unified Mod information reader.")]
	ValueTask<IReadOnlyList<EquipmentUnitCatalogEntry>> FilterTransferableSourcePartsAsync(
		IReadOnlyList<EquipmentUnitCatalogEntry> candidates,
		IReadOnlyList<string> patchTocPaths,
		CancellationToken cancellationToken,
		IReadOnlyDictionary<string, IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>> preparedEntries);

	ValueTask<CrossArmorTransferPlan> CreatePlanAsync(
		IReadOnlyList<EquipmentUnitCatalogEntry> sourceCandidates,
		IReadOnlyList<EquipmentUnitCatalogEntry> targetCandidates,
		string? selectedSourceArchiveId,
		UnitMeshBodyVariant? selectedSourceBodyVariant,
		CrossArmorBodyVariantPreference bodyVariantPreference,
		CrossArmorLayerPreference layerPreference,
		IReadOnlyCollection<string> selectedTargetArchiveIds,
		IReadOnlyList<CrossArmorManualMapping>? manualMappings = null,
		IReadOnlyList<CrossArmorManualSuppression>? manualSuppressions = null,
		bool manualMode = false,
		IReadOnlyCollection<string>? additionalSourceArchiveIds = null,
		CancellationToken cancellationToken = default);
}
