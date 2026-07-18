using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Audits cross-armor source vertices and bone influences before target-shell reconstruction.
public sealed class CrossArmorSkinningDiagnosticAnalyzer
{
	private const float ActiveWeightThreshold = 0.001f;
	private const int MaximumSamples = 32;

	public CrossArmorSkinningDiagnostic Analyze(UnitMeshModel sourceModel, int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(sourceModel);
		var mesh = sourceModel.RawMeshData.FirstOrDefault(item => item.MeshInfoIndex == sourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"The source Unit does not contain mesh {sourceMeshInfoIndex}.");
		var meshInfo = sourceModel.Meshes.FirstOrDefault(item => item.Index == sourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"The source Unit does not contain MeshInfo {sourceMeshInfoIndex}.");
		var boneInfoIndex = sourceModel.BoneInfos.Count == 0 ? -1 : mesh.LodIndex >= 0 && mesh.LodIndex < sourceModel.BoneInfos.Count ? mesh.LodIndex : 0;
		var boneInfo = boneInfoIndex < 0 ? null : sourceModel.BoneInfos[boneInfoIndex];
		var materialByVertex = BuildVertexMaterialMap(mesh);
		var samples = new List<CrossArmorVertexSkinningSample>();
		var activeInfluenceCount = 0;
		var lowWeightInfluenceCount = 0;
		var invalidInfluenceCount = 0;
		var nonFinitePositionCount = 0;
		var zeroWeightVertexCount = 0;
		var min = new Vector3(float.PositiveInfinity);
		var max = new Vector3(float.NegativeInfinity);
		foreach (var vertex in mesh.Vertices)
		{
			var position = FindComponent(vertex, 0)?.FloatValues ?? Array.Empty<float>();
			if (position.Length >= 3)
			{
				var value = new Vector3(position[0], position[1], position[2]);
				if (float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z))
				{
					min = Vector3.Min(min, value);
					max = Vector3.Max(max, value);
				}
				else nonFinitePositionCount++;
			}
			var indices = FindComponent(vertex, 6)?.UIntValues ?? Array.Empty<uint>();
			var weights = FindComponent(vertex, 7)?.FloatValues ?? Array.Empty<float>();
			if (indices.Length == 0 && weights.Length == 0) continue;
			var activeWeight = 0f;
			var influences = new List<CrossArmorBoneInfluenceSample>();
			for (var influence = 0; influence < Math.Max(indices.Length, weights.Length); influence++)
			{
				var weight = influence < weights.Length ? weights[influence] : 0f;
				var fakeIndex = influence < indices.Length ? indices[influence] : 0;
				if (weight <= ActiveWeightThreshold)
				{
					if (weight > 0) lowWeightInfluenceCount++;
					continue;
				}
				activeInfluenceCount++;
				activeWeight += weight;
				var resolved = TryResolveBoneHash(fakeIndex, materialByVertex.GetValueOrDefault(vertex.Index), boneInfo, sourceModel.TransformNameHashes, out var hash, out var transformIndex, out var failure);
				if (!resolved) invalidInfluenceCount++;
				influences.Add(new CrossArmorBoneInfluenceSample(influence, fakeIndex, weight, resolved ? $"0x{hash:x8}" : null, transformIndex, failure));
			}
			if (activeWeight <= ActiveWeightThreshold) zeroWeightVertexCount++;
			if ((influences.Any(item => item.Failure is not null) || activeWeight <= ActiveWeightThreshold || MathF.Abs(activeWeight - 1f) > 0.02f) && samples.Count < MaximumSamples)
			{
				samples.Add(new CrossArmorVertexSkinningSample(vertex.Index, materialByVertex.GetValueOrDefault(vertex.Index), position.Take(3).ToArray(), activeWeight, influences));
			}
		}
		var hasBounds = float.IsFinite(min.X);
		return new CrossArmorSkinningDiagnostic(
			sourceMeshInfoIndex,
			meshInfo.TransformIndex,
			boneInfoIndex,
			mesh.Vertices.Count,
			activeInfluenceCount,
			lowWeightInfluenceCount,
			invalidInfluenceCount,
			zeroWeightVertexCount,
			nonFinitePositionCount,
			hasBounds ? new[] { min.X, min.Y, min.Z } : Array.Empty<float>(),
			hasBounds ? new[] { max.X, max.Y, max.Z } : Array.Empty<float>(),
			samples);
	}

	private static IReadOnlyDictionary<uint, uint> BuildVertexMaterialMap(UnitRawMeshData mesh)
	{
		var result = new Dictionary<uint, uint>();
		foreach (var section in mesh.Sections)
		{
			foreach (var index in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }))
			{
				if (result.TryGetValue(index, out var existing) && existing != section.MaterialIndex) throw new InvalidDataException("A source vertex belongs to multiple material remaps.");
				result[index] = section.MaterialIndex;
			}
		}
		return result;
	}

	private static bool TryResolveBoneHash(uint fakeIndex, uint materialIndex, UnitBoneInfo? boneInfo, IReadOnlyList<uint> transformHashes, out uint hash, out uint? transformIndex, out string? failure)
	{
		hash = 0;
		transformIndex = null;
		failure = null;
		if (boneInfo is null) { failure = "Source Unit has no BoneInfo."; return false; }
		if (materialIndex >= boneInfo.Remaps.Count) { failure = "Source material has no BoneInfo remap."; return false; }
		var remap = boneInfo.Remaps[(int)materialIndex];
		if (fakeIndex >= remap.FakeIndices.Count) { failure = "Bone index is outside the source material remap."; return false; }
		var realPosition = remap.FakeIndices[(int)fakeIndex];
		if (realPosition >= boneInfo.RealIndices.Count) { failure = "Source remap points outside the real-index table."; return false; }
		var index = boneInfo.RealIndices[(int)realPosition];
		transformIndex = index;
		if (index >= transformHashes.Count) { failure = "Source real index is absent from TransformInfo."; return false; }
		hash = transformHashes[(int)index];
		return true;
	}

	private static UnitVertexComponentValue? FindComponent(UnitRawVertexRecord vertex, uint type)
		=> vertex.Components.FirstOrDefault(component => component.Type == type);
}

public sealed record CrossArmorSkinningDiagnostic(
	int SourceMeshInfoIndex,
	uint SourceMeshTransformIndex,
	int SourceBoneInfoIndex,
	int VertexCount,
	int ActiveInfluenceCount,
	int IgnoredLowWeightInfluenceCount,
	int InvalidActiveInfluenceCount,
	int ZeroActiveWeightVertexCount,
	int NonFinitePositionCount,
	IReadOnlyList<float> BoundsMin,
	IReadOnlyList<float> BoundsMax,
	IReadOnlyList<CrossArmorVertexSkinningSample> Samples);

public sealed record CrossArmorVertexSkinningSample(
	uint VertexIndex,
	uint SourceMaterialIndex,
	IReadOnlyList<float> Position,
	float ActiveWeightSum,
	IReadOnlyList<CrossArmorBoneInfluenceSample> Influences);

public sealed record CrossArmorBoneInfluenceSample(
	int InfluenceIndex,
	uint SourceFakeIndex,
	float Weight,
	string? BoneHash,
	uint? SourceTransformIndex,
	string? Failure);
