using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：实现 Unit RawMesh 实验性重定向，用 source mesh 数据替换 target mesh slot。
// Purpose: Retargets Unit RawMesh data experimentally by replacing a target mesh slot with source mesh data.
public sealed class UnitMeshRetargeter : IUnitMeshRetargeter
{
	private readonly bool allowExperimentalLayoutFallback;
	private readonly bool allowMaterialSectionExpansion;
	private readonly bool propagateSourceMaterials;
	private readonly IReadOnlySet<ulong>? allowedSourceMaterialIds;

	public UnitMeshRetargeter(bool allowExperimentalLayoutFallback = false, bool propagateSourceMaterials = false, IReadOnlySet<ulong>? allowedSourceMaterialIds = null, bool allowMaterialSectionExpansion = false)
	{
		this.allowExperimentalLayoutFallback = allowExperimentalLayoutFallback;
		this.allowMaterialSectionExpansion = allowMaterialSectionExpansion;
		this.propagateSourceMaterials = propagateSourceMaterials;
		this.allowedSourceMaterialIds = allowedSourceMaterialIds;
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

		var materialMap = propagateSourceMaterials ? CreateMaterialMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh, sourceMeshInfoIndex, allowExperimentalLayoutFallback, allowMaterialSectionExpansion, allowedSourceMaterialIds) : null;
		var replacement = CopyRawMeshIntoTargetSlot(targetModel, targetRawMesh, sourceModel, sourceRawMesh, targetStream, allowExperimentalLayoutFallback, allowMaterialSectionExpansion, materialMap);
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

	private static UnitRawMeshData CopyRawMeshIntoTargetSlot(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, UnitStreamInfo targetStream, bool allowExperimentalLayoutFallback, bool allowMaterialSectionExpansion, MaterialSlotMap? materialMap)
	{
		var vertexLimit = allowExperimentalLayoutFallback && targetStream.IndexBufferType != 1
			? ushort.MaxValue + 1
			: sourceRawMesh.Vertices.Count;
		var maxSections = allowExperimentalLayoutFallback && !allowMaterialSectionExpansion && targetRawMesh.Sections.Count > 0
			? Math.Min(sourceRawMesh.Sections.Count, targetRawMesh.Sections.Count)
			: sourceRawMesh.Sections.Count;
		var vertexIndexMap = BuildRetainedVertexIndexMap(sourceRawMesh.Sections.Take(maxSections), vertexLimit, sourceRawMesh.Vertices.Count);
		var sourceSections = sourceRawMesh.Sections.Take(maxSections).ToArray();
		var sections = sourceRawMesh.Sections.Count == 0
			? Array.Empty<UnitRawMeshSectionData>()
			: sourceSections.Select((section, index) => CopySectionIntoTargetSlot(targetRawMesh, section, index, vertexIndexMap, materialMap)).ToArray();
		var triangles = sections.SelectMany(section => section.Triangles).ToArray();
		var boneMap = CreateBoneMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh, sourceSections, sections);
		var vertexMaterialMap = boneMap is null
			? null
			: CreateVertexMaterialMap(sourceSections, sections, vertexIndexMap.Count);
		var vertices = vertexIndexMap
			.Select(pair => sourceRawMesh.Vertices[(int)pair.Key])
			.Select((vertex, index) => new UnitRawVertexRecord((uint)index, BuildTargetVertexData(vertex, targetStream, allowExperimentalLayoutFallback, boneMap, vertexMaterialMap, index), vertex.Components))
			.ToArray();

