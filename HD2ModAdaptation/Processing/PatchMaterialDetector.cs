using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Detects whether a patch is self-contained or requires external material patches.
// Strategy: Check the ratio of total entries to Unit entries.
// A complete embedded patch has more entries (includes materials, textures, etc.)
public sealed class PatchMaterialDetector
{
	private const ulong UnitTypeId = 0xe0a48d0be9a7453f;
	private const ulong MaterialTypeId = 0xeac0b497876adedf; // Correct Material TypeId from typehash.txt
	
	// Heuristic: A complete embedded patch typically has more entries per Unit
	// because it includes material data, textures, etc.
	private const double MinEntriesPerUnitForEmbedded = 1.5;

	/// <summary>
	/// Detects the material mode of a patch using heuristics.
	/// </summary>
	public async Task<PatchMaterialDetectionResult> DetectAsync(string patchPath)
	{
		ArgumentNullException.ThrowIfNull(patchPath);

		if (!File.Exists(patchPath))
		{
			throw new FileNotFoundException("Patch file not found", patchPath);
		}

		var scanner = new PatchTocScanner();
		var entries = await scanner.ScanEntriesAsync(patchPath);

		// Find Unit entries
		var unitEntries = entries.Where(e => e.AssetKey.TypeId == UnitTypeId).ToList();
		if (unitEntries.Count == 0)
		{
			return new PatchMaterialDetectionResult(
				PatchMaterialMode.Unknown, 
				totalEntries: entries.Count,
				unitEntries: 0,
				materialEntries: 0,
				entriesPerUnit: 0);
		}

		// Count Material entries
		var materialEntries = entries.Where(e => e.AssetKey.TypeId == MaterialTypeId).ToList();

		// Calculate ratio
		var entriesPerUnit = (double)entries.Count / unitEntries.Count;
		
		// Heuristic: If ratio is low, likely separated (only Units, no extra data)
		var mode = entriesPerUnit >= MinEntriesPerUnitForEmbedded
			? PatchMaterialMode.Embedded
			: PatchMaterialMode.Separated;

		return new PatchMaterialDetectionResult(
			mode,
			totalEntries: entries.Count,
			unitEntries: unitEntries.Count,
			materialEntries: materialEntries.Count,
			entriesPerUnit: entriesPerUnit);
	}
}

public enum PatchMaterialMode
{
	Unknown,    // Cannot determine (no Units found)
	Embedded,   // Self-contained patch with all data
	Separated   // Minimal patch, likely requires external materials
}

public sealed record PatchMaterialDetectionResult(
	PatchMaterialMode Mode,
	int totalEntries,
	int unitEntries,
	int materialEntries,
	double entriesPerUnit)
{
	public bool IsComplete => Mode == PatchMaterialMode.Embedded;
	
	public string GetDescription() => Mode switch
	{
		PatchMaterialMode.Embedded => $"Embedded (self-contained): {totalEntries} entries ({unitEntries} Units, {materialEntries} Materials) - ratio: {entriesPerUnit:F2}",
		PatchMaterialMode.Separated => $"Separated (minimal): {totalEntries} entries ({unitEntries} Units, {materialEntries} Materials) - ratio: {entriesPerUnit:F2}",
		_ => "Unknown"
	};
}
