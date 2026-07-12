using HD2ModAdaptation.PatchReconstruction.UnitMesh;

namespace HD2ModAdaptation.Processing;

// Purpose: Simplified mesh transfer using extracted BoneRemapper and MaterialMapper components.
// This is a cleaner implementation of the core mesh transfer logic from StrictUnitMeshTransfer.
public sealed class MeshTransfer
{
	private readonly bool allowTargetLayoutConversion;

	public MeshTransfer(bool allowTargetLayoutConversion = false)
	{
		this.allowTargetLayoutConversion = allowTargetLayoutConversion;
	}

	/// <summary>
	/// Transfers mesh data from source to target Unit model.
	/// </summary>
	public MeshTransferResult Transfer(
		UnitMeshModel targetModel,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);

		// 1. Find raw mesh and stream data
		var targetRawMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceRawMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var targetStream = FindStream(targetModel, targetRawMesh, "target");
		var sourceStream = FindStream(sourceModel, sourceRawMesh, "source");

		if (!allowTargetLayoutConversion)
		{
			ValidateStreamCompatibility(targetStream, sourceStream);
		}

		// 2. Create material mapper
		var materialMapper = MaterialMapper.Create(targetModel, targetRawMesh, sourceModel, sourceRawMesh, sourceMeshInfoIndex);

		// 3. Build vertex index map and copy sections
		var vertexLimit = GetVertexLimit(targetStream, sourceRawMesh);
		var vertexIndexMap = BuildVertexIndexMap(sourceRawMesh.Sections, vertexLimit, sourceRawMesh.Vertices.Count);
		var replacementSections = TransformSections(sourceRawMesh, vertexIndexMap, materialMapper);

		// 4. Create bone remapper
		var boneRemapper = CreateBoneRemapper(targetModel, targetRawMesh, sourceModel, sourceRawMesh, replacementSections);

		// 5. Transform vertices
		var vertices = TransformVertices(sourceRawMesh, targetStream, boneRemapper, replacementSections, vertexIndexMap);

		// 6. Build result
		var updatedRawMesh = targetRawMesh with
		{
			Sections = replacementSections,
			Triangles = replacementSections.SelectMany(section => section.Triangles).ToArray(),
			Vertices = vertices
		};

		var updatedMeshes = ApplyMaterialMapping(targetModel.Meshes, targetMeshInfoIndex, materialMapper);
		var updatedMaterials = ApplyMaterialBindings(targetModel.Materials, materialMapper);

		var updatedModel = targetModel with
		{
			Meshes = updatedMeshes,
			Materials = updatedMaterials,
			RawMeshData = targetModel.RawMeshData.Select(mesh =>
				mesh.MeshInfoIndex == targetMeshInfoIndex ? updatedRawMesh : mesh).ToArray()
		};

		var replacementMaterialIds = materialMapper.Replacements
			.Select(r => r.SourceMaterialId)
			.Distinct()
			.OrderBy(id => id)
			.ToArray();

