using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：实现 Unit RawMesh 实验性重定向，用 source mesh 数据替换 target mesh slot。
// Purpose: Retargets Unit RawMesh data experimentally by replacing a target mesh slot with source mesh data.
public sealed class UnitMeshRetargeter : IUnitMeshRetargeter
{
	private readonly bool allowExperimentalLayoutFallback;
	private readonly bool propagateSourceMaterials;

	public UnitMeshRetargeter(bool allowExperimentalLayoutFallback = false, bool propagateSourceMaterials = false)
	{
		this.allowExperimentalLayoutFallback = allowExperimentalLayoutFallback;
		this.propagateSourceMaterials = propagateSourceMaterials;
	}

	public UnitMeshModel ReplaceRawMesh(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex)
	{
		var targetRawMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceRawMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var targetStream = FindStream(targetModel, targetRawMesh, "target");
		var sourceStream = FindStream(sourceModel, sourceRawMesh, "source");
		if (!allowExperimentalLayoutFallback)
		{
			EnsureCompatibleStreamLayout(targetStream, sourceStream);
		}

		var materialMap = propagateSourceMaterials ? CreateMaterialMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh) : null;
		var replacement = CopyRawMeshIntoTargetSlot(targetModel, targetRawMesh, sourceModel, sourceRawMesh, targetStream, allowExperimentalLayoutFallback, materialMap);
		var rawMeshes = targetModel.RawMeshData
			.Select(mesh => mesh.MeshInfoIndex == targetMeshInfoIndex ? replacement : mesh)
			.ToArray();
		var meshes = materialMap is null
			? targetModel.Meshes
			: ApplyMaterialMapToMeshes(targetModel.Meshes, targetMeshInfoIndex, materialMap);
		var materials = materialMap is null
			? targetModel.Materials
			: ApplyMaterialMapToBindings(targetModel.Materials, materialMap);

