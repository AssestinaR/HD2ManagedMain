using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Temporarily combines model and material patches in memory without modifying original files.
// This allows separated patches to be processed as if they were embedded patches.
public sealed class TemporaryMaterialCombiner
{
	private const ulong UnitTypeId = 0xe0a48d0be9a7453f;

	/// <summary>
	/// Combines model patch with optional material patches in memory.
	/// Returns the combined entries that can be used for reading Units with materials.
	/// </summary>
	public async Task<CombinedPatchResult> CombineAsync(
		string modelPatchPath,
		IReadOnlyList<string>? materialPatchPaths = null)
	{
		ArgumentNullException.ThrowIfNull(modelPatchPath);

		if (!File.Exists(modelPatchPath))
		{
			throw new FileNotFoundException("Model patch not found", modelPatchPath);
		}

		var scanner = new PatchTocScanner();
		
		// 1. Read model patch entries
		var modelEntries = await scanner.ScanEntriesAsync(modelPatchPath);
		var unitEntries = modelEntries.Where(e => e.AssetKey.TypeId == UnitTypeId).ToList();

		if (unitEntries.Count == 0)
		{
			throw new InvalidOperationException("No Unit entries found in model patch");
		}

		// 2. If no material patches provided, return model entries only
		if (materialPatchPaths == null || materialPatchPaths.Count == 0)
		{
			return new CombinedPatchResult(
				modelEntries,
				modelPatchPath,
				[],
				WasCombined: false);
		}

		// 3. Read all material patch entries
		var allMaterialEntries = new List<PatchTocEntry>();
		var usedMaterialPaths = new List<string>();

		foreach (var matPath in materialPatchPaths)
		{
			if (!File.Exists(matPath))
			{
				Console.WriteLine($"⚠️ Material patch not found, skipping: {matPath}");
				continue;
			}

			var matEntries = await scanner.ScanEntriesAsync(matPath);
			allMaterialEntries.AddRange(matEntries);
			usedMaterialPaths.Add(matPath);
		}

		if (allMaterialEntries.Count == 0)
		{
			Console.WriteLine("⚠️ No valid material patches found, using model patch only");
			return new CombinedPatchResult(
				modelEntries,
				modelPatchPath,
				[],
				WasCombined: false);
		}

		// 4. Combine entries (model + materials)
		// Note: If there are duplicate AssetKeys, later entries (materials) will override earlier ones
		var combinedEntries = modelEntries.Concat(allMaterialEntries).ToList();

		return new CombinedPatchResult(
			combinedEntries,
			modelPatchPath,
			usedMaterialPaths,
			WasCombined: true);
	}

	/// <summary>
	/// Reads a Unit from combined entries (model + materials).
	/// </summary>
	public async Task<PatchUnitMesh> ReadCombinedUnitAsync(
		CombinedPatchResult combinedResult,
		int unitIndex = 0)
	{
		ArgumentNullException.ThrowIfNull(combinedResult);

		var unitEntries = combinedResult.CombinedEntries
			.Where(e => e.AssetKey.TypeId == UnitTypeId)
			.ToList();

		if (unitIndex < 0 || unitIndex >= unitEntries.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(unitIndex), 
				$"Unit index {unitIndex} is out of range (0-{unitEntries.Count - 1})");
		}

		var reader = new PatchUnitMeshReader();
		return await reader.ReadAsync(unitEntries[unitIndex], combinedResult.CombinedEntries);
	}
}

/// <summary>
/// Result of combining model and material patches.
/// </summary>
public sealed record CombinedPatchResult(
	IReadOnlyList<PatchTocEntry> CombinedEntries,
	string ModelPatchPath,
	IReadOnlyList<string> MaterialPatchPaths,
	bool WasCombined)
{
	public int TotalEntries => CombinedEntries.Count;
	public int UnitCount => CombinedEntries.Count(e => e.AssetKey.TypeId == 0xe0a48d0be9a7453f);
	
	public string GetDescription() => WasCombined
		? $"Combined: {TotalEntries} entries from model + {MaterialPatchPaths.Count} material patches"
		: $"Model only: {TotalEntries} entries";
}
