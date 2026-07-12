using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Maps bone indices from source mesh to target mesh based on real bone indices.
// Extracted from StrictUnitMeshTransfer.BoneIndexMap for reusability.
public sealed class BoneRemapper
{
	private readonly Dictionary<uint, Dictionary<uint, uint>> sourceToTargetByMaterial;
	private readonly Dictionary<uint, uint>? fallbackSourceToTarget;

	public BoneRemapper(UnitBoneInfo sourceBoneInfo, UnitBoneInfo targetBoneInfo, IReadOnlyList<BoneRemapPair> remapPairs)
	{
		var maps = new Dictionary<uint, Dictionary<uint, uint>>();
		foreach (var pair in remapPairs)
		{
			var sourceToTarget = new Dictionary<uint, uint>();
			var targetRealToFake = BuildRealToFakeIndex(targetBoneInfo, pair.TargetRemap);
			for (var sourceIndex = 0; sourceIndex < pair.SourceRemap.FakeIndices.Count; sourceIndex++)
			{
				var sourceFakeIndex = pair.SourceRemap.FakeIndices[sourceIndex];
				if (sourceFakeIndex >= sourceBoneInfo.RealIndices.Count)
				{
					continue;
				}

				var realIndex = sourceBoneInfo.RealIndices[(int)sourceFakeIndex];
				if (targetRealToFake.TryGetValue(realIndex, out var targetIndex))
				{
					sourceToTarget[(uint)sourceIndex] = targetIndex;
				}
			}

			maps[pair.SourceMaterialIndex] = sourceToTarget;
		}

		sourceToTargetByMaterial = maps;
		fallbackSourceToTarget = maps.TryGetValue(0, out var materialZeroMap)
			? materialZeroMap
			: maps.Values.FirstOrDefault();
	}

	/// <summary>
	/// Attempts to map a source bone index to a target bone index.
	/// </summary>
	/// <param name="sourceIndex">Source bone index</param>
	/// <param name="materialIndex">Material index to determine which bone remap to use</param>
	/// <param name="targetIndex">Mapped target bone index if found</param>
	/// <returns>True if mapping was found, false otherwise</returns>
	public bool TryMap(uint sourceIndex, uint materialIndex, out uint targetIndex)
	{
		if (sourceToTargetByMaterial.TryGetValue(materialIndex, out var materialMap) && materialMap.TryGetValue(sourceIndex, out targetIndex))
		{
			return true;
		}

		if (fallbackSourceToTarget is not null && fallbackSourceToTarget.TryGetValue(sourceIndex, out targetIndex))
		{
			return true;
		}

		targetIndex = 0;
		return false;
	}

	private static Dictionary<uint, uint> BuildRealToFakeIndex(UnitBoneInfo boneInfo, UnitBoneRemap remap)
	{
		var result = new Dictionary<uint, uint>();
		for (var targetIndex = 0; targetIndex < remap.FakeIndices.Count; targetIndex++)
		{
			var fakeIndex = remap.FakeIndices[targetIndex];
			if (fakeIndex >= boneInfo.RealIndices.Count)
			{
				continue;
			}

			result.TryAdd(boneInfo.RealIndices[(int)fakeIndex], (uint)targetIndex);
		}

		return result;
	}
}

public sealed record BoneRemapPair(uint SourceMaterialIndex, UnitBoneRemap SourceRemap, UnitBoneRemap TargetRemap);