		return targetRawMesh with
		{
			Sections = sections,
			Triangles = triangles,
			Vertices = vertices
		};
	}

	private static IReadOnlyDictionary<uint, uint> BuildRetainedVertexIndexMap(IEnumerable<UnitRawMeshSectionData> sections, int vertexLimit, int sourceVertexCount)
	{
		var boundedVertexLimit = Math.Min(vertexLimit, sourceVertexCount);
		if (vertexLimit >= ushort.MaxValue + 1)
		{
			return BuildReferencedVertexIndexMap(sections, boundedVertexLimit, sourceVertexCount);
		}

		return Enumerable.Range(0, boundedVertexLimit).ToDictionary(index => (uint)index, index => (uint)index);
	}

	private static IReadOnlyDictionary<uint, uint> BuildReferencedVertexIndexMap(IEnumerable<UnitRawMeshSectionData> sections, int vertexLimit, int sourceVertexCount)
	{
		var map = new Dictionary<uint, uint>();
		foreach (var triangle in sections.SelectMany(section => section.Triangles))
		{
			if (!CanAddTriangle(map, triangle, vertexLimit, sourceVertexCount))
			{
				continue;
			}

			AddVertexIndex(map, triangle.A);
			AddVertexIndex(map, triangle.B);
			AddVertexIndex(map, triangle.C);
		}

		return map;
	}

	private static bool CanAddTriangle(Dictionary<uint, uint> map, UnitTriangleIndices triangle, int vertexLimit, int sourceVertexCount)
	{
		var required = CountNewTriangleVertices(map, triangle, sourceVertexCount);
		return required >= 0 && map.Count + required <= vertexLimit;
	}

	private static int CountNewTriangleVertices(Dictionary<uint, uint> map, UnitTriangleIndices triangle, int sourceVertexCount)
	{
		var required = 0;
		Span<uint> vertices = [triangle.A, triangle.B, triangle.C];
		for (var i = 0; i < vertices.Length; i++)
		{
			var sourceIndex = vertices[i];
			if (sourceIndex >= sourceVertexCount)
			{
				return -1;
			}

			if (map.ContainsKey(sourceIndex) || Contains(vertices[..i], sourceIndex))
			{
				continue;
			}

			required++;
		}

		return required;
	}

	private static bool Contains(ReadOnlySpan<uint> values, uint value)
	{
		foreach (var candidate in values)
		{
			if (candidate == value)
			{
				return true;
			}
		}

		return false;
	}

	private static void AddVertexIndex(Dictionary<uint, uint> map, uint sourceIndex)
	{
		if (map.ContainsKey(sourceIndex))
		{
			return;
		}

		map.Add(sourceIndex, checked((uint)map.Count));
	}

	private static byte[] BuildTargetVertexData(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, bool allowExperimentalLayoutFallback, BoneIndexMap? boneMap, IReadOnlyList<VertexMaterialMapEntry>? vertexMaterialMap, int vertexIndex)
	{
		var vertexMaterial = GetVertexMaterial(vertexMaterialMap, vertexIndex);
		if (!allowExperimentalLayoutFallback)
		{
			return boneMap is null
				? NormalizeVertexData(sourceVertex.Data, targetStream.VertexStride)
				: RewriteCompatibleVertexBoneIndices(sourceVertex, targetStream, boneMap, vertexMaterial);
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
			WriteTargetComponent(data.AsSpan(cursor, size), targetComponent, sourceComponent, boneMap, vertexMaterial);
			cursor += size;
		}

		return data;
	}

	private static UnitVertexComponentValue? FindSourceComponent(IReadOnlyList<UnitVertexComponentValue> sourceComponents, UnitStreamComponentInfo targetComponent)
	{
		return sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type && component.Index == targetComponent.Index)
			?? sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type);
	}

	private static void WriteTargetComponent(Span<byte> destination, UnitStreamComponentInfo targetComponent, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap, VertexMaterialMapEntry vertexMaterial)
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
				WriteIntegerComponent(destination, targetComponent, sourceComponent, boneMap, vertexMaterial);
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

	private static void WriteIntegerComponent(Span<byte> destination, UnitStreamComponentInfo component, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap = null, VertexMaterialMapEntry? vertexMaterial = null)
	{
		if (sourceComponent is not null && sourceComponent.RawData.Length == destination.Length && (component.Type != 6 || boneMap is null))
		{
			sourceComponent.RawData.CopyTo(destination);
			return;
		}

		if (component.FormatName == "vec4_uint32")
		{
			var values = RemapBoneIndices(sourceComponent?.UIntValues ?? Array.Empty<uint>(), boneMap, vertexMaterial);
			WriteUInt32(destination, 0, values.Length > 0 ? values[0] : 0);
			WriteUInt32(destination, 4, values.Length > 1 ? values[1] : 0);
			WriteUInt32(destination, 8, values.Length > 2 ? values[2] : 0);
			WriteUInt32(destination, 12, values.Length > 3 ? values[3] : 0);
		}
		else if (component.FormatName == "vec4_uint8")
		{
			var values = RemapBoneIndices(sourceComponent?.UIntValues ?? Array.Empty<uint>(), boneMap, vertexMaterial);
			destination[0] = values.Length > 0 ? ClampToByte(values[0]) : (byte)0;
			destination[1] = values.Length > 1 ? ClampToByte(values[1]) : (byte)0;
			destination[2] = values.Length > 2 ? ClampToByte(values[2]) : (byte)0;
			destination[3] = values.Length > 3 ? ClampToByte(values[3]) : (byte)0;
		}
	}

	private static byte[] RewriteCompatibleVertexBoneIndices(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, BoneIndexMap boneMap, VertexMaterialMapEntry vertexMaterial)
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
				WriteIntegerComponent(data.AsSpan(cursor, size), component, sourceComponent, boneMap, vertexMaterial);
			}

			cursor += size;
		}

		return data;
	}

	private static uint[] RemapBoneIndices(IReadOnlyList<uint> values, BoneIndexMap? boneMap, VertexMaterialMapEntry? vertexMaterial)
	{
		var remapped = new uint[Math.Max(values.Count, 4)];
		for (var i = 0; i < remapped.Length; i++)
		{
			var value = i < values.Count ? values[i] : 0;
			remapped[i] = boneMap?.TryMap(value, vertexMaterial, out var mapped) == true ? mapped : value;
		}

		return remapped;
	}

	private static BoneIndexMap? CreateBoneMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, IReadOnlyList<UnitRawMeshSectionData> sourceSections, IReadOnlyList<UnitRawMeshSectionData> replacementSections)
	{
		var targetBoneInfo = FindBoneInfoForMesh(targetModel, targetRawMesh);
		var sourceBoneInfo = FindBoneInfoForMesh(sourceModel, sourceRawMesh);
		if (targetBoneInfo is null || sourceBoneInfo is null)
		{
			return null;
		}

		var pairs = sourceSections.Zip(replacementSections, (sourceSection, replacementSection) =>
		{
			var sourceRemap = FindBoneRemap(sourceBoneInfo, sourceSection.MaterialIndex);
			var targetRemap = FindBoneRemap(targetBoneInfo, replacementSection.MaterialIndex);
			return sourceRemap is null || targetRemap is null
				? null
				: new BoneRemapPair((int)sourceSection.MaterialIndex, sourceRemap, targetRemap);
		}).ToArray();
		return pairs.Length == 0 || pairs.Any(pair => pair is null)
			? null
			: new BoneIndexMap(sourceBoneInfo, targetBoneInfo, pairs!);
	}

	private static IReadOnlyList<VertexMaterialMapEntry> CreateVertexMaterialMap(IReadOnlyList<UnitRawMeshSectionData> sourceSections, IReadOnlyList<UnitRawMeshSectionData> replacementSections, int vertexCount)
	{
		var result = Enumerable.Range(0, vertexCount)
			.Select(_ => new VertexMaterialMapEntry(0, 0))
			.ToArray();
		var sectionCount = Math.Min(sourceSections.Count, replacementSections.Count);
		for (var sectionIndex = 0; sectionIndex < sectionCount; sectionIndex++)
		{
			var sourceMaterialIndex = checked((int)sourceSections[sectionIndex].MaterialIndex);
			var targetMaterialIndex = checked((int)replacementSections[sectionIndex].MaterialIndex);
			foreach (var triangle in replacementSections[sectionIndex].Triangles)
			{
				AssignVertexMaterial(result, triangle.A, sourceMaterialIndex, targetMaterialIndex);
				AssignVertexMaterial(result, triangle.B, sourceMaterialIndex, targetMaterialIndex);
				AssignVertexMaterial(result, triangle.C, sourceMaterialIndex, targetMaterialIndex);
			}
		}

		return result;
	}

	private static void AssignVertexMaterial(VertexMaterialMapEntry[] result, uint vertexIndex, int sourceMaterialIndex, int targetMaterialIndex)
	{
		if (vertexIndex < result.Length)
		{
			result[(int)vertexIndex] = new VertexMaterialMapEntry(sourceMaterialIndex, targetMaterialIndex);
		}
	}

	private static VertexMaterialMapEntry GetVertexMaterial(IReadOnlyList<VertexMaterialMapEntry>? vertexMaterialMap, int vertexIndex)
		=> vertexMaterialMap is not null && vertexIndex < vertexMaterialMap.Count
			? vertexMaterialMap[vertexIndex]
			: new VertexMaterialMapEntry(0, 0);

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

	private static UnitRawMeshSectionData CopySectionIntoTargetSlot(UnitRawMeshData targetRawMesh, UnitRawMeshSectionData sourceSection, int sectionIndex, IReadOnlyDictionary<uint, uint> vertexIndexMap, MaterialSlotMap? materialMap)
	{
		var targetSection = sectionIndex < targetRawMesh.Sections.Count ? targetRawMesh.Sections[sectionIndex] : targetRawMesh.Sections.FirstOrDefault();
		var triangles = sourceSection.Triangles
			.Where(triangle => vertexIndexMap.ContainsKey(triangle.A) && vertexIndexMap.ContainsKey(triangle.B) && vertexIndexMap.ContainsKey(triangle.C))
			.Select(triangle => new UnitTriangleIndices(vertexIndexMap[triangle.A], vertexIndexMap[triangle.B], vertexIndexMap[triangle.C]))
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

	private static MaterialSlotMap? CreateMaterialMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, int sourceMeshInfoIndex, bool allowExperimentalLayoutFallback, bool allowMaterialSectionExpansion, IReadOnlySet<ulong>? allowedSourceMaterialIds)
	{
		var targetMeshInfo = targetModel.Meshes.FirstOrDefault(mesh => mesh.Index == targetRawMesh.MeshInfoIndex);
		var sourceMeshInfo = sourceModel.Meshes.FirstOrDefault(mesh => mesh.Index == sourceMeshInfoIndex);
		if (targetMeshInfo is null || targetMeshInfo.MaterialSlotIds.Count == 0)
		{
			return null;
		}

		if (sourceMeshInfo is not null && sourceMeshInfo.MaterialSlotIds.Count > 0 && sourceRawMesh.Sections.Any(section => section.MaterialIndex >= sourceMeshInfo.MaterialSlotIds.Count))
		{
			return null;
		}

		var copiedSourceSections = allowExperimentalLayoutFallback && !allowMaterialSectionExpansion && targetRawMesh.Sections.Count > 0
			? sourceRawMesh.Sections.Take(targetRawMesh.Sections.Count)
			: sourceRawMesh.Sections;
		var sourceSlots = (sourceMeshInfo?.MaterialSlotIds.Count > 0 && targetMeshInfo.MaterialSlotIds.Count >= sourceMeshInfo.MaterialSlotIds.Count)
			? sourceMeshInfo!.MaterialSlotIds
			: copiedSourceSections.Select(section => section.MaterialSlotId).Distinct().ToArray();
		if (sourceSlots.Count == 0)
		{
			return null;
		}

		var sourceBindingGroups = sourceModel.Materials
			.Where(binding => sourceSlots.Contains(binding.SectionId))
			.GroupBy(binding => binding.SectionId)
			.ToArray();
		if (sourceBindingGroups.Any(group => group.Select(binding => binding.MaterialId).Distinct().Count() != 1))
		{
			return null;
		}

		var sourceBindings = sourceBindingGroups.ToDictionary(group => group.Key, group => group.First().MaterialId);
		if (sourceBindings.Count != sourceSlots.Count)
		{
			return null;
		}
		var materialSlots = BuildMaterialSlots(targetModel, targetMeshInfo, sourceSlots, sourceBindings, allowMaterialSectionExpansion);
		if (materialSlots is null)
		{
			return null;
		}

		if (materialSlots.SourceTargetSlots.Distinct().Count() != materialSlots.SourceTargetSlots.Count || sourceSlots.Distinct().Count() != sourceSlots.Count)
		{
			return null;
		}

		var replacements = new List<MaterialSlotReplacement>(sourceSlots.Count);
		for (var i = 0; i < sourceSlots.Count; i++)
		{
			var targetSlot = materialSlots.SourceTargetSlots[i];
			var sourceSlot = sourceSlots[i];
			var sourceMaterialId = sourceBindings[sourceSlot];
			var canUseSourceMaterial = allowedSourceMaterialIds is null || allowedSourceMaterialIds.Contains(sourceMaterialId);
			replacements.Add(new MaterialSlotReplacement(targetSlot, sourceSlot, sourceMaterialId, (uint)i, checked((uint)IndexOf(materialSlots.OutputSlots, targetSlot)), canUseSourceMaterial));
		}

		return new MaterialSlotMap(replacements, materialSlots.OutputSlots);
	}

	private static MaterialSlots? BuildMaterialSlots(UnitMeshModel targetModel, UnitMeshInfo targetMeshInfo, IReadOnlyList<uint> sourceSlots, IReadOnlyDictionary<uint, ulong> sourceBindings, bool allowMaterialSectionExpansion)
	{
		var outputSlots = targetMeshInfo.MaterialSlotIds.ToList();
		var originalSlotCount = outputSlots.Count;
		if (!allowMaterialSectionExpansion && originalSlotCount < sourceSlots.Count)
		{
			return null;
		}

		var targetBindings = targetModel.Materials
			.GroupBy(binding => binding.SectionId)
			.Where(group => group.Count() == 1)
			.ToDictionary(group => group.Key, group => group.Single().MaterialId);
		var sourceTargetSlots = new List<uint>(sourceSlots.Count);
		var usedTargetSlots = new HashSet<uint>();
		foreach (var sourceSlot in sourceSlots)
		{
			var sourceMaterialId = sourceBindings[sourceSlot];
			var matchingSlot = outputSlots.FirstOrDefault(slot => !usedTargetSlots.Contains(slot) && targetBindings.TryGetValue(slot, out var materialId) && materialId == sourceMaterialId);
			if (matchingSlot != 0 || outputSlots.Any(slot => slot == 0 && !usedTargetSlots.Contains(slot) && targetBindings.TryGetValue(slot, out var materialId) && materialId == sourceMaterialId))
			{
				sourceTargetSlots.Add(matchingSlot);
				usedTargetSlots.Add(matchingSlot);
				continue;
			}

			if (!allowMaterialSectionExpansion || (originalSlotCount < sourceSlots.Count && sourceTargetSlots.Count < originalSlotCount))
			{
				var reusableSlot = outputSlots.First(slot => !usedTargetSlots.Contains(slot));
				sourceTargetSlots.Add(reusableSlot);
				usedTargetSlots.Add(reusableSlot);
				continue;
			}

			var addedSlot = FindNextAvailableSlot(targetModel, outputSlots);
			outputSlots.Add(addedSlot);
			targetBindings[addedSlot] = sourceMaterialId;
			sourceTargetSlots.Add(addedSlot);
			usedTargetSlots.Add(addedSlot);
		}

		return new MaterialSlots(sourceTargetSlots, outputSlots);
	}

	private static uint FindNextAvailableSlot(UnitMeshModel targetModel, IReadOnlyCollection<uint> localSlots)
	{
		var usedSlots = targetModel.Meshes.SelectMany(mesh => mesh.MaterialSlotIds)
			.Concat(targetModel.RawMeshData.SelectMany(mesh => mesh.Sections.Select(section => section.MaterialSlotId)))
			.Concat(targetModel.Materials.Select(binding => binding.SectionId))
			.Concat(localSlots)
			.ToHashSet();
		var nextSlot = 0u;
		while (usedSlots.Contains(nextSlot))
		{
			nextSlot++;
		}

		return nextSlot;
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

		throw new InvalidDataException("A material replacement slot is missing from the target material slot table.");
	}

	private static IReadOnlyList<UnitMeshInfo> ApplyMaterialMapToMeshes(IReadOnlyList<UnitMeshInfo> meshes, int targetMeshInfoIndex, MaterialSlotMap materialMap)
	{
		return meshes.Select(mesh => mesh.Index == targetMeshInfoIndex
			? mesh with { MaterialSlotIds = materialMap.BuildMaterialSlotIds(mesh.MaterialSlotIds), Sections = ApplyMaterialMapToSections(mesh.Sections, materialMap) }
			: mesh).ToArray();
	}

	private static IReadOnlyList<UnitMeshSectionInfo> ApplyMaterialMapToSections(IReadOnlyList<UnitMeshSectionInfo> sections, MaterialSlotMap materialMap)
	{
		if (sections.Count == 0)
		{
			return Array.Empty<UnitMeshSectionInfo>();
		}

		return materialMap.Replacements.Count >= sections.Count
			? materialMap.Replacements.Select((replacement, index) =>
		{
			var template = index < sections.Count ? sections[index] : sections[^1];
			return template with
			{
				MaterialIndex = replacement.TargetMaterialIndex,
				MaterialSlotId = replacement.TargetSlotId
			};
			}).ToArray()
			: sections.Select(section => materialMap.TryMap(section.MaterialSlotId, out var materialIndex, out var materialSlotId)
				? section with { MaterialIndex = materialIndex, MaterialSlotId = materialSlotId }
				: section).ToArray();
	}

	private static IReadOnlyList<UnitMaterialBinding> ApplyMaterialMapToBindings(IReadOnlyList<UnitMaterialBinding> bindings, MaterialSlotMap materialMap)
	{
		var updatedBindings = bindings.Select(binding => materialMap.TryReplaceTargetBinding(binding.SectionId, out var replacement)
			? new UnitMaterialBinding(replacement.TargetSlotId, replacement.UseSourceMaterial ? replacement.SourceMaterialId : binding.MaterialId)
			: binding).ToList();
		foreach (var replacement in materialMap.Replacements.Where(replacement => updatedBindings.All(binding => binding.SectionId != replacement.TargetSlotId)))
		{
			updatedBindings.Add(new UnitMaterialBinding(replacement.TargetSlotId, replacement.UseSourceMaterial ? replacement.SourceMaterialId : 0));
		}

		return updatedBindings;
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
		private readonly Dictionary<int, Dictionary<uint, uint>> sourceToTargetByMaterial;
		private readonly Dictionary<uint, uint>? fallbackSourceToTarget;

		public BoneIndexMap(UnitBoneInfo sourceBoneInfo, UnitBoneInfo targetBoneInfo, IReadOnlyList<BoneRemapPair> remapPairs)
		{
			var maps = new Dictionary<int, Dictionary<uint, uint>>();
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

		public bool TryMap(uint sourceIndex, VertexMaterialMapEntry? vertexMaterial, out uint targetIndex)
		{
			var materialIndex = vertexMaterial?.SourceMaterialIndex ?? 0;
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

	private sealed record BoneRemapPair(int SourceMaterialIndex, UnitBoneRemap SourceRemap, UnitBoneRemap TargetRemap);

	private sealed record VertexMaterialMapEntry(int SourceMaterialIndex, int TargetMaterialIndex);

	private sealed record MaterialSlots(IReadOnlyList<uint> SourceTargetSlots, IReadOnlyList<uint> OutputSlots);

	private sealed record MaterialSlotReplacement(uint TargetSlotId, uint SourceSlotId, ulong SourceMaterialId, uint SourceMaterialIndex, uint TargetMaterialIndex, bool UseSourceMaterial);

	private sealed class MaterialSlotMap
	{
		private readonly IReadOnlyList<MaterialSlotReplacement> replacements;
		private readonly Dictionary<uint, MaterialSlotReplacement> sourceSlotLookup;
		private readonly Dictionary<uint, MaterialSlotReplacement> targetSlotLookup;

		private readonly IReadOnlyList<uint> outputSlots;

		public MaterialSlotMap(IReadOnlyList<MaterialSlotReplacement> replacements, IReadOnlyList<uint> outputSlots)
		{
			this.replacements = replacements;
			this.outputSlots = outputSlots;
			sourceSlotLookup = replacements.ToDictionary(replacement => replacement.SourceSlotId);
			targetSlotLookup = replacements.ToDictionary(replacement => replacement.TargetSlotId);
		}

		public bool TryMap(uint sourceSlotId, out uint materialIndex, out uint materialSlotId)
		{
			if (sourceSlotLookup.TryGetValue(sourceSlotId, out var replacement))
			{
				materialIndex = replacement.TargetMaterialIndex;
				materialSlotId = replacement.TargetSlotId;
				return true;
			}

			materialIndex = 0;
			materialSlotId = 0;
			return false;
		}

		public bool TryReplaceTargetBinding(uint targetSlotId, out MaterialSlotReplacement replacement)
			=> targetSlotLookup.TryGetValue(targetSlotId, out replacement!);

		public IReadOnlyList<MaterialSlotReplacement> Replacements => replacements;

		public IReadOnlyList<uint> BuildMaterialSlotIds(IReadOnlyList<uint> existingSlots)
		{
			return outputSlots;
		}
	}
}
