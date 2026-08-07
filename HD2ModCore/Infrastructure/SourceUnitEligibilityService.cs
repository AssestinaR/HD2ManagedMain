using HD2ModAdaptation.Analysis;
using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// Purpose: Selects readable source Units with a real default-imported visible mesh.
public sealed class SourceUnitEligibilityService : ISourceUnitEligibilityService
{
	public SourceUnitEligibilitySelection Select(IReadOnlyList<PatchGroupAnalysis> analyses)
	{
		ArgumentNullException.ThrowIfNull(analyses);
		var units = analyses
			.SelectMany(analysis => analysis.PreparedSourceUnits)
			.GroupBy(unit => new AssetKey(unit.Entry.AssetKey.TypeId, unit.Entry.AssetKey.FileId))
			.OrderBy(group => group.Key.TypeId)
			.ThenBy(group => group.Key.FileId)
			.Select(group => Evaluate(group.Key, group.ToArray()))
			.ToArray();
		return new SourceUnitEligibilitySelection(
			units.Where(unit => unit.IsEligible).Select(unit => unit.UnitAssetKey).ToHashSet(),
			units);
	}

	private static SourceUnitEligibility Evaluate(AssetKey key, IReadOnlyList<SourceUnitPreparation> preparations)
	{
		var readable = preparations.Where(unit => unit.IsReadable).ToArray();
		if (readable.Length == 0)
			return new SourceUnitEligibility(key, false, "SourceUnitUnreadable");
		return readable.Any(unit => unit.Meshes.Any(mesh => mesh.IsTransferable && mesh.LodIndex == 0))
			? new SourceUnitEligibility(key, true, "TransferableVisibleLod0")
			: new SourceUnitEligibility(key, false, "NoTransferableVisibleLod0");
	}
}
