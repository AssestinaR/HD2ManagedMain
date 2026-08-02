using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Compiles all provisional skinned meshes of one target LOD into the SDK-style shared BoneInfo palette.
// SDK reference: BoneInfo.SetRemap() builds material-ordinal remaps after final object topology and vertex groups exist.
public sealed record CanonicalLodBoneInput(UnitRawMeshData Mesh, UnitBoneInfo ProvisionalBoneInfo);

public sealed record CanonicalLodBoneCompilation(
	UnitBoneInfo? BoneInfo,
	IReadOnlyList<UnitRawMeshData> Meshes,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => BoneInfo is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalLodBonePaletteCompiler
{
	public CanonicalLodBoneCompilation TryCompile(
		UnitMeshModel target,
		int lodIndex,
		IReadOnlyList<CanonicalLodBoneInput> inputs)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(inputs);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		if (lodIndex < 0 || lodIndex >= target.BoneInfos.Count)
			diagnostics.Add(new("TargetBoneInfoMissing", "The target LOD does not identify a writable BoneInfo."));
		if (inputs.Count == 0)
			diagnostics.Add(new("EmptyLodBoneCompilation", "A Canonical LOD palette requires at least one final skinned mesh."));
		if (inputs.Any(input => input.Mesh.LodIndex != lodIndex))
			diagnostics.Add(new("LodBoneMeshMismatch", "A final mesh was supplied to the wrong Canonical LOD palette."));
		if (diagnostics.Count != 0)
			return new(null, [], Array.AsReadOnly(diagnostics.ToArray()));

		var targetMeshes = inputs.Select(input => target.Meshes.SingleOrDefault(mesh => mesh.Index == input.Mesh.MeshInfoIndex)).ToArray();
		if (targetMeshes.Any(mesh => mesh is null))
			return new(null, [], [new("TargetMeshCardinality", "A final Canonical mesh has no matching target MeshInfo.")]);
		var transforms = targetMeshes.Select(mesh => mesh!.TransformIndex).Distinct().ToArray();
		if (transforms.Length != 1)
			return new(null, [], [new("SharedLodTransformMismatch", "Meshes sharing one target BoneInfo use different mesh transforms; Canonical refuses to synthesize incompatible inverse-joint matrices.")]);

		var hashes = target.TransformNameHashes;
		var palette = new List<uint>();
		foreach (var input in inputs)
		{
			foreach (var hash in ResolveActiveHashes(input.Mesh, input.ProvisionalBoneInfo, hashes, diagnostics))
				if (!palette.Contains(hash)) palette.Add(hash);
		}
		if (diagnostics.Count != 0)
			return new(null, [], Array.AsReadOnly(diagnostics.ToArray()));

		var realIndices = palette.Select(hash => IndexOf(hashes, hash)).ToArray();
		if (realIndices.Any(index => index < 0))
			return new(null, [], [new("MissingTargetBone", "An active final bone is absent from target TransformInfo.")]);
		var layouts = inputs.Select(input => CanonicalFinalMaterialLayout.TryCreate(input.Mesh)).ToArray();
		foreach (var layout in layouts) diagnostics.AddRange(layout.Diagnostics);
		if (diagnostics.Count != 0)
			return new(null, [], Array.AsReadOnly(diagnostics.ToArray()));
		if (inputs.Where((input, index) => !UsesOwnFinalMaterialLayout(input.Mesh, layouts[index]))
			.Any())
			return new(null, [], [new("InvalidProvisionalMaterialOrdinal", "A provisional skinned mesh must use its own final material-slot ordinal before shared LOD compilation.")]);
		var sharedMaterialOrdinals = BuildSharedMaterialOrdinals(inputs);
		var materialCount = checked((uint)sharedMaterialOrdinals.Count);
		var remaps = new List<UnitBoneRemap>(checked((int)materialCount));
		var offset = checked((uint)(4 + materialCount * 8));
		for (var material = 0u; material < materialCount; material++)
		{
			var fake = Enumerable.Range(0, palette.Count).Select(index => checked((uint)index)).ToArray();
			remaps.Add(new UnitBoneRemap(checked((int)material), offset, fake));
			offset = checked(offset + checked((uint)(fake.Length * sizeof(uint))));
		}

		var matrices = BuildInverseJointMatrices(target, transforms[0], realIndices, diagnostics);
		if (diagnostics.Count != 0)
			return new(null, [], Array.AsReadOnly(diagnostics.ToArray()));
		var template = target.BoneInfos[lodIndex];
		var boneInfo = template with { NumBones = checked((uint)realIndices.Length), RealIndices = realIndices.Select(index => checked((uint)index)).ToArray(), Remaps = remaps, BoneMatrices = matrices };
		var meshes = inputs.Select(input => RewriteMesh(input.Mesh, input.ProvisionalBoneInfo, boneInfo, hashes, sharedMaterialOrdinals, diagnostics)).ToArray();
		return diagnostics.Count == 0
			? new(boneInfo, meshes, Array.Empty<CanonicalPlanDiagnostic>())
			: new(null, [], Array.AsReadOnly(diagnostics.ToArray()));
	}

	private static IEnumerable<uint> ResolveActiveHashes(UnitRawMeshData mesh, UnitBoneInfo info, IReadOnlyList<uint> hashes, List<CanonicalPlanDiagnostic> diagnostics)
	{
		foreach (var section in mesh.Sections)
		{
			var remap = info.Remaps.SingleOrDefault(item => item.MaterialIndex == section.MaterialIndex);
			if (remap is null) { diagnostics.Add(new("MissingProvisionalBoneRemap", "A final material ordinal has no provisional BoneInfo remap.")); continue; }
			foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
			{
				if (vertexIndex >= mesh.Vertices.Count) { diagnostics.Add(new("IndexOutOfRange", "A final section references a vertex outside its mesh.")); continue; }
				var vertex = mesh.Vertices[(int)vertexIndex];
				var indices = vertex.Components.Where(component => component.Type == 6).ToArray();
				var weights = vertex.Components.SingleOrDefault(component => component.Type == 7 && component.Index == 0);
				if (indices.Length == 0 || weights is null) { diagnostics.Add(new("FinalSkinningLayoutMissing", "A final skinned vertex must contain one or more Type=6 components and Type=7 at index 0.")); continue; }
				var values = weights.FloatValues.Length > 0 ? weights.FloatValues : weights.UIntValues.Select(value => value / 255f).ToArray();
				foreach (var indexGroup in indices)
				{
					if (indexGroup.UIntValues.Length != values.Length) { diagnostics.Add(new("FinalBoneWeightIndexArityMismatch", "A final skinned vertex has different bone-index and weight counts.")); continue; }
					for (var index = 0; index < values.Length; index++)
					{
						if (!float.IsFinite(values[index]) || values[index] <= 0) continue;
						var fake = indexGroup.UIntValues[index];
						if (fake >= remap.FakeIndices.Count || remap.FakeIndices[(int)fake] >= info.RealIndices.Count) { diagnostics.Add(new("InvalidFinalBoneRemap", "A final fake bone index cannot be resolved through its provisional palette.")); continue; }
						var real = info.RealIndices[(int)remap.FakeIndices[(int)fake]];
						if (real >= hashes.Count) { diagnostics.Add(new("InvalidFinalBonePalette", "A final palette index is absent from TransformInfo.")); continue; }
						yield return hashes[(int)real];
					}
				}
			}
		}
	}

	private static UnitRawMeshData RewriteMesh(UnitRawMeshData mesh, UnitBoneInfo oldInfo, UnitBoneInfo unified, IReadOnlyList<uint> hashes, IReadOnlyDictionary<uint, uint> sharedMaterialOrdinals, List<CanonicalPlanDiagnostic> diagnostics)
	{
		var vertices = new List<UnitRawVertexRecord>();
		var sections = new List<UnitRawMeshSectionData>(mesh.Sections.Count);
		foreach (var section in mesh.Sections)
		{
			var oldRemap = oldInfo.Remaps.SingleOrDefault(item => item.MaterialIndex == section.MaterialIndex);
			if (!sharedMaterialOrdinals.TryGetValue(section.MaterialSlotId, out var sharedMaterialOrdinal))
			{
				diagnostics.Add(new("MissingSharedMaterialOrdinal", "A final target material slot is absent from the shared LOD layout."));
				continue;
			}
			var newRemap = unified.Remaps.SingleOrDefault(item => item.MaterialIndex == sharedMaterialOrdinal);
			if (oldRemap is null || newRemap is null) { diagnostics.Add(new("MissingUnifiedBoneRemap", "A final material ordinal has no unified BoneInfo remap.")); continue; }
			var copied = new Dictionary<uint, uint>();
			uint Copy(uint sourceIndex)
			{
				if (sourceIndex >= mesh.Vertices.Count) { diagnostics.Add(new("IndexOutOfRange", "A final section references a vertex outside its mesh.")); return 0; }
				if (copied.TryGetValue(sourceIndex, out var existing)) return existing;
				var source = mesh.Vertices[(int)sourceIndex];
				var components = source.Components.Select(component => component.Type == 6
					? component with { UIntValues = component.UIntValues.Select(fake => Remap(fake, oldRemap, oldInfo, newRemap, unified, hashes, diagnostics)).ToArray(), RawData = Array.Empty<byte>() }
					: component).ToArray();
				var output = checked((uint)vertices.Count);
				vertices.Add(source with { Index = output, Data = Array.Empty<byte>(), Components = components });
				copied.Add(sourceIndex, output);
				return output;
			}
			sections.Add(section with
			{
				MaterialIndex = sharedMaterialOrdinal,
				Triangles = section.Triangles.Select(triangle => new UnitTriangleIndices(Copy(triangle.A), Copy(triangle.B), Copy(triangle.C))).ToArray()
			});
		}
		return mesh with { Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray(), Vertices = vertices };
	}

	private static bool UsesOwnFinalMaterialLayout(UnitRawMeshData mesh, CanonicalFinalMaterialLayoutResult layout)
		=> mesh.Sections.Select((section, index) => section.MaterialIndex == layout.GetMaterialOrdinal(index)).All(value => value);

	private static IReadOnlyDictionary<uint, uint> BuildSharedMaterialOrdinals(IReadOnlyList<CanonicalLodBoneInput> inputs)
	{
		var result = new Dictionary<uint, uint>();
		foreach (var section in inputs.SelectMany(input => input.Mesh.Sections))
			if (!result.ContainsKey(section.MaterialSlotId))
				result.Add(section.MaterialSlotId, checked((uint)result.Count));
		return result;
	}

	private static uint Remap(uint fake, UnitBoneRemap oldRemap, UnitBoneInfo oldInfo, UnitBoneRemap newRemap, UnitBoneInfo unified, IReadOnlyList<uint> hashes, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (fake >= oldRemap.FakeIndices.Count) { diagnostics.Add(new("InvalidFinalBoneRemap", "A final fake index exceeds the provisional remap.")); return 0; }
		var oldPalette = oldRemap.FakeIndices[(int)fake];
		if (oldPalette >= oldInfo.RealIndices.Count || oldInfo.RealIndices[(int)oldPalette] >= hashes.Count) { diagnostics.Add(new("InvalidFinalBonePalette", "A final fake index resolves outside TransformInfo.")); return 0; }
		var hash = hashes[(int)oldInfo.RealIndices[(int)oldPalette]];
		var targetTransform = IndexOf(hashes, hash);
		var unifiedPalette = IndexOf(unified.RealIndices, checked((uint)targetTransform));
		var newFake = IndexOf(newRemap.FakeIndices, checked((uint)unifiedPalette));
		if (targetTransform < 0 || unifiedPalette < 0 || newFake < 0) { diagnostics.Add(new("MissingUnifiedBone", "An active bone is absent from the unified palette.")); return 0; }
		return checked((uint)newFake);
	}

	private static IReadOnlyList<byte[]> BuildInverseJointMatrices(UnitMeshModel target, uint meshTransformIndex, IReadOnlyList<int> realIndices, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (meshTransformIndex >= target.TransformInfo.Matrices.Count) { diagnostics.Add(new("MissingMeshTransform", "The target LOD has no mesh TransformInfo matrix.")); return []; }
		var mesh = ToMatrix(target.TransformInfo.Matrices[(int)meshTransformIndex], diagnostics);
		if (mesh is null || !Matrix4x4.Invert(mesh.Value, out var inverseMesh)) { diagnostics.Add(new("NonInvertibleMeshTransform", "The target mesh transform is not invertible.")); return []; }
		var matrices = new List<byte[]>(realIndices.Count);
		foreach (var index in realIndices)
		{
			if (index < 0 || index >= target.TransformInfo.Matrices.Count) { diagnostics.Add(new("MissingBoneTransform", "A target bone TransformInfo matrix is absent.")); continue; }
			var bone = ToMatrix(target.TransformInfo.Matrices[index], diagnostics);
			if (bone is null || !Matrix4x4.Invert(bone.Value * inverseMesh, out var inverseJoint)) { diagnostics.Add(new("NonInvertibleBoneTransform", "A target inverse-joint matrix cannot be generated safely.")); continue; }
			matrices.Add([.. new[] { inverseJoint.M11, inverseJoint.M12, inverseJoint.M13, inverseJoint.M14, inverseJoint.M21, inverseJoint.M22, inverseJoint.M23, inverseJoint.M24, inverseJoint.M31, inverseJoint.M32, inverseJoint.M33, inverseJoint.M34, inverseJoint.M41, inverseJoint.M42, inverseJoint.M43, inverseJoint.M44 }.SelectMany(BitConverter.GetBytes)]);
		}
		return matrices;
	}

	private static Matrix4x4? ToMatrix(UnitTransformMatrix matrix, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (matrix.Values.Count != 16 || matrix.Values.Any(value => !float.IsFinite(value))) { diagnostics.Add(new("InvalidTransformInfoMatrix", "TransformInfo matrix is not a finite 4x4 matrix.")); return null; }
		var v = matrix.Values;
		return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}
}