		return targetModel with { Meshes = meshes, Materials = materials, RawMeshData = rawMeshes };
	}

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
	{
		return model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain RawMeshData for MeshInfoIndex {meshInfoIndex}.");
	}

	private static UnitStreamInfo FindStream(UnitMeshModel model, UnitRawMeshData rawMesh, string role)
	{
		return model.Streams.FirstOrDefault(stream => stream.Index == rawMesh.StreamIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain stream {rawMesh.StreamIndex}.");
	}

	private static void EnsureCompatibleStreamLayout(UnitStreamInfo targetStream, UnitStreamInfo sourceStream)
	{
		if (targetStream.VertexStride != sourceStream.VertexStride)
		{
			throw new InvalidDataException("Cannot retarget Unit RawMesh because source and target vertex strides differ.");
		}

		if (targetStream.Components.Count != sourceStream.Components.Count)
		{
			throw new InvalidDataException("Cannot retarget Unit RawMesh because source and target component counts differ.");
		}

		for (var i = 0; i < targetStream.Components.Count; i++)
		{
			var target = targetStream.Components[i];
			var source = sourceStream.Components[i];
			if (target.Type != source.Type || target.Format != source.Format || target.Index != source.Index || target.Size != source.Size)
			{
				throw new InvalidDataException("Cannot retarget Unit RawMesh because source and target component layouts differ.");
			}
		}
	}

	private static UnitRawMeshData CopyRawMeshIntoTargetSlot(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, UnitStreamInfo targetStream, bool allowExperimentalLayoutFallback, MaterialSlotMap? materialMap)
	{
		var maxVertices = allowExperimentalLayoutFallback && targetStream.IndexBufferType != 1
			? Math.Min(sourceRawMesh.Vertices.Count, ushort.MaxValue + 1)
			: sourceRawMesh.Vertices.Count;
		var maxSections = allowExperimentalLayoutFallback && targetRawMesh.Sections.Count > 0
			? Math.Min(sourceRawMesh.Sections.Count, targetRawMesh.Sections.Count)
			: sourceRawMesh.Sections.Count;
		var sections = sourceRawMesh.Sections.Count == 0
			? Array.Empty<UnitRawMeshSectionData>()
			: sourceRawMesh.Sections.Take(maxSections).Select((section, index) => CopySectionIntoTargetSlot(targetRawMesh, section, index, maxVertices, materialMap)).ToArray();
		var triangles = sections.SelectMany(section => section.Triangles).ToArray();
		var boneMap = CreateBoneMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh);
		var vertices = sourceRawMesh.Vertices
			.Take(maxVertices)
			.Select((vertex, index) => new UnitRawVertexRecord((uint)index, BuildTargetVertexData(vertex, targetStream, allowExperimentalLayoutFallback, boneMap), vertex.Components))
			.ToArray();

		return targetRawMesh with
		{
			Sections = sections,
			Triangles = triangles,
			Vertices = vertices
		};
	}

	private static byte[] BuildTargetVertexData(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, bool allowExperimentalLayoutFallback, BoneIndexMap? boneMap)
	{
		if (!allowExperimentalLayoutFallback)
		{
			return boneMap is null
				? NormalizeVertexData(sourceVertex.Data, targetStream.VertexStride)
				: RewriteCompatibleVertexBoneIndices(sourceVertex, targetStream, boneMap);
		}

		var data = new byte[checked((int)targetStream.VertexStride)];
		var cursor = 0;
		foreach (var targetComponent in targetStream.Components)
		{
			var size = checked((int)targetComponent.Size);
			if (size <= 0 || cursor + size > data.Length)
			{
				continue;
			}

			var sourceComponent = FindSourceComponent(sourceVertex.Components, targetComponent);
			WriteTargetComponent(data.AsSpan(cursor, size), targetComponent, sourceComponent, boneMap);
			cursor += size;
		}

		return data;
	}

	private static UnitVertexComponentValue? FindSourceComponent(IReadOnlyList<UnitVertexComponentValue> sourceComponents, UnitStreamComponentInfo targetComponent)
	{
		return sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type && component.Index == targetComponent.Index)
			?? sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type);
	}

	private static void WriteTargetComponent(Span<byte> destination, UnitStreamComponentInfo targetComponent, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap)
	{
		if (sourceComponent is not null && sourceComponent.Format == targetComponent.Format && sourceComponent.RawData.Length == destination.Length && (targetComponent.Type != 6 || boneMap is null))
		{
			sourceComponent.RawData.CopyTo(destination);
			return;
		}

		switch (targetComponent.Type)
		{
			case 0:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [0f, 0f, 0f, 1f]));
				break;
			case 1:
			case 2:
			case 3:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [0f, 0f, 1f, 1f]));
				break;
			case 4:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [0f, 0f, 0f, 1f]));
				break;
			case 5:
				WriteColorComponent(destination, targetComponent, sourceComponent);
				break;
			case 6:
				WriteIntegerComponent(destination, targetComponent, sourceComponent, boneMap);
				break;
			case 7:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [1f, 0f, 0f, 0f]));
				break;
		}
	}

	private static IReadOnlyList<float> GetFloatValues(UnitVertexComponentValue? sourceComponent, IReadOnlyList<float> fallback)
	{
		return sourceComponent?.FloatValues.Length > 0 ? sourceComponent.FloatValues : fallback;
	}

	private static void WriteFloatComponent(Span<byte> destination, UnitStreamComponentInfo component, IReadOnlyList<float> values)
	{
		switch (component.FormatName)
		{
			case "float":
				WriteSingle(destination, 0, GetValue(values, 0));
				break;
			case "vec2_float":
				WriteSingle(destination, 0, GetValue(values, 0));
				WriteSingle(destination, 4, GetValue(values, 1));
				break;
			case "vec3_float":
				WriteSingle(destination, 0, GetValue(values, 0));
				WriteSingle(destination, 4, GetValue(values, 1));
				WriteSingle(destination, 8, GetValue(values, 2));
				break;
			case "vec4_float":
				WriteSingle(destination, 0, GetValue(values, 0));
				WriteSingle(destination, 4, GetValue(values, 1));
				WriteSingle(destination, 8, GetValue(values, 2));
				WriteSingle(destination, 12, GetValue(values, 3));
				break;
			case "vec4_1010102":
				WriteUInt32(destination, 0, EncodeTenBitUnsigned(values));
				break;
			case "unk_normal":
				WriteUInt32(destination, 0, EncodePackedOctNormal(values));
				break;
			case "vec2_half":
				WriteHalf(destination, 0, GetValue(values, 0));
				WriteHalf(destination, 2, GetValue(values, 1));
				break;
			case "vec4_half":
				WriteHalf(destination, 0, GetValue(values, 0));
				WriteHalf(destination, 2, GetValue(values, 1));
				WriteHalf(destination, 4, GetValue(values, 2));
				WriteHalf(destination, 6, GetValue(values, 3));
				break;
		}
	}

	private static void WriteColorComponent(Span<byte> destination, UnitStreamComponentInfo component, UnitVertexComponentValue? sourceComponent)
	{
		if (component.FormatName is not ("rgba_r8g8b8a8" or "vec4_uint8"))
		{
			return;
		}

		if (sourceComponent?.UIntValues.Length >= 4)
		{
			destination[0] = ClampToByte(sourceComponent.UIntValues[0]);
			destination[1] = ClampToByte(sourceComponent.UIntValues[1]);
			destination[2] = ClampToByte(sourceComponent.UIntValues[2]);
			destination[3] = ClampToByte(sourceComponent.UIntValues[3]);
			return;
		}

		destination[0] = 255;
		destination[1] = 255;
		destination[2] = 255;
		destination[3] = 255;
	}

	private static void WriteIntegerComponent(Span<byte> destination, UnitStreamComponentInfo component, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap = null)
	{
		if (sourceComponent is not null && sourceComponent.RawData.Length == destination.Length && (component.Type != 6 || boneMap is null))
		{
			sourceComponent.RawData.CopyTo(destination);
			return;
		}

		if (component.FormatName == "vec4_uint32")
		{
			var values = RemapBoneIndices(sourceComponent?.UIntValues ?? Array.Empty<uint>(), boneMap);
			WriteUInt32(destination, 0, values.Length > 0 ? values[0] : 0);
			WriteUInt32(destination, 4, values.Length > 1 ? values[1] : 0);
			WriteUInt32(destination, 8, values.Length > 2 ? values[2] : 0);
			WriteUInt32(destination, 12, values.Length > 3 ? values[3] : 0);
		}
		else if (component.FormatName == "vec4_uint8")
		{
			var values = RemapBoneIndices(sourceComponent?.UIntValues ?? Array.Empty<uint>(), boneMap);
			destination[0] = values.Length > 0 ? ClampToByte(values[0]) : (byte)0;
			destination[1] = values.Length > 1 ? ClampToByte(values[1]) : (byte)0;
			destination[2] = values.Length > 2 ? ClampToByte(values[2]) : (byte)0;
			destination[3] = values.Length > 3 ? ClampToByte(values[3]) : (byte)0;
		}
	}

	private static byte[] RewriteCompatibleVertexBoneIndices(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, BoneIndexMap boneMap)
	{
		var data = NormalizeVertexData(sourceVertex.Data, targetStream.VertexStride);
		var cursor = 0;
		foreach (var component in targetStream.Components)
		{
			var size = checked((int)component.Size);
			if (size <= 0 || cursor + size > data.Length)
			{
				continue;
			}

			if (component.Type == 6)
			{
				var sourceComponent = FindSourceComponent(sourceVertex.Components, component);
				WriteIntegerComponent(data.AsSpan(cursor, size), component, sourceComponent, boneMap);
			}

			cursor += size;
		}

		return data;
	}

	private static uint[] RemapBoneIndices(IReadOnlyList<uint> values, BoneIndexMap? boneMap)
	{
		var remapped = new uint[Math.Max(values.Count, 4)];
		for (var i = 0; i < remapped.Length; i++)
		{
			var value = i < values.Count ? values[i] : 0;
			remapped[i] = boneMap?.TryMap(value, out var mapped) == true ? mapped : value;
		}

		return remapped;
	}

	private static BoneIndexMap? CreateBoneMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh)
	{
		var targetBoneInfo = FindBoneInfoForMesh(targetModel, targetRawMesh);
		var sourceBoneInfo = FindBoneInfoForMesh(sourceModel, sourceRawMesh);
		if (targetBoneInfo is null || sourceBoneInfo is null)
		{
			return null;
		}

		var targetMaterialIndex = targetRawMesh.Sections.FirstOrDefault()?.MaterialIndex ?? 0;
		var sourceMaterialIndex = sourceRawMesh.Sections.FirstOrDefault()?.MaterialIndex ?? 0;
		var targetRemap = FindBoneRemap(targetBoneInfo, targetMaterialIndex);
		var sourceRemap = FindBoneRemap(sourceBoneInfo, sourceMaterialIndex);
		return targetRemap is null || sourceRemap is null
			? null
			: new BoneIndexMap(sourceBoneInfo, sourceRemap, targetBoneInfo, targetRemap);
	}

	private static UnitBoneInfo? FindBoneInfoForMesh(UnitMeshModel model, UnitRawMeshData mesh)
	{
		if (model.BoneInfos.Count == 0)
		{
			return null;
		}

		var index = mesh.LodIndex < 0 ? 0 : mesh.LodIndex;
		return index < model.BoneInfos.Count ? model.BoneInfos[index] : model.BoneInfos[0];
	}

	private static UnitBoneRemap? FindBoneRemap(UnitBoneInfo boneInfo, uint materialIndex)
	{
		return materialIndex < boneInfo.Remaps.Count ? boneInfo.Remaps[(int)materialIndex] : null;
	}

	private static byte[] NormalizeVertexData(byte[] sourceData, uint targetStride)
	{
		var data = new byte[checked((int)targetStride)];
		Array.Copy(sourceData, data, Math.Min(sourceData.Length, data.Length));
		return data;
	}

	private static UnitRawMeshSectionData CopySectionIntoTargetSlot(UnitRawMeshData targetRawMesh, UnitRawMeshSectionData sourceSection, int sectionIndex, int maxVertices, MaterialSlotMap? materialMap)
	{
		var targetSection = sectionIndex < targetRawMesh.Sections.Count ? targetRawMesh.Sections[sectionIndex] : targetRawMesh.Sections.FirstOrDefault();
		var triangles = sourceSection.Triangles
			.Where(triangle => triangle.A < maxVertices && triangle.B < maxVertices && triangle.C < maxVertices)
			.Select(triangle => new UnitTriangleIndices(triangle.A, triangle.B, triangle.C))
			.ToArray();
		if (materialMap?.TryMap(sourceSection.MaterialSlotId, out var materialIndex, out var materialSlotId) == true)
		{
			return new UnitRawMeshSectionData(materialIndex, materialSlotId, triangles);
		}

		return new UnitRawMeshSectionData(
			targetSection?.MaterialIndex ?? 0,
			targetSection?.MaterialSlotId ?? 0,
			triangles);
	}

	private static MaterialSlotMap? CreateMaterialMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh)
	{
		var targetMeshInfo = targetModel.Meshes.FirstOrDefault(mesh => mesh.Index == targetRawMesh.MeshInfoIndex);
		var sourceMeshInfo = sourceModel.Meshes.FirstOrDefault(mesh => mesh.Index == sourceRawMesh.MeshInfoIndex);
		if (targetMeshInfo is null || targetMeshInfo.MaterialSlotIds.Count == 0)
		{
			return null;
		}

		var sourceSlots = (sourceMeshInfo?.MaterialSlotIds.Count > 0 ? sourceMeshInfo.MaterialSlotIds : sourceRawMesh.Sections.Select(section => section.MaterialSlotId))
			.Distinct()
			.ToArray();
		if (sourceSlots.Length == 0 || sourceSlots.Length > targetMeshInfo.MaterialSlotIds.Count)
		{
			return null;
		}

		var sourceBindings = sourceModel.Materials
			.Where(binding => sourceSlots.Contains(binding.SectionId))
			.ToDictionary(binding => binding.SectionId, binding => binding.MaterialId);
		if (sourceBindings.Count != sourceSlots.Length)
		{
			return null;
		}

		var replacements = new List<MaterialSlotReplacement>(sourceSlots.Length);
		for (var i = 0; i < sourceSlots.Length; i++)
		{
			var targetSlot = targetMeshInfo.MaterialSlotIds[i];
			var sourceSlot = sourceSlots[i];
			replacements.Add(new MaterialSlotReplacement(targetSlot, sourceSlot, sourceBindings[sourceSlot], (uint)i));
		}

		return new MaterialSlotMap(replacements);
	}

	private static IReadOnlyList<UnitMeshInfo> ApplyMaterialMapToMeshes(IReadOnlyList<UnitMeshInfo> meshes, int targetMeshInfoIndex, MaterialSlotMap materialMap)
	{
		return meshes.Select(mesh => mesh.Index == targetMeshInfoIndex
			? mesh with { MaterialSlotIds = materialMap.BuildMaterialSlotIds(mesh.MaterialSlotIds), Sections = ApplyMaterialMapToSections(mesh.Sections, materialMap) }
			: mesh).ToArray();
	}

	private static IReadOnlyList<UnitMeshSectionInfo> ApplyMaterialMapToSections(IReadOnlyList<UnitMeshSectionInfo> sections, MaterialSlotMap materialMap)
	{
		return sections.Select(section => materialMap.TryMap(section.MaterialSlotId, out var materialIndex, out var materialSlotId)
			? section with { MaterialIndex = materialIndex, MaterialSlotId = materialSlotId }
			: section).ToArray();
	}

	private static IReadOnlyList<UnitMaterialBinding> ApplyMaterialMapToBindings(IReadOnlyList<UnitMaterialBinding> bindings, MaterialSlotMap materialMap)
	{
		return bindings.Select(binding => materialMap.TryReplaceTargetBinding(binding.SectionId, out var replacement)
			? new UnitMaterialBinding(replacement.SourceSlotId, replacement.SourceMaterialId)
			: binding).ToArray();
	}

	private static float GetValue(IReadOnlyList<float> values, int index) => index < values.Count ? values[index] : 0f;

	private static uint EncodeTenBitUnsigned(IReadOnlyList<float> values)
	{
		var x = ClampToBits(GetValue(values, 0), 1023);
		var y = ClampToBits(GetValue(values, 1), 1023);
		var z = ClampToBits(GetValue(values, 2), 1023);
		var w = ClampToBits(GetValue(values, 3), 3);
		return x | (y << 10) | (z << 20) | (w << 30);
	}

	private static uint EncodePackedOctNormal(IReadOnlyList<float> values)
	{
		var x = GetValue(values, 0);
		var y = GetValue(values, 1);
		var z = values.Count > 2 ? values[2] : 1f;
		var length = MathF.Sqrt(x * x + y * y + z * z);
		if (length > 0)
		{
			x /= length;
			y /= length;
			z /= length;
		}

		var l1 = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
		if (l1 > 0)
		{
			x /= l1;
			y /= l1;
		}

		if (z < 0)
		{
			var oldX = x;
			x = (1f - Math.Abs(y)) * Math.Sign(oldX == 0 ? 1f : oldX);
			y = (1f - Math.Abs(oldX)) * Math.Sign(y == 0 ? 1f : y);
		}

		var encodedX = ClampToBits((x + 1f) * 0.5f, 1023);
		var encodedY = ClampToBits((y + 1f) * 0.5f, 1023);
		return encodedX | (encodedY << 10);
	}

	private static uint ClampToBits(float value, uint max)
		=> (uint)Math.Clamp((int)MathF.Round(value * max), 0, (int)max);

	private static byte ClampToByte(uint value) => (byte)Math.Min(byte.MaxValue, value);

	private static void WriteSingle(Span<byte> data, int offset, float value) => WriteUInt32(data, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

	private static void WriteHalf(Span<byte> data, int offset, float value)
	{
		var bits = BitConverter.HalfToUInt16Bits((Half)value);
		data[offset] = (byte)bits;
		data[offset + 1] = (byte)(bits >> 8);
	}

	private static void WriteUInt32(Span<byte> data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}

	private sealed class BoneIndexMap
	{
		private readonly Dictionary<uint, uint> sourceToTarget = new();

		public BoneIndexMap(UnitBoneInfo sourceBoneInfo, UnitBoneRemap sourceRemap, UnitBoneInfo targetBoneInfo, UnitBoneRemap targetRemap)
		{
			var targetRealToFake = BuildRealToFakeIndex(targetBoneInfo, targetRemap);
			for (var sourceIndex = 0; sourceIndex < sourceRemap.FakeIndices.Count; sourceIndex++)
			{
				var sourceFakeIndex = sourceRemap.FakeIndices[sourceIndex];
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
		}

		public bool TryMap(uint sourceIndex, out uint targetIndex) => sourceToTarget.TryGetValue(sourceIndex, out targetIndex);

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

	private sealed record MaterialSlotReplacement(uint TargetSlotId, uint SourceSlotId, ulong SourceMaterialId, uint SourceMaterialIndex);

	private sealed class MaterialSlotMap
	{
		private readonly IReadOnlyList<MaterialSlotReplacement> replacements;
		private readonly Dictionary<uint, MaterialSlotReplacement> sourceSlotLookup;
		private readonly Dictionary<uint, MaterialSlotReplacement> targetSlotLookup;

		public MaterialSlotMap(IReadOnlyList<MaterialSlotReplacement> replacements)
		{
			this.replacements = replacements;
			sourceSlotLookup = replacements.ToDictionary(replacement => replacement.SourceSlotId);
			targetSlotLookup = replacements.ToDictionary(replacement => replacement.TargetSlotId);
		}

		public bool TryMap(uint sourceSlotId, out uint materialIndex, out uint materialSlotId)
		{
			if (sourceSlotLookup.TryGetValue(sourceSlotId, out var replacement))
			{
				materialIndex = replacement.SourceMaterialIndex;
				materialSlotId = replacement.SourceSlotId;
				return true;
			}

			materialIndex = 0;
			materialSlotId = 0;
			return false;
		}

		public bool TryReplaceTargetBinding(uint targetSlotId, out MaterialSlotReplacement replacement)
			=> targetSlotLookup.TryGetValue(targetSlotId, out replacement!);

		public IReadOnlyList<uint> BuildMaterialSlotIds(IReadOnlyList<uint> existingSlots)
		{
			var slots = existingSlots.ToArray();
			for (var i = 0; i < replacements.Count && i < slots.Length; i++)
			{
				slots[i] = replacements[i].SourceSlotId;
			}

			return slots;
		}
	}
}
