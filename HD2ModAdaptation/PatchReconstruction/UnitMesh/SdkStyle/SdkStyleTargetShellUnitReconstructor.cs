namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Rebuilds one current target Unit from explicit source mappings, SDK-style vertex re-encoding, and minify-only coverage for every remaining target mesh.
public sealed class SdkStyleTargetShellUnitReconstructor
{
	private readonly PlaceholderUnitMeshMinifier minifier;
	private readonly SdkStyleMeshReencoder reencoder;
	private readonly UnitMeshWriter writer;

	public SdkStyleTargetShellUnitReconstructor(
		PlaceholderUnitMeshMinifier? minifier = null,
		SdkStyleMeshReencoder? reencoder = null,
		UnitMeshWriter? writer = null)
	{
		this.minifier = minifier ?? new PlaceholderUnitMeshMinifier();
		this.reencoder = reencoder ?? new SdkStyleMeshReencoder();
		this.writer = writer ?? new UnitMeshWriter();
	}

	public SdkStyleTargetShellUnitReconstructionResult Reconstruct(
		GameDataUnitMesh targetUnit,
		IReadOnlyCollection<PatchUnitMesh> sourceUnits,
		IReadOnlyCollection<TargetShellMeshMapping> mappings)
	{
		ArgumentNullException.ThrowIfNull(targetUnit);
		ArgumentNullException.ThrowIfNull(sourceUnits);
		ArgumentNullException.ThrowIfNull(mappings);

		var sourceByKey = sourceUnits.ToDictionary(unit => unit.Entry.AssetKey);
		var targetIndexes = new HashSet<int>();
		foreach (var mapping in mappings)
		{
			if (!targetIndexes.Add(mapping.TargetMeshInfoIndex)) throw new InvalidDataException($"Target mesh {mapping.TargetMeshInfoIndex} has more than one source mapping.");
			if (!sourceByKey.ContainsKey(mapping.SourceUnitAssetKey)) throw new KeyNotFoundException($"Source Unit 0x{mapping.SourceUnitAssetKey.FileId:x16} was not supplied.");
			if (!targetUnit.Model.RawMeshData.Any(mesh => mesh.MeshInfoIndex == mapping.TargetMeshInfoIndex)) throw new KeyNotFoundException($"Target Unit does not contain mesh {mapping.TargetMeshInfoIndex}.");
		}

		var model = targetIndexes.Count == 0
			? minifier.MinifyAll(targetUnit.Model)
			: minifier.MinifyExcept(targetUnit.Model, targetIndexes);
		var rebuiltBoneInfoIndexes = new HashSet<int>();
		var replacementMaterialIds = new HashSet<ulong>();
		foreach (var mapping in mappings.OrderBy(mapping => mapping.TargetMeshInfoIndex))
		{
			var sourceUnit = sourceByKey[mapping.SourceUnitAssetKey];
			var result = reencoder.Reencode(model, mapping.TargetMeshInfoIndex, sourceUnit.Model, mapping.SourceMeshInfoIndex);
			model = result.Model;
			rebuiltBoneInfoIndexes.Add(result.TargetBoneInfoIndex);
			foreach (var materialId in result.SourceMaterialIds) replacementMaterialIds.Add(materialId);
		}

		var targetMeshIndexes = targetUnit.Model.RawMeshData.Select(mesh => mesh.MeshInfoIndex).ToHashSet();
		var coveredIndexes = model.RawMeshData.Select(mesh => mesh.MeshInfoIndex).ToHashSet();
		if (!targetMeshIndexes.SetEquals(coveredIndexes)) throw new InvalidDataException("The reconstructed target shell does not cover every current target RawMesh.");
		var replacements = mappings.Select(mapping => mapping.TargetMeshInfoIndex).ToHashSet();
		var minified = targetMeshIndexes.Where(index => !replacements.Contains(index)).OrderBy(index => index).ToArray();
		if (minified.Any(index => !IsPlaceholder(model.RawMeshData.Single(mesh => mesh.MeshInfoIndex == index)))) throw new InvalidDataException("An unreplaced target mesh was not reduced to a placeholder.");

		var write = targetUnit.CompositePayload is null
			? writer.Write(model, targetUnit.Payload.TocData)
			: writer.Write(model, targetUnit.Payload.TocData, targetUnit.CompositePayload.TocData);
		return new SdkStyleTargetShellUnitReconstructionResult(model, write, mappings.ToArray(), minified, rebuiltBoneInfoIndexes.OrderBy(index => index).ToArray(), replacementMaterialIds.OrderBy(id => id).ToArray());
	}

	private static bool IsPlaceholder(UnitRawMeshData mesh)
		=> mesh.Vertices.Count <= 3 && mesh.Triangles.Count <= 1;
}

public sealed record SdkStyleTargetShellUnitReconstructionResult(
	UnitMeshModel Model,
	UnitMeshWriteResult WriteResult,
	IReadOnlyList<TargetShellMeshMapping> Replacements,
	IReadOnlyList<int> MinifiedTargetMeshInfoIndexes,
	IReadOnlyList<int> RebuiltBoneInfoIndexes,
	IReadOnlyList<ulong> ReplacementMaterialIds)
{
	public int CoveredTargetMeshCount => Model.RawMeshData.Count;
}