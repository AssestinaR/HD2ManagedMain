using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Expands a cross-armor target TransformInfo from the authoritative avatar rig and recomputes target-relative inverse-joint matrices.
public sealed class CrossArmorTransformInfoExpander
{
	private const float ActiveWeightThreshold = 0.001f;

	public UnitMeshModel Expand(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex, UnitTransformInfo avatar)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);
		ArgumentNullException.ThrowIfNull(avatar);
		ValidateTransformInfo(avatar, "avatar");
		ValidateTransformInfo(targetModel.TransformInfo, "target");
		var requiredHashes = CollectSourceBoneHashes(sourceModel, sourceMeshInfoIndex);
		var expanded = ExpandTransformInfo(targetModel.TransformInfo, requiredHashes, avatar);
		var mesh = targetModel.Meshes.FirstOrDefault(item => item.Index == targetMeshInfoIndex)
			?? throw new KeyNotFoundException($"The target Unit does not contain MeshInfo {targetMeshInfoIndex}.");
		if (mesh.TransformIndex >= expanded.Matrices.Count) throw new InvalidDataException("The target mesh transform index is absent from expanded TransformInfo.");
		var meshMatrix = ToMatrix(expanded.Matrices[(int)mesh.TransformIndex]);
		if (!Matrix4x4.Invert(meshMatrix, out var inverseMesh)) throw new InvalidDataException("The target mesh TransformInfo matrix is not invertible.");
		var hashByIndex = expanded.NameHashes;
		var matrixByHash = expanded.NameHashes.Select((hash, index) => (hash, matrix: expanded.Matrices[index])).ToDictionary(item => item.hash, item => item.matrix);
		var boneInfos = targetModel.BoneInfos.Select(info => info with
		{
			BoneMatrices = info.RealIndices.Select(index =>
			{
				if (index >= hashByIndex.Count) throw new InvalidDataException($"BoneInfo transform index {index} is absent from expanded TransformInfo.");
				var boneWorld = ToMatrix(matrixByHash[hashByIndex[(int)index]]);
				var relative = boneWorld * inverseMesh;
				if (!Matrix4x4.Invert(relative, out var inverseRelative)) throw new InvalidDataException($"Bone transform 0x{hashByIndex[(int)index]:x8} is not invertible relative to the target mesh.");
				return Serialize(inverseRelative);
			}).ToArray()
		}).ToArray();
		return targetModel with { TransformInfo = expanded, TransformNameHashes = expanded.NameHashes, BoneInfos = boneInfos };
	}

	private static UnitTransformInfo ExpandTransformInfo(UnitTransformInfo target, IReadOnlyList<uint> requiredHashes, UnitTransformInfo avatar)
	{
		var locals = target.LocalTransforms.ToList();
		var matrices = target.Matrices.ToList();
		var entries = target.Entries.ToList();
		var hashes = target.NameHashes.ToList();
		var avatarByHash = avatar.NameHashes.Select((hash, index) => (hash, index)).ToDictionary(item => item.hash, item => item.index);
		var targetByHash = hashes.Select((hash, index) => (hash, index)).ToDictionary(item => item.hash, item => item.index);

		int EnsureHash(uint hash, HashSet<uint> visiting)
		{
			if (targetByHash.TryGetValue(hash, out var existing)) return existing;
			if (!avatarByHash.TryGetValue(hash, out var avatarIndex)) throw new InvalidDataException($"The authoritative avatar TransformInfo does not contain required bone hash 0x{hash:x8}.");
			if (!visiting.Add(hash)) throw new InvalidDataException($"The avatar TransformInfo contains a parent cycle at bone hash 0x{hash:x8}.");
			var avatarEntry = avatar.Entries[avatarIndex];
			var parentIndex = 0;
			if (avatarEntry.ParentIndex != avatarIndex)
			{
				if (avatarEntry.ParentIndex >= avatar.NameHashes.Count) throw new InvalidDataException($"Avatar bone hash 0x{hash:x8} has an invalid parent index {avatarEntry.ParentIndex}.");
				parentIndex = EnsureHash(avatar.NameHashes[avatarEntry.ParentIndex], visiting);
			}
			visiting.Remove(hash);
			var next = hashes.Count;
			if (next > ushort.MaxValue) throw new InvalidDataException("Expanded TransformInfo exceeds the uint16 parent-index limit.");
			hashes.Add(hash);
			locals.Add(avatar.LocalTransforms[avatarIndex]);
			matrices.Add(avatar.Matrices[avatarIndex]);
			entries.Add(new UnitTransformEntry(avatarEntry.Increment, checked((ushort)parentIndex)));
			targetByHash.Add(hash, next);
			return next;
		}

		foreach (var hash in requiredHashes) EnsureHash(hash, new HashSet<uint>());
		return new UnitTransformInfo(target.Reserved0, target.Reserved1, target.Reserved2, locals, matrices, entries, hashes);
	}

	private static IReadOnlyList<uint> CollectSourceBoneHashes(UnitMeshModel source, int meshInfoIndex)
	{
		var mesh = source.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == meshInfoIndex) ?? throw new KeyNotFoundException($"The source Unit does not contain mesh {meshInfoIndex}.");
		if (source.BoneInfos.Count == 0) return Array.Empty<uint>();
		var info = source.BoneInfos[mesh.LodIndex >= 0 && mesh.LodIndex < source.BoneInfos.Count ? mesh.LodIndex : 0];
		var result = new HashSet<uint>();
		foreach (var section in mesh.Sections)
		{
			if (section.MaterialIndex >= info.Remaps.Count) throw new InvalidDataException("A source section has no BoneInfo remap.");
			var remap = info.Remaps[(int)section.MaterialIndex];
			foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
			{
				if (vertexIndex >= mesh.Vertices.Count) throw new InvalidDataException("A source section references a vertex outside the source mesh.");
				var vertex = mesh.Vertices[(int)vertexIndex];
				var indices = vertex.Components.FirstOrDefault(component => component.Type == 6)?.UIntValues ?? Array.Empty<uint>();
				var weights = vertex.Components.FirstOrDefault(component => component.Type == 7)?.FloatValues ?? Array.Empty<float>();
				for (var influence = 0; influence < indices.Length; influence++)
				{
					if (influence < weights.Length && weights[influence] <= ActiveWeightThreshold) continue;
					if (indices[influence] >= remap.FakeIndices.Count) throw new InvalidDataException("A source vertex bone index is outside its source material remap.");
					var realPosition = remap.FakeIndices[(int)indices[influence]];
					if (realPosition >= info.RealIndices.Count) throw new InvalidDataException("A source BoneInfo remap points outside its real-index table.");
					var transformIndex = info.RealIndices[(int)realPosition];
					if (transformIndex >= source.TransformNameHashes.Count) throw new InvalidDataException("A source BoneInfo real index is absent from TransformInfo.");
					result.Add(source.TransformNameHashes[(int)transformIndex]);
				}
			}
		}
		return result.OrderBy(hash => hash).ToArray();
	}

	private static void ValidateTransformInfo(UnitTransformInfo info, string role)
	{
		var count = info.NameHashes.Count;
		if (info.LocalTransforms.Count != count || info.Matrices.Count != count || info.Entries.Count != count) throw new InvalidDataException($"The {role} TransformInfo arrays have inconsistent counts.");
		if (info.NameHashes.Distinct().Count() != count) throw new InvalidDataException($"The {role} TransformInfo contains duplicate name hashes.");
	}

	private static Matrix4x4 ToMatrix(UnitTransformMatrix matrix)
	{
		var v = matrix.Values;
		if (v.Count != 16) throw new InvalidDataException("A TransformInfo matrix does not contain 16 floats.");
		return new Matrix4x4(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7], v[8], v[9], v[10], v[11], v[12], v[13], v[14], v[15]);
	}

	private static byte[] Serialize(Matrix4x4 matrix)
	{
		var values = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
		var data = new byte[64];
		for (var i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(data, i * 4);
		return data;
	}
}