using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Attaches static source geometry to one explicit target-rig anchor without assuming palette index zero.
public sealed record CanonicalStaticMeshBindResult(
	UnitRawMeshData? Mesh,
	UnitBoneInfo? BoneInfo,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Mesh is not null && BoneInfo is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalStaticMeshBinder
{
	public CanonicalStaticMeshBindResult TryBind(
		UnitMeshModel target,
		UnitRawMeshData targetMesh,
		UnitRawMeshData staticMesh,
		UnitStreamInfo targetStream,
		CanonicalBoneAnchor? anchor = null)
	{
		ArgumentNullException.ThrowIfNull(target);
		ArgumentNullException.ThrowIfNull(targetMesh);
		ArgumentNullException.ThrowIfNull(staticMesh);
		ArgumentNullException.ThrowIfNull(targetStream);
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		if (targetMesh.LodIndex < 0 || targetMesh.LodIndex >= target.BoneInfos.Count)
			diagnostics.Add(new("TargetBoneInfoMissing", "A static Canonical placement requires one writable target BoneInfo/Lod."));
		var targetInfo = target.Meshes.SingleOrDefault(mesh => mesh.Index == targetMesh.MeshInfoIndex);
		if (targetInfo is null || targetInfo.TransformIndex >= target.TransformNameHashes.Count || targetInfo.TransformIndex >= target.TransformInfo.Matrices.Count)
			diagnostics.Add(new("StaticAnchorUnavailable", "The target mesh transform cannot be used as a Canonical static anchor."));
		var indexComponent = targetStream.Components.SingleOrDefault(component => component.Type == 6 && component.Index == 0);
		var weightComponent = targetStream.Components.SingleOrDefault(component => component.Type == 7 && component.Index == 0);
		if (indexComponent is null || weightComponent is null)
			diagnostics.Add(new("StaticTargetSkinningLayoutMissing", "A skinned target stream must contain one bone-index and one bone-weight component."));
		if (diagnostics.Count != 0) return new(null, null, Array.AsReadOnly(diagnostics.ToArray()));

		var resolvedTargetInfo = targetInfo!;
		var resolvedIndexComponent = indexComponent!;
		var resolvedWeightComponent = weightComponent!;
		var materialLayout = CanonicalFinalMaterialLayout.TryCreate(targetMesh);
		if (!materialLayout.IsValid)
			return new(null, null, materialLayout.Diagnostics);
		var anchorHash = anchor?.AvatarBoneHash ?? target.TransformNameHashes[(int)resolvedTargetInfo.TransformIndex];
		var targetTransformIndex = resolvedTargetInfo.TransformIndex;
		var anchorIndex = IndexOf(target.TransformNameHashes, anchorHash);
		if (anchorIndex < 0 || anchorIndex >= target.TransformInfo.Matrices.Count)
			return new(null, null, [new("StaticAnchorMissing", $"Static Canonical anchor bone 0x{anchorHash:x8} is absent from target TransformInfo.")]);
		var materialIndexes = materialLayout.Slots.Select(slot => slot.MaterialOrdinal).ToArray();
		var remapOffset = checked((uint)(4 + materialIndexes.Length * 8));
		var remaps = new List<UnitBoneRemap>(materialIndexes.Length);
		foreach (var materialIndex in materialIndexes)
		{
			remaps.Add(new UnitBoneRemap(checked((int)materialIndex), remapOffset, [0]));
			remapOffset += sizeof(uint);
		}
		var rewritten = staticMesh with
		{
			Vertices = staticMesh.Vertices.Select((vertex, ordinal) => new UnitRawVertexRecord(
				checked((uint)ordinal), Array.Empty<byte>(),
				vertex.Components
					.Where(component => component.Type is not (6 or 7))
					.Append(new UnitVertexComponentValue(6, resolvedIndexComponent.TypeName, resolvedIndexComponent.Format, resolvedIndexComponent.FormatName, 0, [], [0, 0, 0, 0], []))
					.Append(new UnitVertexComponentValue(7, resolvedWeightComponent.TypeName, resolvedWeightComponent.Format, resolvedWeightComponent.FormatName, 0, [1, 0, 0, 0], [], []))
					.ToArray())).ToArray()
		};
		var matrix = BuildInverseJointMatrix(target.TransformInfo.Matrices[(int)anchorIndex], target.TransformInfo.Matrices[(int)targetTransformIndex], diagnostics);
		if (diagnostics.Count != 0) return new(null, null, Array.AsReadOnly(diagnostics.ToArray()));
		var template = target.BoneInfos[targetMesh.LodIndex];
		return new(rewritten, template with { NumBones = 1, RealIndices = [checked((uint)anchorIndex)], Remaps = remaps, BoneMatrices = [matrix!] }, Array.Empty<CanonicalPlanDiagnostic>());
	}

	private static byte[]? BuildInverseJointMatrix(UnitTransformMatrix anchor, UnitTransformMatrix mesh, List<CanonicalPlanDiagnostic> diagnostics)
	{
		if (anchor.Values.Count != 16 || mesh.Values.Count != 16 || anchor.Values.Concat(mesh.Values).Any(value => !float.IsFinite(value)))
		{
			diagnostics.Add(new("InvalidTransformInfoMatrix", "A static Canonical anchor requires finite 4x4 TransformInfo matrices."));
			return null;
		}
		var a = ToMatrix(anchor); var m = ToMatrix(mesh);
		if (!Matrix4x4.Invert(m, out var inverseMesh) || !Matrix4x4.Invert(a * inverseMesh, out var inverseJoint))
		{
			diagnostics.Add(new("NonInvertibleStaticAnchor", "The static Canonical anchor cannot form an inverse-joint matrix."));
			return null;
		}
		return [.. new[] { inverseJoint.M11, inverseJoint.M12, inverseJoint.M13, inverseJoint.M14, inverseJoint.M21, inverseJoint.M22, inverseJoint.M23, inverseJoint.M24, inverseJoint.M31, inverseJoint.M32, inverseJoint.M33, inverseJoint.M34, inverseJoint.M41, inverseJoint.M42, inverseJoint.M43, inverseJoint.M44 }.SelectMany(BitConverter.GetBytes)];
	}

	private static Matrix4x4 ToMatrix(UnitTransformMatrix value)
	{
		var v = value.Values;
		return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}
}