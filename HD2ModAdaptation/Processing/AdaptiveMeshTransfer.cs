using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Adaptive mesh transfer that handles both embedded and separated patches.
// Integrates PatchMaterialDetector, TemporaryMaterialCombiner, and MeshTransfer.
public sealed class AdaptiveMeshTransfer
{
	private readonly PatchMaterialDetector detector;
	private readonly TemporaryMaterialCombiner combiner;
	private readonly MeshTransfer meshTransfer;

	public AdaptiveMeshTransfer(
		PatchMaterialDetector? detector = null,
		TemporaryMaterialCombiner? combiner = null,
		MeshTransfer? meshTransfer = null)
	{
		this.detector = detector ?? new PatchMaterialDetector();
		this.combiner = combiner ?? new TemporaryMaterialCombiner();
		this.meshTransfer = meshTransfer ?? new MeshTransfer();
	}

	/// <summary>
	/// Transfers mesh from source patch to target model, handling both embedded and separated patches.
	/// </summary>
	/// <param name="sourcePatchPath">Path to the source patch (model)</param>
	/// <param name="targetModel">Target Unit model to transfer into</param>
	/// <param name="targetMeshIndex">Target mesh index to replace</param>
	/// <param name="sourceMeshIndex">Source mesh index to use (default: 0)</param>
	/// <param name="materialPatchPaths">Optional material patches for separated patches</param>
	public async Task<AdaptiveMeshTransferResult> TransferAsync(
		string sourcePatchPath,
		UnitMeshModel targetModel,
		int targetMeshIndex,
		int sourceMeshIndex = 0,
		IReadOnlyList<string>? materialPatchPaths = null)
	{
		ArgumentNullException.ThrowIfNull(sourcePatchPath);
		ArgumentNullException.ThrowIfNull(targetModel);

		// 1. Detect patch type
		var detection = await detector.DetectAsync(sourcePatchPath);
		Console.WriteLine($"Detected: {detection.GetDescription()}");

		// 2. If separated and no materials provided, warn but continue
		if (detection.Mode == PatchMaterialMode.Separated && 
		    (materialPatchPaths == null || materialPatchPaths.Count == 0))
		{
			Console.WriteLine("⚠️ Warning: Separated patch detected but no materials provided");
			Console.WriteLine("   Material references may be incomplete");
		}

		// 3. Combine if needed
		var combined = await combiner.CombineAsync(sourcePatchPath, materialPatchPaths);
		Console.WriteLine($"Combined: {combined.GetDescription()}");

		// 4. Read source Unit
		var sourceUnit = await combiner.ReadCombinedUnitAsync(combined, 0);
		var sourceModel = sourceUnit.Model;

		// 5. Validate mesh indices
		if (sourceMeshIndex < 0 || sourceMeshIndex >= sourceModel.RawMeshData.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshIndex),
				$"Source mesh index {sourceMeshIndex} is out of range (0-{sourceModel.RawMeshData.Count - 1})");
		}

		if (targetMeshIndex < 0 || targetMeshIndex >= targetModel.RawMeshData.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(targetMeshIndex),
				$"Target mesh index {targetMeshIndex} is out of range (0-{targetModel.RawMeshData.Count - 1})");
		}

		// 6. Perform mesh transfer using existing MeshTransfer
		var transferResult = meshTransfer.Transfer(
			targetModel,
			targetMeshIndex,
			sourceModel,
			sourceMeshIndex);

		return new AdaptiveMeshTransferResult(
			transferResult.Model,
			detection.Mode,
			combined.WasCombined,
			combined.MaterialPatchPaths,
			sourceMeshIndex,
			targetMeshIndex);
	}
}

/// <summary>
/// Result of adaptive mesh transfer.
/// </summary>
public sealed record AdaptiveMeshTransferResult(
	UnitMeshModel UpdatedModel,
	PatchMaterialMode OriginalMode,
	bool WasCombined,
	IReadOnlyList<string> UsedMaterialPaths,
	int SourceMeshIndex,
	int TargetMeshIndex)
{
	public bool IsComplete => OriginalMode == PatchMaterialMode.Embedded || WasCombined;
	
	public string GetSummary() => OriginalMode switch
	{
		PatchMaterialMode.Embedded => $"Embedded patch processed (no combining needed)",
		PatchMaterialMode.Separated when WasCombined => 
			$"Separated patch combined with {UsedMaterialPaths.Count} material patches",
		PatchMaterialMode.Separated => 
			$"Separated patch processed without materials (may be incomplete)",
		_ => "Unknown"
	};
}
