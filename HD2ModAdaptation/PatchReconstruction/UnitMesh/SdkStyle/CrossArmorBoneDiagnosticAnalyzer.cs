namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Produces read-only cross-armor bone and BoneInfo capacity diagnostics before an experimental target-shell candidate is written.
public sealed class CrossArmorBoneDiagnosticAnalyzer
{
	private const float ActiveWeightThreshold = 0.001f;
	private readonly SdkStyleMeshReencoder dryRunReencoder = new(allowSectionRebuild: true, propagateSourceMaterials: false, transformMeshSpace: true);

	public CrossArmorBoneTransferDiagnostic Analyze(
		UnitMeshModel targetModel,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		var targetMesh = FindMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceMesh = FindMesh(sourceModel, sourceMeshInfoIndex, "source");
		var targetBoneInfoIndex = GetBoneInfoIndex(targetModel, targetMesh);
		var sourceBoneInfoIndex = GetBoneInfoIndex(sourceModel, sourceMesh);
		var targetBoneInfo = targetBoneInfoIndex < 0 ? null : targetModel.BoneInfos[targetBoneInfoIndex];
		var sourceBoneInfo = sourceBoneInfoIndex < 0 ? null : sourceModel.BoneInfos[sourceBoneInfoIndex];
		var sourceBoneHashes = sourceBoneInfo is null
			? Array.Empty<uint>()
			: CollectSourceBoneHashes(sourceMesh, sourceBoneInfo, sourceModel.TransformNameHashes);
		var requiredTargetTransformIndexes = sourceBoneHashes
			.Select(hash => IndexOf(targetModel.TransformNameHashes, hash))
			.Where(index => index >= 0)
			.Select(index => checked((uint)index))
			.Distinct()
			.OrderBy(index => index)
			.ToArray();
		var missingTargetTransformHashes = sourceBoneHashes
			.Where(hash => IndexOf(targetModel.TransformNameHashes, hash) < 0)
			.Distinct()
			.OrderBy(hash => hash)
			.ToArray();
		var absentFromTargetPalette = targetBoneInfo is null
			? requiredTargetTransformIndexes
			: requiredTargetTransformIndexes.Where(index => !targetBoneInfo.RealIndices.Contains(index)).ToArray();
		var currentRecordBytes = targetBoneInfo is null ? 0 : EstimatePayloadBytes(targetBoneInfo);
		var dryRun = TryMeasureRebuiltBoneInfo(targetModel, targetMeshInfoIndex, sourceModel, sourceMeshInfoIndex);
		var missingMatrices = dryRun.Failure is null
			? Array.Empty<uint>()
			: FindUnavailableMatrixIndexes(dryRun.Failure, requiredTargetTransformIndexes);
		var actualRebuiltBytes = dryRun.PayloadBytes;
		var status = DetermineStatus(missingTargetTransformHashes, missingMatrices, targetBoneInfo, actualRebuiltBytes, dryRun.Failure);

		return new CrossArmorBoneTransferDiagnostic(
			targetMeshInfoIndex,
			sourceMeshInfoIndex,
			targetMesh.LodIndex,
			targetBoneInfoIndex,
			sourceBoneInfoIndex,
			targetModel.TransformNameHashes.Count,
			sourceModel.TransformNameHashes.Count,
			sourceBoneHashes,
			requiredTargetTransformIndexes,
			missingTargetTransformHashes,
			missingMatrices,
			absentFromTargetPalette,
			currentRecordBytes,
			actualRebuiltBytes,
			status,
			dryRun.Failure);
	}

	private (int PayloadBytes, string? Failure) TryMeasureRebuiltBoneInfo(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex)
	{
		try
		{
			var result = dryRunReencoder.Reencode(targetModel, targetMeshInfoIndex, sourceModel, sourceMeshInfoIndex);
			var matrices = result.RebuiltTargetBoneInfo.RealIndices.Zip(result.RebuiltTargetBoneInfo.BoneMatrices, (index, matrix) => (index, matrix))
				.Where(pair => pair.matrix.Length == 64)
				.ToDictionary(pair => pair.index, pair => pair.matrix);
			return (UnitMeshWriter.SerializeBoneInfo(result.RebuiltTargetBoneInfo, matrices).Length, null);
		}
		catch (InvalidDataException exception)
		{
			return (0, exception.Message);
		}
	}

