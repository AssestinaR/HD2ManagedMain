namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Transfers one explicitly selected source mesh into a compatible target Unit shell with source material propagation.
// NOTE: This class is deprecated. Use Processing.MeshTransfer instead, which uses extracted BoneRemapper and MaterialMapper components.
// This class is kept for reference and as a backup fallback.
[Obsolete("Use HD2ModAdaptation.Processing.MeshTransfer instead. This class is kept for reference only.")]
public sealed class StrictUnitMeshTransfer
{
	private readonly bool allowTargetLayoutConversion;

	public StrictUnitMeshTransfer(bool allowTargetLayoutConversion = false)
	{
		this.allowTargetLayoutConversion = allowTargetLayoutConversion;
	}

	public UnitMeshTransferResult Transfer(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);

		var targetRawMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceRawMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var targetStream = FindStream(targetModel, targetRawMesh, "target");
		var sourceStream = FindStream(sourceModel, sourceRawMesh, "source");
		if (!allowTargetLayoutConversion)
		{
			EnsureCompatibleStreamLayout(targetStream, sourceStream);
		}

		var materialMap = CreateMaterialMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh, sourceMeshInfoIndex);
		var vertexIndexMap = BuildRetainedVertexIndexMap(sourceRawMesh.Sections, GetVertexLimit(targetStream, sourceRawMesh), sourceRawMesh.Vertices.Count);
		var replacementSections = CopySections(sourceRawMesh, vertexIndexMap, materialMap);
		var boneMap = CreateBoneMap(targetModel, targetRawMesh, sourceModel, sourceRawMesh, replacementSections);
		var vertices = CopyVertices(sourceRawMesh, targetStream, boneMap, replacementSections, vertexIndexMap, allowTargetLayoutConversion);
		var replacement = targetRawMesh with
		{
			Sections = replacementSections,
			Triangles = replacementSections.SelectMany(section => section.Triangles).ToArray(),
			Vertices = vertices
		};

		var meshes = ApplyMaterialMapToMeshes(targetModel.Meshes, targetMeshInfoIndex, materialMap);
		var materials = ApplyMaterialBindings(targetModel.Materials, materialMap);
		var model = targetModel with
		{
			Meshes = meshes,
			Materials = materials,
			RawMeshData = targetModel.RawMeshData.Select(mesh => mesh.MeshInfoIndex == targetMeshInfoIndex ? replacement : mesh).ToArray()
		};
		return new UnitMeshTransferResult(model, materialMap.Replacements.Select(item => item.SourceMaterialId).Distinct().OrderBy(id => id).ToArray());
	}

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain RawMeshData for MeshInfoIndex {meshInfoIndex}.");

	private static UnitStreamInfo FindStream(UnitMeshModel model, UnitRawMeshData rawMesh, string role)
		=> model.Streams.FirstOrDefault(stream => stream.Index == rawMesh.StreamIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain stream {rawMesh.StreamIndex}.");

	private static UnitMeshInfo FindMeshInfo(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.Meshes.FirstOrDefault(mesh => mesh.Index == meshInfoIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain MeshInfo {meshInfoIndex}.");

	private static void EnsureCompatibleStreamLayout(UnitStreamInfo target, UnitStreamInfo source)
	{
		if (target.VertexStride != source.VertexStride || target.Components.Count != source.Components.Count)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because source and target stream layouts differ.");
		}

		for (var index = 0; index < target.Components.Count; index++)
		{
			var targetComponent = target.Components[index];
			var sourceComponent = source.Components[index];
			if (targetComponent.Type != sourceComponent.Type || targetComponent.Format != sourceComponent.Format || targetComponent.Index != sourceComponent.Index || targetComponent.Size != sourceComponent.Size)
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because source and target stream component layouts differ.");
			}
		}
	}

	private static int GetVertexLimit(UnitStreamInfo targetStream, UnitRawMeshData sourceRawMesh)
		=> targetStream.IndexBufferType == 1 ? sourceRawMesh.Vertices.Count : ushort.MaxValue + 1;

	private static MaterialSlotMap CreateMaterialMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, int sourceMeshInfoIndex)
	{
		var targetMesh = FindMeshInfo(targetModel, targetRawMesh.MeshInfoIndex, "target");
		var sourceMesh = FindMeshInfo(sourceModel, sourceMeshInfoIndex, "source");
		if (sourceRawMesh.Sections.Count == 0 || targetMesh.MaterialSlotIds.Count == 0)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because source or target material sections are empty.");
		}

		if (sourceRawMesh.Sections.Any(section => section.MaterialIndex >= sourceMesh.MaterialSlotIds.Count))
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a source section material index is outside its mesh slot table.");
		}

		var sourceSlots = targetMesh.MaterialSlotIds.Count >= sourceMesh.MaterialSlotIds.Count
			? sourceMesh.MaterialSlotIds.ToArray()
			: sourceRawMesh.Sections.Select(section => section.MaterialSlotId).Distinct().ToArray();
		var sourceBindings = sourceModel.Materials
			.Where(binding => sourceSlots.Contains(binding.SectionId))
			.GroupBy(binding => binding.SectionId)
			.ToDictionary(group => group.Key, group => group.Select(binding => binding.MaterialId).Distinct().ToArray());
		if (sourceSlots.Any(slot => !sourceBindings.TryGetValue(slot, out var materialIds) || materialIds.Length != 1))
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a source material slot does not resolve to exactly one Material asset.");
		}

		var materialSlots = BuildMaterialSlots(targetModel, targetMesh, sourceSlots, sourceBindings.ToDictionary(pair => pair.Key, pair => pair.Value[0]));
		var replacements = new List<MaterialSlotReplacement>(sourceSlots.Length);
		for (var index = 0; index < sourceSlots.Length; index++)
		{
			var sourceSlot = sourceSlots[index];
			var targetSlot = materialSlots.SourceTargetSlots[index];
			var sourceMaterialId = sourceBindings[sourceSlot][0];
			var sourceMaterialIndex = checked((uint)IndexOf(sourceMesh.MaterialSlotIds, sourceSlot));
			var targetMaterialIndex = checked((uint)IndexOf(materialSlots.OutputSlots, targetSlot));
			replacements.Add(new MaterialSlotReplacement(targetSlot, sourceSlot, sourceMaterialId, sourceMaterialIndex, targetMaterialIndex));
		}

		if (replacements.Select(item => item.TargetSlotId).Distinct().Count() != replacements.Count)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because material slot mapping is ambiguous.");
		}

		return new MaterialSlotMap(replacements, materialSlots.OutputSlots);
	}

	private static MaterialSlots BuildMaterialSlots(UnitMeshModel targetModel, UnitMeshInfo targetMesh, IReadOnlyList<uint> sourceSlots, IReadOnlyDictionary<uint, ulong> sourceBindings)
	{
		var outputSlots = targetMesh.MaterialSlotIds.ToList();
		var targetBindings = targetModel.Materials
			.GroupBy(binding => binding.SectionId)
			.Where(group => group.Select(binding => binding.MaterialId).Distinct().Count() == 1)
			.ToDictionary(group => group.Key, group => group.First().MaterialId);
		var sourceTargetSlots = new List<uint>(sourceSlots.Count);
		var usedTargetSlots = new HashSet<uint>();
		foreach (var sourceSlot in sourceSlots)
		{
			var sourceMaterialId = sourceBindings[sourceSlot];
			var matchingSlot = outputSlots.Cast<uint?>().FirstOrDefault(slot => slot is not null && !usedTargetSlots.Contains(slot.Value) && targetBindings.TryGetValue(slot.Value, out var materialId) && materialId == sourceMaterialId);
			if (matchingSlot is not null)
			{
				sourceTargetSlots.Add(matchingSlot.Value);
				usedTargetSlots.Add(matchingSlot.Value);
				continue;
			}

			var reusableSlot = outputSlots.Cast<uint?>().FirstOrDefault(slot => slot is not null && !usedTargetSlots.Contains(slot.Value));
			if (reusableSlot is not null)
			{
				sourceTargetSlots.Add(reusableSlot.Value);
				usedTargetSlots.Add(reusableSlot.Value);
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
		for (var index = 0; index < values.Count; index++)
		{
			if (values[index] == value)
			{
				return index;
			}
		}

		throw new InvalidDataException("Cannot transfer Unit mesh because a material slot is missing from its slot table.");
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
		for (var index = 0; index < vertices.Length; index++)
		{
			var sourceIndex = vertices[index];
			if (sourceIndex >= sourceVertexCount)
			{
				return -1;
			}
			if (map.ContainsKey(sourceIndex) || Contains(vertices[..index], sourceIndex))
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
		if (!map.ContainsKey(sourceIndex))
		{
			map.Add(sourceIndex, checked((uint)map.Count));
		}
	}

	private static IReadOnlyList<UnitRawMeshSectionData> CopySections(UnitRawMeshData sourceRawMesh, IReadOnlyDictionary<uint, uint> vertexIndexMap, MaterialSlotMap materialMap)
		=> sourceRawMesh.Sections.Select(section =>
		{
			if (!materialMap.TryMap(section.MaterialSlotId, out var targetMaterialIndex, out var targetSlotId))
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because a source material section has no target slot mapping.");
			}
			var triangles = section.Triangles
				.Where(triangle => vertexIndexMap.ContainsKey(triangle.A) && vertexIndexMap.ContainsKey(triangle.B) && vertexIndexMap.ContainsKey(triangle.C))
				.Select(triangle => new UnitTriangleIndices(vertexIndexMap[triangle.A], vertexIndexMap[triangle.B], vertexIndexMap[triangle.C]))
				.ToArray();
			return new UnitRawMeshSectionData(targetMaterialIndex, targetSlotId, triangles);
		}).ToArray();

	private static IReadOnlyList<UnitMeshInfo> ApplyMaterialMapToMeshes(IReadOnlyList<UnitMeshInfo> meshes, int targetMeshInfoIndex, MaterialSlotMap materialMap)
		=> meshes.Select(mesh => mesh.Index == targetMeshInfoIndex
			? mesh with { MaterialSlotIds = materialMap.OutputSlots, Sections = ApplyMaterialMapToSections(mesh.Sections, materialMap) }
			: mesh).ToArray();

	private static IReadOnlyList<UnitMeshSectionInfo> ApplyMaterialMapToSections(IReadOnlyList<UnitMeshSectionInfo> targetSections, MaterialSlotMap materialMap)
	{
		if (targetSections.Count == 0)
		{
			return Array.Empty<UnitMeshSectionInfo>();
		}

		return materialMap.Replacements.Select((replacement, index) =>
		{
			var template = index < targetSections.Count ? targetSections[index] : targetSections[^1];
			return template with { MaterialIndex = replacement.TargetMaterialIndex, MaterialSlotId = replacement.TargetSlotId };
		}).ToArray();
	}

	private static IReadOnlyList<UnitMaterialBinding> ApplyMaterialBindings(IReadOnlyList<UnitMaterialBinding> targetBindings, MaterialSlotMap materialMap)
	{
		var result = targetBindings.Select(binding => materialMap.TryReplaceTargetBinding(binding.SectionId, out var replacement)
			? new UnitMaterialBinding(replacement.TargetSlotId, replacement.SourceMaterialId)
			: binding).ToList();
		foreach (var replacement in materialMap.Replacements.Where(item => result.All(binding => binding.SectionId != item.TargetSlotId)))
		{
			result.Add(new UnitMaterialBinding(replacement.TargetSlotId, replacement.SourceMaterialId));
		}

		return result;
	}

	private static BoneIndexMap? CreateBoneMap(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, IReadOnlyList<UnitRawMeshSectionData> replacementSections)
	{
		var sourceBoneInfo = FindBoneInfo(sourceModel, sourceRawMesh);
		var targetBoneInfo = FindBoneInfo(targetModel, targetRawMesh);
		if (sourceBoneInfo is null || targetBoneInfo is null)
		{
			return null;
		}

		var pairs = sourceRawMesh.Sections.Zip(replacementSections, (sourceSection, replacementSection) =>
		{
			var sourceRemap = FindBoneRemap(sourceBoneInfo, sourceSection.MaterialIndex);
			// Use the target section's MaterialIndex based on section position, not the converted MaterialIndex
			var targetSectionIndex = sourceRawMesh.Sections.ToList().IndexOf(sourceSection);
			var targetSection = targetSectionIndex < targetRawMesh.Sections.Count 
				? targetRawMesh.Sections[targetSectionIndex] 
				: targetRawMesh.Sections.FirstOrDefault();
			var targetRemap = targetSection is not null ? FindBoneRemap(targetBoneInfo, targetSection.MaterialIndex) : null;
			return sourceRemap is null || targetRemap is null
				? null
				: new BoneRemapPair(sourceSection.MaterialIndex, sourceRemap, targetRemap);
		}).ToArray();

		return pairs.Length == 0 || pairs.Any(pair => pair is null)
			? null
			: new BoneIndexMap(sourceBoneInfo, targetBoneInfo, pairs!);
	}

	private static UnitBoneRemap? FindBoneRemap(UnitBoneInfo boneInfo, uint materialIndex)
		=> materialIndex < boneInfo.Remaps.Count ? boneInfo.Remaps[(int)materialIndex] : null;

	private static UnitBoneInfo? FindBoneInfo(UnitMeshModel model, UnitRawMeshData rawMesh)
	{
		if (model.BoneInfos.Count == 0)
		{
			return null;
		}

		var index = rawMesh.LodIndex < 0 ? 0 : rawMesh.LodIndex;
		return index < model.BoneInfos.Count ? model.BoneInfos[index] : model.BoneInfos[0];
	}

	private static IReadOnlyList<UnitRawVertexRecord> CopyVertices(UnitRawMeshData sourceRawMesh, UnitStreamInfo targetStream, BoneIndexMap? boneMap, IReadOnlyList<UnitRawMeshSectionData> replacementSections, IReadOnlyDictionary<uint, uint> vertexIndexMap, bool convertToTargetLayout)
	{
		var sourceMaterialByVertex = BuildSourceMaterialByVertex(sourceRawMesh, replacementSections, vertexIndexMap.Count);
		return vertexIndexMap.Select(pair => sourceRawMesh.Vertices[(int)pair.Key]).Select((vertex, index) => CopyVertex(vertex, (uint)index, targetStream, boneMap, sourceMaterialByVertex[index], convertToTargetLayout)).ToArray();
	}

	private static UnitRawVertexRecord CopyVertex(UnitRawVertexRecord sourceVertex, uint outputIndex, UnitStreamInfo targetStream, BoneIndexMap? boneMap, VertexMaterialIndex materialIndex, bool convertToTargetLayout)
	{
		var data = convertToTargetLayout
			? BuildTargetVertexData(sourceVertex, targetStream, boneMap, materialIndex.SourceMaterialIndex)
			: RewriteBoneIndices(sourceVertex, targetStream, boneMap, materialIndex.SourceMaterialIndex);
		var components = DecodeVertexComponents(targetStream, data);
		return new UnitRawVertexRecord(outputIndex, data, components);
	}

	private static byte[] BuildTargetVertexData(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, BoneIndexMap? boneMap, uint materialIndex)
	{
		var data = new byte[checked((int)targetStream.VertexStride)];
		var cursor = 0;
		foreach (var targetComponent in targetStream.Components)
		{
			var size = checked((int)targetComponent.Size);
			if (size <= 0 || cursor + size > data.Length)
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because the target stream contains an invalid component range.");
			}

			var sourceComponent = FindSourceComponent(sourceVertex.Components, targetComponent);
			WriteTargetComponent(data.AsSpan(cursor, size), targetComponent, sourceComponent, boneMap, materialIndex);
			cursor += size;
		}

		return data;
	}

	private static UnitVertexComponentValue? FindSourceComponent(IReadOnlyList<UnitVertexComponentValue> sourceComponents, UnitStreamComponentInfo targetComponent)
		=> sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type && component.Index == targetComponent.Index)
			?? sourceComponents.FirstOrDefault(component => component.Type == targetComponent.Type);

	private static void WriteTargetComponent(Span<byte> destination, UnitStreamComponentInfo targetComponent, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap, uint materialIndex)
	{
		if (sourceComponent is not null && sourceComponent.Format == targetComponent.Format && sourceComponent.RawData.Length == destination.Length && (targetComponent.Type != 6 || boneMap is null))
		{
			sourceComponent.RawData.CopyTo(destination);
			return;
		}

		switch (targetComponent.Type)
		{
			case 0:
			case 4:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [0f, 0f, 0f, 1f]));
				break;
			case 1:
			case 2:
			case 3:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [0f, 0f, 1f, 1f]));
				break;
			case 5:
				WriteColorComponent(destination, targetComponent, sourceComponent);
				break;
			case 6:
				WriteBoneIndices(destination, targetComponent, sourceComponent, boneMap, materialIndex);
				break;
			case 7:
				WriteFloatComponent(destination, targetComponent, GetFloatValues(sourceComponent, [1f, 0f, 0f, 0f]));
				break;
		}
	}

	private static IReadOnlyList<float> GetFloatValues(UnitVertexComponentValue? sourceComponent, IReadOnlyList<float> fallback)
		=> sourceComponent?.FloatValues.Length > 0 ? sourceComponent.FloatValues : fallback;

	private static void WriteFloatComponent(Span<byte> destination, UnitStreamComponentInfo component, IReadOnlyList<float> values)
	{
		switch (component.FormatName)
		{
			case "float": WriteSingle(destination, 0, GetValue(values, 0)); break;
			case "vec2_float": WriteSingle(destination, 0, GetValue(values, 0)); WriteSingle(destination, 4, GetValue(values, 1)); break;
			case "vec3_float": WriteSingle(destination, 0, GetValue(values, 0)); WriteSingle(destination, 4, GetValue(values, 1)); WriteSingle(destination, 8, GetValue(values, 2)); break;
			case "vec4_float": WriteSingle(destination, 0, GetValue(values, 0)); WriteSingle(destination, 4, GetValue(values, 1)); WriteSingle(destination, 8, GetValue(values, 2)); WriteSingle(destination, 12, GetValue(values, 3)); break;
			case "vec4_1010102": WriteUInt32(destination, 0, EncodeTenBitUnsigned(values)); break;
			case "unk_normal": WriteUInt32(destination, 0, EncodePackedOctNormal(values)); break;
			case "vec2_half": WriteHalf(destination, 0, GetValue(values, 0)); WriteHalf(destination, 2, GetValue(values, 1)); break;
			case "vec4_half": WriteHalf(destination, 0, GetValue(values, 0)); WriteHalf(destination, 2, GetValue(values, 1)); WriteHalf(destination, 4, GetValue(values, 2)); WriteHalf(destination, 6, GetValue(values, 3)); break;
			default: throw new InvalidDataException($"Cannot transfer Unit mesh because target float format '{component.FormatName}' is unsupported.");
		}
	}

	private static void WriteColorComponent(Span<byte> destination, UnitStreamComponentInfo component, UnitVertexComponentValue? sourceComponent)
	{
		if (component.FormatName is not ("rgba_r8g8b8a8" or "vec4_uint8"))
		{
			throw new InvalidDataException($"Cannot transfer Unit mesh because target color format '{component.FormatName}' is unsupported.");
		}

		var values = sourceComponent?.UIntValues;
		for (var index = 0; index < 4; index++)
		{
			destination[index] = values is not null && index < values.Length ? ClampToByte(values[index]) : byte.MaxValue;
		}
	}

	private static void WriteBoneIndices(Span<byte> destination, UnitStreamComponentInfo component, UnitVertexComponentValue? sourceComponent, BoneIndexMap? boneMap, uint materialIndex)
	{
		IReadOnlyList<uint> values = sourceComponent?.UIntValues ?? Array.Empty<uint>();
		if (boneMap is not null)
		{
			if (materialIndex == uint.MaxValue)
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because a skinned vertex is not referenced by a material section.");
			}
			values = MapBoneIndices(values, sourceComponent?.FloatValues ?? Array.Empty<float>(), boneMap, materialIndex);
		}

		if (component.FormatName == "vec4_uint8")
		{
			for (var index = 0; index < 4; index++) destination[index] = index < values.Count ? ClampToByte(values[index]) : (byte)0;
			return;
		}
		if (component.FormatName == "vec4_uint32")
		{
			for (var index = 0; index < 4; index++) WriteUInt32(destination, index * 4, index < values.Count ? values[index] : 0);
			return;
		}

		throw new InvalidDataException($"Cannot transfer Unit mesh because target bone-index format '{component.FormatName}' is unsupported.");
	}

	private static IReadOnlyList<uint> MapBoneIndices(IReadOnlyList<uint> values, IReadOnlyList<float> weights, BoneIndexMap boneMap, uint materialIndex)
	{
		var mapped = new uint[Math.Max(4, values.Count)];
		var mappedFlags = new bool[mapped.Length];
		for (var index = 0; index < mapped.Length; index++)
		{
			var sourceIndex = index < values.Count ? values[index] : 0;
			if (boneMap.TryMap(sourceIndex, materialIndex, out var targetIndex))
			{
				mapped[index] = targetIndex;
				mappedFlags[index] = true;
			}
		}

		for (var index = 0; index < mapped.Length; index++)
		{
			if (!mappedFlags[index])
			{
				mapped[index] = index < values.Count ? values[index] : 0;
			}
		}

		return mapped;
	}

	private static float GetValue(IReadOnlyList<float> values, int index) => index < values.Count ? values[index] : 0f;

	private static uint EncodeTenBitUnsigned(IReadOnlyList<float> values)
		=> ClampToBits(GetValue(values, 0), 1023) | (ClampToBits(GetValue(values, 1), 1023) << 10) | (ClampToBits(GetValue(values, 2), 1023) << 20) | (ClampToBits(GetValue(values, 3), 3) << 30);

	private static uint EncodePackedOctNormal(IReadOnlyList<float> values)
	{
		var x = GetValue(values, 0);
		var y = GetValue(values, 1);
		var z = values.Count > 2 ? values[2] : 1f;
		var length = MathF.Sqrt(x * x + y * y + z * z);
		if (length > 0) { x /= length; y /= length; z /= length; }
		var l1 = Math.Abs(x) + Math.Abs(y) + Math.Abs(z);
		if (l1 > 0) { x /= l1; y /= l1; }
		if (z < 0) { var oldX = x; x = (1f - Math.Abs(y)) * Math.Sign(oldX == 0 ? 1f : oldX); y = (1f - Math.Abs(oldX)) * Math.Sign(y == 0 ? 1f : y); }
		return ClampToBits((x + 1f) * .5f, 1023) | (ClampToBits((y + 1f) * .5f, 1023) << 10);
	}

	private static uint ClampToBits(float value, uint max) => (uint)Math.Clamp((int)MathF.Round(value * max), 0, (int)max);
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

	private static IReadOnlyList<VertexMaterialIndex> BuildSourceMaterialByVertex(UnitRawMeshData sourceRawMesh, IReadOnlyList<UnitRawMeshSectionData> replacementSections, int vertexCount)
	{
		var materials = Enumerable.Repeat(new VertexMaterialIndex(uint.MaxValue, uint.MaxValue), vertexCount).ToArray();
		for (var sectionIndex = 0; sectionIndex < replacementSections.Count; sectionIndex++)
		{
			var sourceMaterialIndex = sectionIndex < sourceRawMesh.Sections.Count ? sourceRawMesh.Sections[sectionIndex].MaterialIndex : replacementSections[sectionIndex].MaterialIndex;
			var targetMaterialIndex = replacementSections[sectionIndex].MaterialIndex;
			foreach (var triangle in replacementSections[sectionIndex].Triangles)
			{
				AssignMaterial(materials, triangle.A, sourceMaterialIndex, targetMaterialIndex);
				AssignMaterial(materials, triangle.B, sourceMaterialIndex, targetMaterialIndex);
				AssignMaterial(materials, triangle.C, sourceMaterialIndex, targetMaterialIndex);
			}
		}
		return materials;
	}

	private static void AssignMaterial(VertexMaterialIndex[] materials, uint vertexIndex, uint sourceMaterialIndex, uint targetMaterialIndex)
	{
		if (vertexIndex >= materials.Length)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a triangle references a missing source vertex.");
		}
		if (materials[(int)vertexIndex].SourceMaterialIndex != uint.MaxValue && materials[(int)vertexIndex].SourceMaterialIndex != sourceMaterialIndex)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because one vertex belongs to multiple bone-remap material sections.");
		}
		materials[(int)vertexIndex] = new VertexMaterialIndex(sourceMaterialIndex, targetMaterialIndex);
	}

	private static byte[] RewriteBoneIndices(UnitRawVertexRecord vertex, UnitStreamInfo stream, BoneIndexMap? boneMap, uint materialIndex)
	{
		var data = vertex.Data.ToArray();
		if (boneMap is null)
		{
			return data;
		}
		if (materialIndex == uint.MaxValue)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a skinned vertex is not referenced by any material section.");
		}

		var cursor = 0;
		foreach (var component in stream.Components)
		{
			var size = checked((int)component.Size);
			if (component.Type == 6)
			{
				var sourceComponent = vertex.Components.FirstOrDefault(value => value.Type == component.Type && value.Index == component.Index)
					?? vertex.Components.FirstOrDefault(value => value.Type == component.Type);
				var values = MapBoneIndices(sourceComponent?.UIntValues ?? ReadBoneIndices(data.AsSpan(cursor, size), component), sourceComponent?.FloatValues ?? Array.Empty<float>(), boneMap, materialIndex);
				if (component.FormatName == "vec4_uint8")
				{
					for (var index = 0; index < 4; index++) data[cursor + index] = ClampToByte(values[index]);
				}
				else if (component.FormatName == "vec4_uint32")
				{
					for (var index = 0; index < 4; index++)
					{
						var offset = cursor + index * 4;
						BitConverter.GetBytes(values[index]).CopyTo(data, offset);
					}
				}
				else
				{
					throw new InvalidDataException("Cannot transfer Unit mesh because its bone-index vertex format is unsupported.");
				}
			}
			cursor += size;
		}

		return data;
	}

	private static IReadOnlyList<uint> ReadBoneIndices(ReadOnlySpan<byte> data, UnitStreamComponentInfo component)
	{
		if (component.FormatName == "vec4_uint8")
		{
			return new uint[] { data[0], data[1], data[2], data[3] };
		}
		if (component.FormatName == "vec4_uint32")
		{
			return new uint[] { BitConverter.ToUInt32(data[..4]), BitConverter.ToUInt32(data.Slice(4, 4)), BitConverter.ToUInt32(data.Slice(8, 4)), BitConverter.ToUInt32(data.Slice(12, 4)) };
		}

		return Array.Empty<uint>();
	}

	private static IReadOnlyList<UnitVertexComponentValue> DecodeVertexComponents(UnitStreamInfo stream, byte[] data)
	{
		var components = new List<UnitVertexComponentValue>(stream.Components.Count);
		var cursor = 0;
		foreach (var component in stream.Components)
		{
			var size = checked((int)component.Size);
			if (size <= 0 || cursor + size > data.Length)
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because a rewritten vertex component is outside the target stride.");
			}

			var raw = data.AsSpan(cursor, size).ToArray();
			components.Add(new UnitVertexComponentValue(component.Type, GetComponentTypeName(component.Type), component.Format, component.FormatName, component.Index, DecodeFloats(raw, component), DecodeUInts(raw, component), raw));
			cursor += size;
		}

		return components;
	}

	private static float[] DecodeFloats(ReadOnlySpan<byte> data, UnitStreamComponentInfo component)
	{
		return component.FormatName switch
		{
			"float" => [ReadSingle(data, 0)],
			"vec2_float" => [ReadSingle(data, 0), ReadSingle(data, 4)],
			"vec3_float" => [ReadSingle(data, 0), ReadSingle(data, 4), ReadSingle(data, 8)],
			"vec4_float" => [ReadSingle(data, 0), ReadSingle(data, 4), ReadSingle(data, 8), ReadSingle(data, 12)],
			"vec2_half" => [(float)ReadHalf(data, 0), (float)ReadHalf(data, 2)],
			"vec4_half" => [(float)ReadHalf(data, 0), (float)ReadHalf(data, 2), (float)ReadHalf(data, 4), (float)ReadHalf(data, 6)],
			_ => Array.Empty<float>()
		};
	}

	private static uint[] DecodeUInts(ReadOnlySpan<byte> data, UnitStreamComponentInfo component)
	{
		return component.FormatName switch
		{
			"vec4_uint8" or "rgba_r8g8b8a8" => [data[0], data[1], data[2], data[3]],
			"vec4_uint32" => [BitConverter.ToUInt32(data[..4]), BitConverter.ToUInt32(data.Slice(4, 4)), BitConverter.ToUInt32(data.Slice(8, 4)), BitConverter.ToUInt32(data.Slice(12, 4))],
			_ => Array.Empty<uint>()
		};
	}

	private static string GetComponentTypeName(uint type) => type switch
	{
		0 => "position",
		1 => "normal",
		2 => "tangent",
		3 => "binormal",
		4 => "texcoord",
		5 => "color",
		6 => "bone_index",
		7 => "bone_weight",
		_ => $"component_{type}"
	};

	private static float ReadSingle(ReadOnlySpan<byte> data, int offset) => BitConverter.Int32BitsToSingle((int)BitConverter.ToUInt32(data.Slice(offset, 4)));
	private static Half ReadHalf(ReadOnlySpan<byte> data, int offset) => BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data.Slice(offset, 2)));

	private sealed record MaterialSlots(IReadOnlyList<uint> SourceTargetSlots, IReadOnlyList<uint> OutputSlots);

	private sealed record MaterialSlotReplacement(uint TargetSlotId, uint SourceSlotId, ulong SourceMaterialId, uint SourceMaterialIndex, uint TargetMaterialIndex);

	private sealed class MaterialSlotMap
	{
		private readonly Dictionary<uint, MaterialSlotReplacement> sourceSlotLookup;
		private readonly Dictionary<uint, MaterialSlotReplacement> targetSlotLookup;

		public MaterialSlotMap(IReadOnlyList<MaterialSlotReplacement> replacements, IReadOnlyList<uint> outputSlots)
		{
			Replacements = replacements;
			OutputSlots = outputSlots;
			sourceSlotLookup = replacements.ToDictionary(replacement => replacement.SourceSlotId);
			targetSlotLookup = replacements.ToDictionary(replacement => replacement.TargetSlotId);
		}

		public IReadOnlyList<MaterialSlotReplacement> Replacements { get; }

		public IReadOnlyList<uint> OutputSlots { get; }

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
	}

	private sealed class BoneIndexMap
	{
		private readonly Dictionary<uint, Dictionary<uint, uint>> sourceToTargetByMaterial;
		private readonly Dictionary<uint, uint>? fallbackSourceToTarget;

		public BoneIndexMap(UnitBoneInfo sourceBoneInfo, UnitBoneInfo targetBoneInfo, IReadOnlyList<BoneRemapPair> remapPairs)
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

	private sealed record BoneRemapPair(uint SourceMaterialIndex, UnitBoneRemap SourceRemap, UnitBoneRemap TargetRemap);

	private readonly record struct VertexMaterialIndex(uint SourceMaterialIndex, uint TargetMaterialIndex);
}

public sealed record UnitMeshTransferResult(UnitMeshModel Model, IReadOnlyCollection<ulong> ReplacementMaterialIds);