namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Audits whether a source mesh can be rebaked against a current target Unit using the canonical Avatar rig, without writing geometry.
public sealed class TargetBakeCompatibilityAnalyzer
{
	private const float ActiveWeightThreshold = 0.001f;

	public TargetBakeCompatibilityDiagnostic Analyze(
		UnitMeshModel targetModel,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex,
		SdkStyleAvatarRigResource canonicalRig)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);
		ArgumentNullException.ThrowIfNull(canonicalRig);
		var sourceMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var targetMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceBoneInfo = FindBoneInfo(sourceModel, sourceMesh, "source");
		var usedHashes = CollectActiveBoneHashes(sourceMesh, sourceBoneInfo, sourceModel.TransformNameHashes);
		var targetHashes = targetModel.TransformNameHashes;
		var canonicalHashes = canonicalRig.TransformInfo.NameHashes;
		var missingFromCanonical = usedHashes.Where(hash => IndexOf(canonicalHashes, hash) < 0).ToArray();
		var missingFromTarget = usedHashes.Where(hash => IndexOf(targetHashes, hash) < 0).ToArray();
		var targetIndexes = usedHashes.Select(hash => IndexOf(targetHashes, hash)).Where(index => index >= 0).Select(index => checked((uint)index)).ToArray();
		var canonicalIndexes = usedHashes.Select(hash => IndexOf(canonicalHashes, hash)).Where(index => index >= 0).Select(index => checked((uint)index)).ToArray();
		var targetMeshTransformPresent = targetModel.Meshes.Any(mesh => mesh.Index == targetMeshInfoIndex && mesh.TransformIndex < targetModel.TransformInfo.Matrices.Count);
		var sourceMeshTransformPresent = sourceModel.Meshes.Any(mesh => mesh.Index == sourceMeshInfoIndex && mesh.TransformIndex < sourceModel.TransformInfo.Matrices.Count);
		var canonicalMatricesPresent = canonicalIndexes.All(index => index < canonicalRig.TransformInfo.Matrices.Count);
		var targetParentMismatchHashes = usedHashes.Where(hash =>
		{
			var targetIndex = IndexOf(targetHashes, hash);
			var canonicalIndex = IndexOf(canonicalHashes, hash);
			return targetIndex >= 0 && canonicalIndex >= 0 && ParentHash(targetModel.TransformInfo, targetHashes, targetIndex) != ParentHash(canonicalRig.TransformInfo, canonicalHashes, canonicalIndex);
		}).ToArray();
		var status = missingFromCanonical.Length != 0
			? "MissingCanonicalBones"
			: missingFromTarget.Length != 0
				? "NeedsTargetTransformExpansion"
				: !targetMeshTransformPresent || !sourceMeshTransformPresent || !canonicalMatricesPresent
					? "MissingBindMatrices"
					: targetParentMismatchHashes.Length != 0
						? "ParentChainMismatch"
						: "TargetBakeReady";
		return new TargetBakeCompatibilityDiagnostic(
			targetMeshInfoIndex,
			sourceMeshInfoIndex,
			usedHashes,
			missingFromCanonical,
			missingFromTarget,
			targetParentMismatchHashes,
			targetIndexes,
			canonicalIndexes,
			sourceMeshTransformPresent,
			targetMeshTransformPresent,
			canonicalMatricesPresent,
			status);
	}

	private static IReadOnlyList<uint> CollectActiveBoneHashes(UnitRawMeshData mesh, UnitBoneInfo boneInfo, IReadOnlyList<uint> hashes)
	{
		var result = new HashSet<uint>();
		foreach (var section in mesh.Sections)
		{
			if (section.MaterialIndex >= boneInfo.Remaps.Count) continue;
			var remap = boneInfo.Remaps[(int)section.MaterialIndex];
			foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
			{
				if (vertexIndex >= mesh.Vertices.Count) continue;
				var vertex = mesh.Vertices[(int)vertexIndex];
				var indices = vertex.Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? Array.Empty<uint>();
				var weights = vertex.Components.FirstOrDefault(component => component.Type == 7)?.FloatValues ?? Array.Empty<float>();
				for (var index = 0; index < Math.Min(indices.Length, weights.Length); index++)
				{
					if (weights[index] <= ActiveWeightThreshold || indices[index] >= remap.FakeIndices.Count) continue;
					var realPosition = remap.FakeIndices[(int)indices[index]];
					if (realPosition >= boneInfo.RealIndices.Count) continue;
					var transformIndex = boneInfo.RealIndices[(int)realPosition];
					if (transformIndex < hashes.Count) result.Add(hashes[(int)transformIndex]);
				}
			}
		}
		return result.OrderBy(hash => hash).ToArray();
	}

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new KeyNotFoundException($"The {role} Unit does not contain mesh {meshInfoIndex}.");

	private static UnitBoneInfo FindBoneInfo(UnitMeshModel model, UnitRawMeshData mesh, string role)
	{
		if (model.BoneInfos.Count == 0) throw new InvalidDataException($"The {role} Unit has no BoneInfo records.");
		var index = mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0;
		return model.BoneInfos[index];
	}

	private static uint? ParentHash(UnitTransformInfo transforms, IReadOnlyList<uint> hashes, int index)
	{
		if (index < 0 || index >= transforms.Entries.Count) return null;
		var parent = transforms.Entries[index].ParentIndex;
		return parent < hashes.Count ? hashes[parent] : null;
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}
}

public sealed record TargetBakeCompatibilityDiagnostic(
	int TargetMeshInfoIndex,
	int SourceMeshInfoIndex,
	IReadOnlyList<uint> ActiveSourceBoneHashes,
	IReadOnlyList<uint> MissingCanonicalBoneHashes,
	IReadOnlyList<uint> MissingTargetBoneHashes,
	IReadOnlyList<uint> ParentChainMismatchBoneHashes,
	IReadOnlyList<uint> TargetTransformIndexes,
	IReadOnlyList<uint> CanonicalTransformIndexes,
	bool SourceMeshTransformPresent,
	bool TargetMeshTransformPresent,
	bool CanonicalMatricesPresent,
	string Status);