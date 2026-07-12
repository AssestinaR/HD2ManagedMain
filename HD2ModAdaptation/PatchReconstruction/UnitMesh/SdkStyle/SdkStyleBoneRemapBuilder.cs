namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Rebuilds BoneInfo remaps with the same name-hash and skip semantics as HD2SDK BoneInfo.SetRemap.
public sealed class SdkStyleBoneRemapBuilder
{
	public UnitBoneInfo SetRemap(
		UnitBoneInfo boneInfo,
		IReadOnlyList<IReadOnlyList<string>> materialBoneNames,
		IReadOnlyList<uint> transformNameHashes)
	{
		ArgumentNullException.ThrowIfNull(boneInfo);
		ArgumentNullException.ThrowIfNull(materialBoneNames);
		ArgumentNullException.ThrowIfNull(transformNameHashes);

		var realIndices = boneInfo.RealIndices.ToList();
		var remaps = new List<UnitBoneRemap>(materialBoneNames.Count);
		var remapOffset = checked((uint)(4 + materialBoneNames.Count * 8));
		for (var materialIndex = 0; materialIndex < materialBoneNames.Count; materialIndex++)
		{
			var fakeIndices = new List<uint>();
			foreach (var boneName in materialBoneNames[materialIndex])
			{
				var boneHash = ResolveBoneHash(boneName);
				var transformIndex = IndexOf(transformNameHashes, boneHash);
				if (transformIndex < 0)
				{
					continue;
				}

				var realIndexPosition = IndexOf(realIndices, checked((uint)transformIndex));
				if (realIndexPosition < 0)
				{
					realIndexPosition = realIndices.Count;
					realIndices.Add(checked((uint)transformIndex));
				}

				fakeIndices.Add(checked((uint)realIndexPosition));
			}

			remaps.Add(new UnitBoneRemap(materialIndex, remapOffset, fakeIndices));
			remapOffset = checked(remapOffset + (uint)(fakeIndices.Count * sizeof(uint)));
		}

		return boneInfo with
		{
			NumBones = checked((uint)realIndices.Count),
			RealIndices = realIndices,
			Remaps = remaps
		};
	}

	private static uint ResolveBoneHash(string boneName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(boneName);
		return uint.TryParse(boneName, out var numericHash) ? numericHash : SdkStyleMurmurHash.Murmur32(boneName);
	}

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var i = 0; i < values.Count; i++)
		{
			if (values[i] == value)
			{
				return i;
			}
		}

		return -1;
	}
}