	private static string DetermineStatus(IReadOnlyList<uint> missingHashes, IReadOnlyList<uint> missingMatrices, UnitBoneInfo? targetBoneInfo, int actualPayloadBytes, string? dryRunFailure)
	{
		if (missingHashes.Count != 0) return "NeedsTransformInfoExpansion";
		if (missingMatrices.Count != 0) return "NeedsRigMatrixProvider";
		if (dryRunFailure is not null) return "DryRunBlocked";
		if (targetBoneInfo is not null && actualPayloadBytes > EstimatePayloadBytes(targetBoneInfo)) return "NeedsBoneInfoRelocation";
		return "DirectTargetCompatible";
	}

	private static uint[] FindUnavailableMatrixIndexes(string failure, IReadOnlyList<uint> requiredIndexes)
	{
		const string marker = "No current-target inverse joint matrix exists for transform index ";
		var markerIndex = failure.IndexOf(marker, StringComparison.Ordinal);
		if (markerIndex < 0) return Array.Empty<uint>();
		var text = failure[(markerIndex + marker.Length)..].TrimEnd('.');
		return uint.TryParse(text, out var index) && requiredIndexes.Contains(index) ? new[] { index } : Array.Empty<uint>();
	}

	private static IReadOnlyList<uint> CollectSourceBoneHashes(UnitRawMeshData mesh, UnitBoneInfo boneInfo, IReadOnlyList<uint> transformHashes)
	{
		var hashes = new HashSet<uint>();
		foreach (var section in mesh.Sections)
		{
			if (section.MaterialIndex >= boneInfo.Remaps.Count) continue;
			foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
			{
				if (vertexIndex >= mesh.Vertices.Count) continue;
				var vertex = mesh.Vertices[(int)vertexIndex];
				var indices = vertex.Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? Array.Empty<uint>();
				var weights = vertex.Components.FirstOrDefault(component => component.Type == 7)?.FloatValues ?? Array.Empty<float>();
				for (var influence = 0; influence < indices.Length; influence++)
				{
					if (influence < weights.Length && weights[influence] <= ActiveWeightThreshold) continue;
					var remap = boneInfo.Remaps[(int)section.MaterialIndex];
					if (indices[influence] >= remap.FakeIndices.Count) continue;
					var realPosition = remap.FakeIndices[(int)indices[influence]];
					if (realPosition >= boneInfo.RealIndices.Count) continue;
					var transformIndex = boneInfo.RealIndices[(int)realPosition];
					if (transformIndex < transformHashes.Count) hashes.Add(transformHashes[(int)transformIndex]);
				}
			}
		}
		return hashes.OrderBy(hash => hash).ToArray();
	}

	private static int EstimatePayloadBytes(UnitBoneInfo boneInfo)
		=> checked(16 + boneInfo.RealIndices.Count * 64 + boneInfo.RealIndices.Count * 4 + 4 + boneInfo.Remaps.Count * 8 + boneInfo.Remaps.Sum(remap => remap.FakeIndices.Count) * 4);

	private static UnitRawMeshData FindMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new KeyNotFoundException($"The {role} Unit does not contain mesh {meshInfoIndex}.");

	private static int GetBoneInfoIndex(UnitMeshModel model, UnitRawMeshData mesh)
		=> model.BoneInfos.Count == 0 ? -1 : mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0;

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}
}

public sealed record CrossArmorBoneTransferDiagnostic(
	int TargetMeshInfoIndex,
	int SourceMeshInfoIndex,
	int TargetLodIndex,
	int TargetBoneInfoIndex,
	int SourceBoneInfoIndex,
	int TargetTransformCount,
	int SourceTransformCount,
	IReadOnlyList<uint> SourceBoneHashes,
	IReadOnlyList<uint> RequiredTargetTransformIndexes,
	IReadOnlyList<uint> MissingTargetTransformHashes,
	IReadOnlyList<uint> MissingInverseJointMatrixTransformIndexes,
	IReadOnlyList<uint> AbsentFromTargetLodPaletteTransformIndexes,
	int CurrentTargetBoneInfoPayloadBytes,
	int ActualRebuiltBoneInfoPayloadBytes,
	string Status,
	string? DryRunFailure);