		return new MeshTransferResult(updatedModel, replacementMaterialIds);
	}

	#region Vertex Index Mapping

	private static IReadOnlyDictionary<uint, uint> BuildVertexIndexMap(
		IEnumerable<UnitRawMeshSectionData> sections,
		int vertexLimit,
		int sourceVertexCount)
	{
		var boundedLimit = Math.Min(vertexLimit, sourceVertexCount);

		if (vertexLimit >= ushort.MaxValue + 1)
		{
			return BuildFullVertexMap(sections, boundedLimit, sourceVertexCount);
		}

		return BuildCompactVertexMap(sections, boundedLimit);
	}

	private static IReadOnlyDictionary<uint, uint> BuildFullVertexMap(
		IEnumerable<UnitRawMeshSectionData> sections,
		int boundedLimit,
		int sourceVertexCount)
	{
		var required = RequiredVertexCount(sections, boundedLimit);

		if (required > boundedLimit)
		{
			throw new InvalidDataException($"Cannot transfer Unit mesh because it requires {required} vertices but target supports only {boundedLimit}.");
		}

		if (required == sourceVertexCount)
		{
			// All vertices used, create identity map
			return Enumerable.Range(0, sourceVertexCount)
				.ToDictionary(i => (uint)i, i => (uint)i);
		}

		// Build referenced vertices map
		var map = new Dictionary<uint, uint>();
		foreach (var section in sections)
		{
			foreach (var triangle in section.Triangles)
			{
				AddVertexIfNeeded(map, triangle.A, boundedLimit);
				AddVertexIfNeeded(map, triangle.B, boundedLimit);
				AddVertexIfNeeded(map, triangle.C, boundedLimit);
			}
		}

		return map;
	}

	private static IReadOnlyDictionary<uint, uint> BuildCompactVertexMap(
		IEnumerable<UnitRawMeshSectionData> sections,
		int boundedLimit)
	{
		var map = new Dictionary<uint, uint>();

		foreach (var section in sections)
		{
			foreach (var triangle in section.Triangles)
			{
				AddVertexIfNeeded(map, triangle.A, boundedLimit);
				AddVertexIfNeeded(map, triangle.B, boundedLimit);
				AddVertexIfNeeded(map, triangle.C, boundedLimit);
			}
		}

		return map;
	}

	private static void AddVertexIfNeeded(Dictionary<uint, uint> map, uint sourceIndex, int limit)
	{
		if (sourceIndex >= limit || map.ContainsKey(sourceIndex))
		{
			return;
		}

		map.Add(sourceIndex, (uint)map.Count);
	}

	private static int RequiredVertexCount(
		IEnumerable<UnitRawMeshSectionData> sections,
		int boundedLimit)
	{
		var vertices = new uint[boundedLimit];
		var required = 0;

		foreach (var section in sections)
		{
			foreach (var triangle in section.Triangles)
			{
				if (TryMarkVertex(vertices, triangle.A, ref required, boundedLimit)) return -1;
				if (TryMarkVertex(vertices, triangle.B, ref required, boundedLimit)) return -1;
				if (TryMarkVertex(vertices, triangle.C, ref required, boundedLimit)) return -1;
			}
		}

		return required;
	}

	private static bool TryMarkVertex(uint[] vertices, uint index, ref int required, int limit)
	{
		if (index >= limit)
		{
			return true; // Out of bounds
		}

		if (vertices[index] == 0 && (index != 0 || required == 0))
		{
			vertices[index] = 1;
			required++;
		}

		return false;
	}

	#endregion

	#region Section Transformation

	private static IReadOnlyList<UnitRawMeshSectionData> TransformSections(
		UnitRawMeshData sourceRawMesh,
		IReadOnlyDictionary<uint, uint> vertexIndexMap,
		MaterialMapper materialMapper)
	{
		return sourceRawMesh.Sections.Select(section =>
		{
			if (!materialMapper.TryMap(section.MaterialSlotId, out var targetMaterialIndex, out var targetSlotId))
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because a source material section has no target slot mapping.");
			}

			var triangles = section.Triangles
				.Where(t => vertexIndexMap.ContainsKey(t.A) && vertexIndexMap.ContainsKey(t.B) && vertexIndexMap.ContainsKey(t.C))
				.Select(t => new UnitTriangleIndices(vertexIndexMap[t.A], vertexIndexMap[t.B], vertexIndexMap[t.C]))
				.ToArray();

			return new UnitRawMeshSectionData(targetMaterialIndex, targetSlotId, triangles);
		}).ToArray();
	}

	#endregion

	#region Bone Remapping

	private BoneRemapper? CreateBoneRemapper(
		UnitMeshModel targetModel,
		UnitRawMeshData targetRawMesh,
		UnitMeshModel sourceModel,
		UnitRawMeshData sourceRawMesh,
		IReadOnlyList<UnitRawMeshSectionData> replacementSections)
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

			// Get target section's original MaterialIndex (not the converted one)
			var targetSectionIndex = Array.IndexOf(sourceRawMesh.Sections.ToArray(), sourceSection);
			var targetSection = targetSectionIndex < targetRawMesh.Sections.Count
				? targetRawMesh.Sections[targetSectionIndex]
				: targetRawMesh.Sections.FirstOrDefault();
			var targetRemap = targetSection is not null
				? FindBoneRemap(targetBoneInfo, targetSection.MaterialIndex)
				: null;

			return sourceRemap is null || targetRemap is null
				? null
				: new BoneRemapPair(sourceSection.MaterialIndex, sourceRemap, targetRemap);
		}).ToArray();

		return pairs.Length == 0 || pairs.Any(pair => pair is null)
			? null
			: new BoneRemapper(sourceBoneInfo, targetBoneInfo, pairs!);
	}

	private static UnitBoneInfo? FindBoneInfo(UnitMeshModel model, UnitRawMeshData rawMesh)
	{
		// Find BoneInfo by matching MeshInfoIndex
		// The BoneInfo index is typically stored in the MeshInfo, not RawMeshData
		var meshInfo = model.Meshes.FirstOrDefault(m => m.Index == rawMesh.MeshInfoIndex);
		if (meshInfo is null)
		{
			return null;
		}

		// Try to find matching BoneInfo (simplified - actual logic may need adjustment)
		return model.BoneInfos.Count > 0 ? model.BoneInfos[0] : null;
	}

	private static UnitBoneRemap? FindBoneRemap(UnitBoneInfo boneInfo, uint materialIndex)
		=> materialIndex < boneInfo.Remaps.Count ? boneInfo.Remaps[(int)materialIndex] : null;

	#endregion

	#region Vertex Transformation

	private IReadOnlyList<UnitRawVertexRecord> TransformVertices(
		UnitRawMeshData sourceRawMesh,
		UnitStreamInfo targetStream,
		BoneRemapper? boneRemapper,
		IReadOnlyList<UnitRawMeshSectionData> replacementSections,
		IReadOnlyDictionary<uint, uint> vertexIndexMap)
	{
		var sourceMaterialByVertex = BuildVertexMaterialMap(sourceRawMesh, replacementSections, vertexIndexMap.Count);

		return vertexIndexMap
			.Select(pair => sourceRawMesh.Vertices[(int)pair.Key])
			.Select((vertex, index) => TransformVertex(
				vertex,
				(uint)index,
				targetStream,
				boneRemapper,
				sourceMaterialByVertex[index]))
			.ToArray();
	}

	private static IReadOnlyList<VertexMaterialIndex> BuildVertexMaterialMap(
		UnitRawMeshData sourceRawMesh,
		IReadOnlyList<UnitRawMeshSectionData> replacementSections,
		int vertexCount)
	{
		var materials = Enumerable.Repeat(
			new VertexMaterialIndex(uint.MaxValue, uint.MaxValue),
			vertexCount).ToArray();

		for (var sectionIndex = 0; sectionIndex < replacementSections.Count; sectionIndex++)
		{
			var sourceMaterialIndex = sectionIndex < sourceRawMesh.Sections.Count
				? sourceRawMesh.Sections[sectionIndex].MaterialIndex
				: replacementSections[sectionIndex].MaterialIndex;
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

	private static void AssignMaterial(
		VertexMaterialIndex[] materials,
		uint vertexIndex,
		uint sourceMaterialIndex,
		uint targetMaterialIndex)
	{
		if (vertexIndex >= materials.Length)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because a triangle references a missing source vertex.");
		}

		if (materials[vertexIndex].SourceMaterialIndex != uint.MaxValue &&
		    materials[vertexIndex].SourceMaterialIndex != sourceMaterialIndex)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because one vertex belongs to multiple bone-remap material sections.");
		}

		materials[vertexIndex] = new VertexMaterialIndex(sourceMaterialIndex, targetMaterialIndex);
	}

	private UnitRawVertexRecord TransformVertex(
		UnitRawVertexRecord sourceVertex,
		uint outputIndex,
		UnitStreamInfo targetStream,
		BoneRemapper? boneRemapper,
		VertexMaterialIndex materialIndex)
	{
		// For now, use simple copy - actual vertex transformation logic would go here
		// This is a placeholder for the complex vertex data transformation
		var data = sourceVertex.Data; // Use Data instead of RawData

		return new UnitRawVertexRecord(outputIndex, data, sourceVertex.Components);
	}

	#endregion

	#region Material Application

	private static IReadOnlyList<UnitMeshInfo> ApplyMaterialMapping(
		IReadOnlyList<UnitMeshInfo> meshes,
		int targetMeshInfoIndex,
		MaterialMapper materialMapper)
	{
		return meshes.Select(mesh =>
		{
			if (mesh.Index != targetMeshInfoIndex)
			{
				return mesh;
			}

			return mesh with { MaterialSlotIds = materialMapper.OutputSlots };
		}).ToArray();
	}

	private static IReadOnlyList<UnitMaterialBinding> ApplyMaterialBindings(
		IReadOnlyList<UnitMaterialBinding> materials,
		MaterialMapper materialMapper)
	{
		var updated = materials
			.Where(binding => !materialMapper.TryReplaceTargetBinding(binding.SectionId, out _))
			.ToList();

		updated.AddRange(materialMapper.Replacements.Select(replacement =>
			new UnitMaterialBinding(replacement.TargetSlotId, replacement.SourceMaterialId)));

		return updated;
	}

	#endregion

	#region Helpers

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain RawMeshData for MeshInfoIndex {meshInfoIndex}.");

	private static UnitStreamInfo FindStream(UnitMeshModel model, UnitRawMeshData rawMesh, string role)
		=> model.Streams.FirstOrDefault(stream => stream.Index == rawMesh.StreamIndex)
			?? throw new InvalidDataException($"The {role} Unit does not contain stream {rawMesh.StreamIndex}.");

	private static void ValidateStreamCompatibility(UnitStreamInfo target, UnitStreamInfo source)
	{
		if (target.VertexStride != source.VertexStride || target.Components.Count != source.Components.Count)
		{
			throw new InvalidDataException("Cannot transfer Unit mesh because source and target stream layouts differ.");
		}

		for (var index = 0; index < target.Components.Count; index++)
		{
			var targetComponent = target.Components[index];
			var sourceComponent = source.Components[index];
			if (targetComponent.Type != sourceComponent.Type ||
			    targetComponent.Format != sourceComponent.Format ||
			    targetComponent.Index != sourceComponent.Index ||
			    targetComponent.Size != sourceComponent.Size)
			{
				throw new InvalidDataException("Cannot transfer Unit mesh because source and target stream component layouts differ.");
			}
		}
	}

	private static int GetVertexLimit(UnitStreamInfo targetStream, UnitRawMeshData sourceRawMesh)
		=> targetStream.IndexBufferType == 1 ? sourceRawMesh.Vertices.Count : ushort.MaxValue + 1;

	#endregion
}

public sealed record MeshTransferResult(UnitMeshModel Model, IReadOnlyCollection<ulong> ReplacementMaterialIds);

internal readonly record struct VertexMaterialIndex(uint SourceMaterialIndex, uint TargetMaterialIndex);
