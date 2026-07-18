using System.Numerics;

namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Re-encodes source mesh attributes into a current target stream and rebuilds the target LOD BoneInfo remaps using SDK-style real-bone semantics.
public sealed class SdkStyleMeshReencoder
{
	private const float ActiveWeightThreshold = 0.001f;
	private readonly SdkStyleBoneRemapBuilder remapBuilder;
	private readonly bool allowSectionRebuild;
	private readonly bool propagateSourceMaterials;
	private readonly bool transformMeshSpace;
	private readonly bool rebuildTargetInverseJointMatrices;
	private readonly IReadOnlySet<ulong>? allowedSourceMaterialIds;

	public SdkStyleMeshReencoder(SdkStyleBoneRemapBuilder? remapBuilder = null, bool allowSectionRebuild = false, bool propagateSourceMaterials = true, bool transformMeshSpace = false, IReadOnlySet<ulong>? allowedSourceMaterialIds = null, bool rebuildTargetInverseJointMatrices = false)
	{
		this.remapBuilder = remapBuilder ?? new SdkStyleBoneRemapBuilder();
		this.allowSectionRebuild = allowSectionRebuild;
		this.propagateSourceMaterials = propagateSourceMaterials;
		this.transformMeshSpace = transformMeshSpace;
		this.rebuildTargetInverseJointMatrices = rebuildTargetInverseJointMatrices;
		this.allowedSourceMaterialIds = allowedSourceMaterialIds;
	}

	public SdkStyleMeshReencodeResult Reencode(
		UnitMeshModel targetModel,
		int targetMeshInfoIndex,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex)
	{
		ArgumentNullException.ThrowIfNull(targetModel);
		ArgumentNullException.ThrowIfNull(sourceModel);
		var targetRawMesh = FindRawMesh(targetModel, targetMeshInfoIndex, "target");
		var sourceRawMesh = FindRawMesh(sourceModel, sourceMeshInfoIndex, "source");
		var meshSpaceTransform = transformMeshSpace || rebuildTargetInverseJointMatrices
			? BuildMeshSpaceTransform(targetModel, targetMeshInfoIndex, sourceModel, sourceMeshInfoIndex)
			: Matrix4x4.Identity;
		if (sourceRawMesh.Sections.Count != targetRawMesh.Sections.Count)
		{
			if (allowSectionRebuild)
			{
				return ReencodeWithRebuiltSections(targetModel, targetRawMesh, sourceModel, sourceRawMesh, sourceMeshInfoIndex, meshSpaceTransform);
			}
			throw new InvalidDataException("SDK-style re-encoding currently requires source and target meshes to have the same section count.");
		}

		var targetStream = FindStream(targetModel, targetRawMesh, "target");
		var sourceUsesBones = sourceRawMesh.Vertices.Any(vertex => FindComponent(vertex, 6) is not null);
		if (sourceUsesBones && targetModel.TransformNameHashes.Count == 0)
		{
			throw new InvalidDataException("The current target Unit has no TransformInfo bone-name hashes.");
		}
		if (!sourceUsesBones)
		{
			return ReencodeUnskinned(targetModel, targetRawMesh, sourceRawMesh, targetStream, sourceMeshInfoIndex, meshSpaceTransform);
		}

		var sourceBoneInfo = FindBoneInfo(sourceModel, sourceRawMesh, "source");
		var targetBoneInfo = FindBoneInfo(targetModel, targetRawMesh, "target");
		var materialByVertex = BuildVertexMaterialMap(sourceRawMesh, targetRawMesh);
		var rebuiltTargetBoneInfo = rebuildTargetInverseJointMatrices
			? BuildCompleteSourceOrderedBoneInfo(sourceBoneInfo, sourceModel.TransformNameHashes, targetModel.TransformNameHashes)
			: remapBuilder.SetRemap(targetBoneInfo, BuildSectionRemapBoneNames(sourceRawMesh, targetRawMesh, sourceBoneInfo, sourceModel.TransformNameHashes, targetModel), targetModel.TransformNameHashes);
		rebuiltTargetBoneInfo = AttachTargetBoneMatrices(rebuiltTargetBoneInfo, BuildTargetMatrixMap(targetModel, targetRawMesh));
		var updatedVertices = sourceRawMesh.Vertices.Select(vertex => new UnitRawVertexRecord(
			vertex.Index,
			EncodeTargetVertex(vertex, targetStream, sourceBoneInfo, rebuiltTargetBoneInfo, sourceModel.TransformNameHashes, targetModel.TransformNameHashes, materialByVertex.TryGetValue(vertex.Index, out var materials) ? materials.Source : 0, materialByVertex.TryGetValue(vertex.Index, out materials) ? materials.Target : 0, meshSpaceTransform, transformMeshSpace || rebuildTargetInverseJointMatrices),
			Array.Empty<UnitVertexComponentValue>())).ToArray();
		var vertexIndexMap = BuildVertexIndexMap(sourceRawMesh);
		var updatedSections = sourceRawMesh.Sections.Select((sourceSection, index) => new UnitRawMeshSectionData(
			targetRawMesh.Sections[index].MaterialIndex,
			targetRawMesh.Sections[index].MaterialSlotId,
			sourceSection.Triangles.Select(triangle => new UnitTriangleIndices(
				vertexIndexMap[triangle.A],
				vertexIndexMap[triangle.B],
				vertexIndexMap[triangle.C])).ToArray())).ToArray();
		var compactedVertices = vertexIndexMap
			.OrderBy(pair => pair.Value)
			.Select(pair => updatedVertices[(int)pair.Key] with { Index = pair.Value })
			.ToArray();
		var updatedRawMesh = targetRawMesh with
		{
			Sections = updatedSections,
			Triangles = updatedSections.SelectMany(section => section.Triangles).ToArray(),
			Vertices = compactedVertices
		};
		var targetBoneInfoIndex = GetBoneInfoIndex(targetModel, targetRawMesh);
		var requires32BitIndices = targetStream.IndexBufferType != 1 && compactedVertices.Length > ushort.MaxValue;
		var materialBindings = propagateSourceMaterials
			? ApplySourceMaterialBindings(targetModel.Materials, targetRawMesh, sourceRawMesh, sourceModel, sourceMeshInfoIndex, allowedSourceMaterialIds)
			: new SourceMaterialBindings(targetModel.Materials, Array.Empty<ulong>());
		var updatedModel = targetModel with
		{
			BoneInfos = targetModel.BoneInfos.Select((boneInfo, index) => index == targetBoneInfoIndex ? rebuiltTargetBoneInfo : boneInfo).ToArray(),
			Streams = requires32BitIndices
				? targetModel.Streams.Select(stream => stream.Index == targetStream.Index ? stream with { IndexBufferType = 1 } : stream).ToArray()
				: targetModel.Streams,
			Materials = materialBindings.Bindings,
			RawMeshData = targetModel.RawMeshData.Select(mesh => mesh.MeshInfoIndex == targetMeshInfoIndex ? updatedRawMesh : mesh).ToArray()
		};
		return new SdkStyleMeshReencodeResult(updatedModel, targetMeshInfoIndex, sourceMeshInfoIndex, targetBoneInfoIndex, rebuiltTargetBoneInfo, materialBindings.SourceMaterialIds);
	}

	private SdkStyleMeshReencodeResult ReencodeWithRebuiltSections(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitMeshModel sourceModel, UnitRawMeshData sourceRawMesh, int sourceMeshInfoIndex, Matrix4x4 meshSpaceTransform)
	{
		if (targetRawMesh.Sections.Count == 0 || sourceRawMesh.Sections.Count == 0)
		{
			throw new InvalidDataException("SDK-style section rebuild requires both source and target meshes to contain material sections.");
		}
		var targetStream = FindStream(targetModel, targetRawMesh, "target");
		var sourceUsesBones = sourceRawMesh.Vertices.Any(vertex => FindComponent(vertex, 6) is not null);
		if (sourceUsesBones && targetModel.TransformNameHashes.Count == 0)
		{
			throw new InvalidDataException("The current target Unit has no TransformInfo bone-name hashes.");
		}

		var assignments = sourceRawMesh.Sections.Select((section, sourceIndex) => new SectionAssignment(
			sourceIndex,
			sourceRawMesh.Sections[sourceIndex],
			targetRawMesh.Sections[sourceIndex % targetRawMesh.Sections.Count],
			sourceIndex % targetRawMesh.Sections.Count)).ToArray();
		var outputSections = assignments.GroupBy(assignment => assignment.TargetSectionIndex).OrderBy(group => group.Key).ToArray();
		var materialBindings = propagateSourceMaterials
			? ApplyRebuiltSectionMaterialBindings(targetModel.Materials, outputSections, sourceModel, sourceMeshInfoIndex, allowedSourceMaterialIds)
			: new SourceMaterialBindings(targetModel.Materials, Array.Empty<ulong>());
		targetModel = targetModel with { Materials = materialBindings.Bindings };
		var vertexKeys = new Dictionary<(uint sourceVertex, uint sourceMaterial), uint>();
		var vertexSources = new List<(uint sourceVertex, uint sourceMaterial, uint targetMaterial)>();
		var rebuiltSections = new List<UnitRawMeshSectionData>();
		foreach (var group in outputSections)
		{
			var targetSection = group.First().TargetSection;
			var triangles = new List<UnitTriangleIndices>();
			foreach (var assignment in group)
			{
				foreach (var triangle in assignment.SourceSection.Triangles)
				{
					triangles.Add(new UnitTriangleIndices(
						GetOrAddVertex(triangle.A, assignment.SourceSection.MaterialIndex, targetSection.MaterialIndex),
						GetOrAddVertex(triangle.B, assignment.SourceSection.MaterialIndex, targetSection.MaterialIndex),
						GetOrAddVertex(triangle.C, assignment.SourceSection.MaterialIndex, targetSection.MaterialIndex)));
				}
			}
			rebuiltSections.Add(new UnitRawMeshSectionData(targetSection.MaterialIndex, targetSection.MaterialSlotId, triangles));
		}

		if (!sourceUsesBones)
		{
			var vertices = vertexSources.Select((source, index) => EncodeUnskinnedVertex(sourceModel, sourceRawMesh, source.sourceVertex, targetStream, (uint)index, meshSpaceTransform)).ToArray();
			return CreateRebuiltResult(targetModel, targetRawMesh, sourceMeshInfoIndex, rebuiltSections, vertices, null, materialBindings.SourceMaterialIds);
		}

		var sourceBoneInfo = FindBoneInfo(sourceModel, sourceRawMesh, "source");
		var targetBoneInfo = FindBoneInfo(targetModel, targetRawMesh, "target");
		var remapNames = BuildTargetRemapBoneNames(targetModel, targetBoneInfo);
		foreach (var group in outputSections)
		{
			var materialIndex = checked((int)group.First().TargetSection.MaterialIndex);
			EnsureMaterialIndex(remapNames, materialIndex);
			remapNames[materialIndex] = group.SelectMany(assignment => CollectSectionBoneHashes(assignment.SourceSection, sourceRawMesh, sourceBoneInfo, sourceModel.TransformNameHashes))
				.Distinct().OrderBy(hash => hash).Select(hash => hash.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		}
		var rebuiltBoneInfo = AttachTargetBoneMatrices(remapBuilder.SetRemap(targetBoneInfo, remapNames, targetModel.TransformNameHashes), BuildTargetMatrixMap(targetModel, targetRawMesh));
		var verticesWithBones = vertexSources.Select((source, index) => new UnitRawVertexRecord(
			(uint)index,
			EncodeTargetVertex(sourceRawMesh.Vertices[(int)source.sourceVertex], targetStream, sourceBoneInfo, rebuiltBoneInfo, sourceModel.TransformNameHashes, targetModel.TransformNameHashes, source.sourceMaterial, source.targetMaterial, meshSpaceTransform, transformMeshSpace),
			Array.Empty<UnitVertexComponentValue>())).ToArray();
		return CreateRebuiltResult(targetModel, targetRawMesh, sourceMeshInfoIndex, rebuiltSections, verticesWithBones, rebuiltBoneInfo, materialBindings.SourceMaterialIds);

		uint GetOrAddVertex(uint sourceVertex, uint sourceMaterial, uint targetMaterial)
		{
			if (sourceVertex >= sourceRawMesh.Vertices.Count) throw new InvalidDataException("A source section references a vertex outside the source mesh.");
			var key = (sourceVertex, sourceMaterial);
			if (vertexKeys.TryGetValue(key, out var existing)) return existing;
			var next = checked((uint)vertexSources.Count);
			vertexKeys.Add(key, next);
			vertexSources.Add((sourceVertex, sourceMaterial, targetMaterial));
			return next;
		}
	}

	private static UnitRawVertexRecord EncodeUnskinnedVertex(UnitMeshModel sourceModel, UnitRawMeshData sourceMesh, uint sourceVertexIndex, UnitStreamInfo targetStream, uint outputIndex, Matrix4x4 meshSpaceTransform)
	{
		var source = sourceMesh.Vertices[(int)sourceVertexIndex];
		var data = new byte[targetStream.VertexStride];
		var cursor = 0;
		foreach (var component in targetStream.Components)
		{
			var size = checked((int)component.Size);
			var value = TransformSpatialComponent(FindComponent(source, component.Type, component.Index), component.Type, meshSpaceTransform);
			if (size > 0 && cursor + size <= data.Length && value is not null) WriteComponent(data.AsSpan(cursor, size), component.FormatName, value);
			cursor += size;
		}
		return new UnitRawVertexRecord(outputIndex, data, Array.Empty<UnitVertexComponentValue>());
	}

	private static SdkStyleMeshReencodeResult CreateRebuiltResult(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, int sourceMeshInfoIndex, IReadOnlyList<UnitRawMeshSectionData> sections, IReadOnlyList<UnitRawVertexRecord> vertices, UnitBoneInfo? rebuiltBoneInfo, IReadOnlyList<ulong> sourceMaterialIds)
	{
		var targetBoneInfoIndex = targetModel.BoneInfos.Count == 0 ? -1 : GetBoneInfoIndex(targetModel, targetRawMesh);
		var requires32BitIndices = targetModel.Streams.Single(stream => stream.Index == targetRawMesh.StreamIndex).IndexBufferType != 1 && vertices.Count > ushort.MaxValue;
		var rawMesh = targetRawMesh with { Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray(), Vertices = vertices };
		var model = targetModel with
		{
			BoneInfos = rebuiltBoneInfo is null || targetBoneInfoIndex < 0 ? targetModel.BoneInfos : targetModel.BoneInfos.Select((boneInfo, index) => index == targetBoneInfoIndex ? rebuiltBoneInfo : boneInfo).ToArray(),
			Streams = requires32BitIndices ? targetModel.Streams.Select(stream => stream.Index == targetRawMesh.StreamIndex ? stream with { IndexBufferType = 1 } : stream).ToArray() : targetModel.Streams,
			RawMeshData = targetModel.RawMeshData.Select(mesh => mesh.MeshInfoIndex == targetRawMesh.MeshInfoIndex ? rawMesh : mesh).ToArray()
		};
		var outputBoneInfo = rebuiltBoneInfo ?? (targetBoneInfoIndex < 0 ? new UnitBoneInfo(-1, 0, 0, 0, 0, 0, Array.Empty<uint>(), Array.Empty<UnitBoneRemap>()) : targetModel.BoneInfos[targetBoneInfoIndex]);
		return new SdkStyleMeshReencodeResult(model, targetRawMesh.MeshInfoIndex, sourceMeshInfoIndex, targetBoneInfoIndex, outputBoneInfo, sourceMaterialIds);
	}

	private static SdkStyleMeshReencodeResult ReencodeUnskinned(UnitMeshModel targetModel, UnitRawMeshData targetRawMesh, UnitRawMeshData sourceRawMesh, UnitStreamInfo targetStream, int sourceMeshInfoIndex, Matrix4x4 meshSpaceTransform)
	{
		var vertexIndexMap = BuildVertexIndexMap(sourceRawMesh);
		var sections = sourceRawMesh.Sections.Select((section, index) => new UnitRawMeshSectionData(
			targetRawMesh.Sections[index].MaterialIndex,
			targetRawMesh.Sections[index].MaterialSlotId,
			section.Triangles.Select(triangle => new UnitTriangleIndices(vertexIndexMap[triangle.A], vertexIndexMap[triangle.B], vertexIndexMap[triangle.C])).ToArray())).ToArray();
		var vertices = vertexIndexMap.OrderBy(pair => pair.Value).Select(pair =>
		{
			var source = sourceRawMesh.Vertices[(int)pair.Key];
			var data = new byte[targetStream.VertexStride];
			var cursor = 0;
			foreach (var component in targetStream.Components)
			{
				var size = checked((int)component.Size);
				var value = TransformSpatialComponent(FindComponent(source, component.Type, component.Index), component.Type, meshSpaceTransform);
				if (size > 0 && cursor + size <= data.Length && value is not null) WriteComponent(data.AsSpan(cursor, size), component.FormatName, value);
				cursor += size;
			}
			return new UnitRawVertexRecord(pair.Value, data, Array.Empty<UnitVertexComponentValue>());
		}).ToArray();
		var rawMesh = targetRawMesh with { Sections = sections, Triangles = sections.SelectMany(section => section.Triangles).ToArray(), Vertices = vertices };
		var model = targetModel with { RawMeshData = targetModel.RawMeshData.Select(mesh => mesh.MeshInfoIndex == targetRawMesh.MeshInfoIndex ? rawMesh : mesh).ToArray() };
		var boneInfoIndex = targetModel.BoneInfos.Count == 0 ? -1 : GetBoneInfoIndex(targetModel, targetRawMesh);
		var boneInfo = boneInfoIndex < 0 ? new UnitBoneInfo(-1, 0, 0, 0, 0, 0, Array.Empty<uint>(), Array.Empty<UnitBoneRemap>()) : targetModel.BoneInfos[boneInfoIndex];
		return new SdkStyleMeshReencodeResult(model, targetRawMesh.MeshInfoIndex, sourceMeshInfoIndex, boneInfoIndex, boneInfo, Array.Empty<ulong>());
	}

	private static UnitRawMeshData FindRawMesh(UnitMeshModel model, int meshInfoIndex, string role)
		=> model.RawMeshData.FirstOrDefault(mesh => mesh.MeshInfoIndex == meshInfoIndex)
			?? throw new KeyNotFoundException($"The {role} Unit does not contain mesh {meshInfoIndex}.");

	private static UnitStreamInfo FindStream(UnitMeshModel model, UnitRawMeshData mesh, string role)
		=> model.Streams.FirstOrDefault(stream => stream.Index == mesh.StreamIndex)
			?? throw new KeyNotFoundException($"The {role} Unit does not contain stream {mesh.StreamIndex}.");

	private static int GetBoneInfoIndex(UnitMeshModel model, UnitRawMeshData mesh)
		=> mesh.LodIndex >= 0 && mesh.LodIndex < model.BoneInfos.Count ? mesh.LodIndex : 0;

	private static UnitBoneInfo FindBoneInfo(UnitMeshModel model, UnitRawMeshData mesh, string role)
		=> model.BoneInfos.Count == 0
			? throw new InvalidDataException($"The {role} Unit has no BoneInfo records.")
			: model.BoneInfos[GetBoneInfoIndex(model, mesh)];

	private static List<IReadOnlyList<string>> BuildTargetRemapBoneNames(UnitMeshModel targetModel, UnitBoneInfo targetBoneInfo)
	{
		var result = new List<IReadOnlyList<string>>(targetBoneInfo.Remaps.Count);
		foreach (var remap in targetBoneInfo.Remaps.OrderBy(remap => remap.MaterialIndex))
		{
			result.Add(remap.FakeIndices
				.Where(index => index < targetBoneInfo.RealIndices.Count)
				.Select(index => targetBoneInfo.RealIndices[(int)index])
				.Where(index => index < targetModel.TransformNameHashes.Count)
				.Select(index => targetModel.TransformNameHashes[(int)index].ToString(System.Globalization.CultureInfo.InvariantCulture))
				.ToArray());
		}
		return result;
	}

	private static List<IReadOnlyList<string>> BuildSectionRemapBoneNames(UnitRawMeshData sourceRawMesh, UnitRawMeshData targetRawMesh, UnitBoneInfo sourceBoneInfo, IReadOnlyList<uint> sourceTransformHashes, UnitMeshModel targetModel)
	{
		var remapNames = BuildTargetRemapBoneNames(targetModel, FindBoneInfo(targetModel, targetRawMesh, "target"));
		for (var sectionIndex = 0; sectionIndex < sourceRawMesh.Sections.Count; sectionIndex++)
		{
			var targetMaterialIndex = checked((int)targetRawMesh.Sections[sectionIndex].MaterialIndex);
			EnsureMaterialIndex(remapNames, targetMaterialIndex);
			remapNames[targetMaterialIndex] = CollectSectionBoneHashes(
				sourceRawMesh.Sections[sectionIndex],
				sourceRawMesh,
				sourceBoneInfo,
				sourceTransformHashes).Select(hash => hash.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
		}
		return remapNames;
	}

	private static UnitBoneInfo BuildCompleteSourceOrderedBoneInfo(UnitBoneInfo sourceBoneInfo, IReadOnlyList<uint> sourceTransformHashes, IReadOnlyList<uint> targetTransformHashes)
	{
		var sourceRealIndexPositions = new Dictionary<uint, uint>();
		var sourceToTargetRealIndices = new List<uint>(sourceBoneInfo.RealIndices.Count);
		for (var sourceRealIndexPosition = 0; sourceRealIndexPosition < sourceBoneInfo.RealIndices.Count; sourceRealIndexPosition++)
		{
			var sourceTransformIndex = sourceBoneInfo.RealIndices[sourceRealIndexPosition];
			if (sourceTransformIndex >= sourceTransformHashes.Count) throw new InvalidDataException("A source BoneInfo real index is absent from TransformInfo.");
			var targetTransformIndex = IndexOf(targetTransformHashes, sourceTransformHashes[(int)sourceTransformIndex]);
			// A source LOD palette can include unused rig bones which are not carried by
			// the current target shell. Active influences are still checked while vertices
			// are encoded; only these unreachable, inactive palette entries are omitted.
			if (targetTransformIndex < 0) continue;
			if (sourceToTargetRealIndices.Contains(checked((uint)targetTransformIndex))) throw new InvalidDataException("Source BoneInfo resolves multiple real bones to the same current target TransformInfo index.");
			sourceRealIndexPositions.Add(checked((uint)sourceRealIndexPosition), checked((uint)sourceToTargetRealIndices.Count));
			sourceToTargetRealIndices.Add(checked((uint)targetTransformIndex));
		}

		var remapOffset = checked((uint)(4 + sourceBoneInfo.Remaps.Count * 8));
		var remaps = new List<UnitBoneRemap>(sourceBoneInfo.Remaps.Count);
		foreach (var sourceRemap in sourceBoneInfo.Remaps.OrderBy(remap => remap.MaterialIndex))
		{
			if (sourceRemap.FakeIndices.Any(index => index >= sourceBoneInfo.RealIndices.Count)) throw new InvalidDataException("A source BoneInfo remap points outside its real-index table.");
			var fakeIndices = sourceRemap.FakeIndices
				.Where(sourceRealIndexPositions.ContainsKey)
				.Select(sourceRealIndexPosition => sourceRealIndexPositions[sourceRealIndexPosition])
				.ToArray();
			remaps.Add(new UnitBoneRemap(sourceRemap.MaterialIndex, remapOffset, fakeIndices));
			remapOffset = checked(remapOffset + (uint)(fakeIndices.Length * sizeof(uint)));
		}

		return new UnitBoneInfo(
			sourceBoneInfo.Index,
			sourceBoneInfo.Offset,
			checked((uint)sourceToTargetRealIndices.Count),
			sourceBoneInfo.MatrixOffset,
			sourceBoneInfo.RealIndicesOffset,
			sourceBoneInfo.RemapDataOffset,
			sourceToTargetRealIndices,
			remaps);
	}

	private IReadOnlyDictionary<uint, byte[]> BuildTargetMatrixMap(UnitMeshModel targetModel, UnitRawMeshData targetMesh)
	{
		if (!transformMeshSpace && !rebuildTargetInverseJointMatrices)
		{
			var existing = new Dictionary<uint, byte[]>();
			foreach (var boneInfo in targetModel.BoneInfos)
			{
				for (var index = 0; index < Math.Min(boneInfo.RealIndices.Count, boneInfo.BoneMatrices.Count); index++) existing.TryAdd(boneInfo.RealIndices[index], boneInfo.BoneMatrices[index]);
			}
			return existing;
		}
		var meshInfo = targetModel.Meshes.FirstOrDefault(mesh => mesh.Index == targetMesh.MeshInfoIndex)
			?? throw new KeyNotFoundException($"The target Unit does not contain MeshInfo {targetMesh.MeshInfoIndex}.");
		if (meshInfo.TransformIndex >= targetModel.TransformInfo.Matrices.Count) throw new InvalidDataException("The target mesh TransformInfo matrix is absent.");
		var meshMatrix = ToMatrix(targetModel.TransformInfo.Matrices[(int)meshInfo.TransformIndex]);
		if (!Matrix4x4.Invert(meshMatrix, out var inverseMesh)) throw new InvalidDataException("The target mesh TransformInfo matrix is not invertible.");
		var result = new Dictionary<uint, byte[]>();
		for (var index = 0; index < targetModel.TransformInfo.Matrices.Count; index++)
		{
			var boneMatrix = ToMatrix(targetModel.TransformInfo.Matrices[index]);
			var relative = boneMatrix * inverseMesh;
			if (!Matrix4x4.Invert(relative, out var inverseRelative)) throw new InvalidDataException($"Target TransformInfo matrix {index} is not invertible relative to its mesh.");
			result.Add(checked((uint)index), SerializeMatrix(inverseRelative));
		}
		return result;
	}

	private static Matrix4x4 ToMatrix(UnitTransformMatrix matrix)
	{
		var values = matrix.Values;
		if (values.Count != 16) throw new InvalidDataException("A target TransformInfo matrix does not contain 16 floats.");
		return new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
	}

	private static byte[] SerializeMatrix(Matrix4x4 matrix)
	{
		var values = new[] { matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24, matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44 };
		var data = new byte[64];
		for (var index = 0; index < values.Length; index++) BitConverter.GetBytes(values[index]).CopyTo(data, index * 4);
		return data;
	}

	private static UnitBoneInfo AttachTargetBoneMatrices(UnitBoneInfo boneInfo, IReadOnlyDictionary<uint, byte[]> matrixByTransformIndex)
	{
		var matrices = boneInfo.RealIndices.Select(transformIndex =>
			matrixByTransformIndex.TryGetValue(transformIndex, out var matrix) && matrix.Length == 64
				? matrix
				: throw new InvalidDataException($"No current-target inverse joint matrix exists for transform index {transformIndex}.")).ToArray();
		return boneInfo with { BoneMatrices = matrices };
	}

	private static SourceMaterialBindings ApplySourceMaterialBindings(
		IReadOnlyList<UnitMaterialBinding> targetBindings,
		UnitRawMeshData targetRawMesh,
		UnitRawMeshData sourceRawMesh,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex,
		IReadOnlySet<ulong>? allowedSourceMaterialIds)
	{
		var sourceMesh = sourceModel.Meshes.FirstOrDefault(mesh => mesh.Index == sourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"The source Unit does not contain mesh {sourceMeshInfoIndex}.");
		var replacementByTargetSlot = new Dictionary<uint, ulong>();
		for (var index = 0; index < sourceRawMesh.Sections.Count; index++)
		{
			var sourceSection = sourceRawMesh.Sections[index];
			if (sourceSection.MaterialIndex >= sourceMesh.MaterialSlotIds.Count) throw new InvalidDataException("A source mesh section material index is outside its material-slot table.");
			var sourceSlot = sourceMesh.MaterialSlotIds[(int)sourceSection.MaterialIndex];
			var materialIds = sourceModel.Materials.Where(binding => binding.SectionId == sourceSlot).Select(binding => binding.MaterialId).Distinct().ToArray();
			if (materialIds.Length != 1) throw new InvalidDataException("A source mesh material slot does not resolve to exactly one Material asset.");
			if (allowedSourceMaterialIds is not null && !allowedSourceMaterialIds.Contains(materialIds[0])) continue;
			var targetSlot = targetRawMesh.Sections[index].MaterialSlotId;
			if (replacementByTargetSlot.TryGetValue(targetSlot, out var existing) && existing != materialIds[0]) throw new InvalidDataException("Multiple source sections would assign different Material assets to the same current target material slot.");
			replacementByTargetSlot[targetSlot] = materialIds[0];
		}

		var result = targetBindings.Where(binding => !replacementByTargetSlot.ContainsKey(binding.SectionId)).ToList();
		result.AddRange(replacementByTargetSlot.OrderBy(pair => pair.Key).Select(pair => new UnitMaterialBinding(pair.Key, pair.Value)));
		return new SourceMaterialBindings(result, replacementByTargetSlot.Values.Distinct().OrderBy(id => id).ToArray());
	}

	private static SourceMaterialBindings ApplyRebuiltSectionMaterialBindings(
		IReadOnlyList<UnitMaterialBinding> targetBindings,
		IReadOnlyList<IGrouping<int, SectionAssignment>> outputSections,
		UnitMeshModel sourceModel,
		int sourceMeshInfoIndex,
		IReadOnlySet<ulong>? allowedSourceMaterialIds)
	{
		var sourceMesh = sourceModel.Meshes.FirstOrDefault(mesh => mesh.Index == sourceMeshInfoIndex)
			?? throw new KeyNotFoundException($"The source Unit does not contain mesh {sourceMeshInfoIndex}.");
		var replacementByTargetSlot = new Dictionary<uint, ulong>();
		foreach (var group in outputSections)
		{
			var materialIds = group
				.Select(assignment => ResolveSourceMaterialId(sourceModel, sourceMesh, assignment.SourceSection))
				.Distinct()
				.ToArray();
			if (materialIds.Length != 1 || allowedSourceMaterialIds is not null && !allowedSourceMaterialIds.Contains(materialIds[0])) continue;
			replacementByTargetSlot[group.First().TargetSection.MaterialSlotId] = materialIds[0];
		}
		var result = targetBindings.Where(binding => !replacementByTargetSlot.ContainsKey(binding.SectionId)).ToList();
		result.AddRange(replacementByTargetSlot.OrderBy(pair => pair.Key).Select(pair => new UnitMaterialBinding(pair.Key, pair.Value)));
		return new SourceMaterialBindings(result, replacementByTargetSlot.Values.Distinct().OrderBy(id => id).ToArray());
	}

	private static ulong ResolveSourceMaterialId(UnitMeshModel sourceModel, UnitMeshInfo sourceMesh, UnitRawMeshSectionData sourceSection)
	{
		if (sourceSection.MaterialIndex >= sourceMesh.MaterialSlotIds.Count) throw new InvalidDataException("A source mesh section material index is outside its material-slot table.");
		var sourceSlot = sourceMesh.MaterialSlotIds[(int)sourceSection.MaterialIndex];
		var materialIds = sourceModel.Materials.Where(binding => binding.SectionId == sourceSlot).Select(binding => binding.MaterialId).Distinct().ToArray();
		if (materialIds.Length != 1) throw new InvalidDataException("A source mesh material slot does not resolve to exactly one Material asset.");
		return materialIds[0];
	}

	private static void EnsureMaterialIndex(List<IReadOnlyList<string>> remapNames, int materialIndex)
	{
		while (remapNames.Count <= materialIndex)
		{
			remapNames.Add(Array.Empty<string>());
		}
	}

	private static IReadOnlyDictionary<uint, VertexMaterialIndexes> BuildVertexMaterialMap(UnitRawMeshData sourceMesh, UnitRawMeshData targetMesh)
	{
		var result = new Dictionary<uint, VertexMaterialIndexes>();
		for (var sectionIndex = 0; sectionIndex < sourceMesh.Sections.Count; sectionIndex++)
		{
			var section = sourceMesh.Sections[sectionIndex];
			var targetMaterialIndex = targetMesh.Sections[sectionIndex].MaterialIndex;
			foreach (var triangle in section.Triangles)
			{
				AssignVertexMaterial(result, triangle.A, section.MaterialIndex, targetMaterialIndex);
				AssignVertexMaterial(result, triangle.B, section.MaterialIndex, targetMaterialIndex);
				AssignVertexMaterial(result, triangle.C, section.MaterialIndex, targetMaterialIndex);
			}
		}
		return result;
	}

	private static IReadOnlyDictionary<uint, uint> BuildVertexIndexMap(UnitRawMeshData mesh)
	{
		var result = new Dictionary<uint, uint>();
		foreach (var index in mesh.Sections.SelectMany(section => section.Triangles).SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }))
		{
			if (index >= mesh.Vertices.Count) throw new InvalidDataException("A source section references a vertex outside the source mesh.");
			if (!result.ContainsKey(index)) result.Add(index, checked((uint)result.Count));
		}
		return result;
	}

	private static void AssignVertexMaterial(IDictionary<uint, VertexMaterialIndexes> values, uint vertexIndex, uint sourceMaterialIndex, uint targetMaterialIndex)
	{
		var value = new VertexMaterialIndexes(sourceMaterialIndex, targetMaterialIndex);
		if (values.TryGetValue(vertexIndex, out var existing) && existing != value)
		{
			throw new InvalidDataException("SDK-style re-encoding requires vertices to belong to only one material section.");
		}
		values[vertexIndex] = value;
	}

	private static IReadOnlyList<uint> CollectSectionBoneHashes(UnitRawMeshSectionData section, UnitRawMeshData mesh, UnitBoneInfo sourceBoneInfo, IReadOnlyList<uint> sourceTransformHashes)
	{
		var hashes = new HashSet<uint>();
		foreach (var vertexIndex in section.Triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct())
		{
			if (vertexIndex >= mesh.Vertices.Count) throw new InvalidDataException("A source section references a vertex outside the source mesh.");
			var vertex = mesh.Vertices[(int)vertexIndex];
			var indices = FindComponent(vertex, 6)?.UIntValues ?? Array.Empty<uint>();
			var weights = FindComponent(vertex, 7)?.FloatValues ?? Array.Empty<float>();
			for (var influence = 0; influence < indices.Length; influence++)
			{
				if (influence < weights.Length && weights[influence] <= ActiveWeightThreshold) continue;
				hashes.Add(ResolveSourceBoneHash(indices[influence], section.MaterialIndex, sourceBoneInfo, sourceTransformHashes));
			}
		}
		return hashes.OrderBy(hash => hash).ToArray();
	}

	private static byte[] EncodeTargetVertex(UnitRawVertexRecord sourceVertex, UnitStreamInfo targetStream, UnitBoneInfo sourceBoneInfo, UnitBoneInfo rebuiltTargetBoneInfo, IReadOnlyList<uint> sourceTransformHashes, IReadOnlyList<uint> targetTransformHashes, uint sourceMaterialIndex, uint targetMaterialIndex, Matrix4x4 meshSpaceTransform, bool requireCompleteBoneRemap)
	{
		var data = new byte[checked((int)targetStream.VertexStride)];
		var cursor = 0;
		foreach (var targetComponent in targetStream.Components)
		{
			var size = checked((int)targetComponent.Size);
			if (size <= 0 || cursor + size > data.Length) throw new InvalidDataException("A target stream component is outside its vertex stride.");
			var destination = data.AsSpan(cursor, size);
			var sourceComponent = TransformVertexComponent(FindComponent(sourceVertex, targetComponent.Type, targetComponent.Index), targetComponent.Type, meshSpaceTransform);
			if (targetComponent.Type == 6)
			{
				if (sourceComponent is not null)
				{
					var sourceWeights = FindComponent(sourceVertex, 7, targetComponent.Index)?.FloatValues ?? Array.Empty<float>();
					WriteBoneIndices(destination, targetComponent, sourceComponent, sourceWeights, sourceMaterialIndex, targetMaterialIndex, sourceBoneInfo, rebuiltTargetBoneInfo, sourceTransformHashes, targetTransformHashes, requireCompleteBoneRemap);
				}
			}
			else if (targetComponent.Type == 7)
			{
				if (sourceComponent is not null) WriteFloatValues(destination, targetComponent.FormatName, sourceComponent.FloatValues);
			}
			else if (sourceComponent is not null)
			{
				WriteComponent(destination, targetComponent.FormatName, sourceComponent);
			}
			cursor += size;
		}
		return data;
	}

	private static Matrix4x4 BuildMeshSpaceTransform(UnitMeshModel targetModel, int targetMeshInfoIndex, UnitMeshModel sourceModel, int sourceMeshInfoIndex)
	{
		var targetMesh = targetModel.Meshes.FirstOrDefault(mesh => mesh.Index == targetMeshInfoIndex) ?? throw new KeyNotFoundException($"The target Unit does not contain MeshInfo {targetMeshInfoIndex}.");
		var sourceMesh = sourceModel.Meshes.FirstOrDefault(mesh => mesh.Index == sourceMeshInfoIndex) ?? throw new KeyNotFoundException($"The source Unit does not contain MeshInfo {sourceMeshInfoIndex}.");
		var targetMatrix = GetTransformMatrix(targetModel, targetMesh.TransformIndex, "target");
		var sourceMatrix = GetTransformMatrix(sourceModel, sourceMesh.TransformIndex, "source");
		if (!Matrix4x4.Invert(targetMatrix, out var inverseTarget)) throw new InvalidDataException("The target mesh TransformInfo matrix is not invertible.");
		return sourceMatrix * inverseTarget;
	}

	private static UnitVertexComponentValue? TransformVertexComponent(UnitVertexComponentValue? component, uint type, Matrix4x4 transform)
		// The target inverse-joint matrices and positions must share mesh space. Surface
		// vectors stay in their source encoding until the SDK tangent-basis conversion is
		// reproduced byte-for-byte; generic normal transforms caused chest artifacts.
		=> type == 0 ? TransformSpatialComponent(component, type, transform) : component;

	private static Matrix4x4 GetTransformMatrix(UnitMeshModel model, uint transformIndex, string role)
	{
		if (transformIndex >= model.TransformInfo.Matrices.Count) throw new InvalidDataException($"The {role} MeshInfo transform index {transformIndex} is absent from TransformInfo.");
		var values = model.TransformInfo.Matrices[(int)transformIndex].Values;
		if (values.Count != 16) throw new InvalidDataException($"The {role} TransformInfo matrix does not contain 16 floats.");
		return new Matrix4x4(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8], values[9], values[10], values[11], values[12], values[13], values[14], values[15]);
	}

	private static UnitVertexComponentValue? TransformSpatialComponent(UnitVertexComponentValue? component, uint type, Matrix4x4 transform)
	{
		if (component is null || type > 3 || component.FloatValues.Length < 3 || transform == Matrix4x4.Identity) return component;
		var input = new Vector3(component.FloatValues[0], component.FloatValues[1], component.FloatValues[2]);
		Vector3 output;
		if (type == 0)
		{
			output = Vector3.Transform(input, transform);
		}
		else if (type == 1)
		{
			if (!Matrix4x4.Invert(transform, out var inverse)) throw new InvalidDataException("The source-to-target mesh transform is not invertible for normal conversion.");
			output = Vector3.Normalize(Vector3.TransformNormal(input, inverse));
		}
		else
		{
			output = Vector3.Normalize(Vector3.TransformNormal(input, transform));
		}
		var values = component.FloatValues.ToArray();
		values[0] = output.X; values[1] = output.Y; values[2] = output.Z;
		return component with { FormatName = string.Empty, FloatValues = values, RawData = Array.Empty<byte>() };
	}

	private static UnitVertexComponentValue? FindComponent(UnitRawVertexRecord vertex, uint type, uint? index = null)
		=> vertex.Components.FirstOrDefault(component => component.Type == type && (!index.HasValue || component.Index == index.Value));

	private static void WriteBoneIndices(Span<byte> destination, UnitStreamComponentInfo targetComponent, UnitVertexComponentValue? sourceComponent, IReadOnlyList<float> sourceWeights, uint sourceMaterialIndex, uint targetMaterialIndex, UnitBoneInfo sourceBoneInfo, UnitBoneInfo targetBoneInfo, IReadOnlyList<uint> sourceTransformHashes, IReadOnlyList<uint> targetTransformHashes, bool requireCompleteBoneRemap)
	{
		var sourceIndices = sourceComponent?.UIntValues ?? Array.Empty<uint>();
		var output = new uint[4];
		for (var influence = 0; influence < output.Length; influence++)
		{
			if (influence >= sourceWeights.Count || sourceWeights[influence] <= ActiveWeightThreshold)
			{
				output[influence] = 0;
				continue;
			}
			var sourceIndex = influence < sourceIndices.Length ? sourceIndices[influence] : 0;
			try
			{
				var hash = ResolveSourceBoneHash(sourceIndex, sourceMaterialIndex, sourceBoneInfo, sourceTransformHashes);
				var targetTransformIndex = IndexOf(targetTransformHashes, hash);
				output[influence] = targetTransformIndex < 0
					? 0
					: GetTargetRemappedIndex(checked((uint)targetTransformIndex), targetMaterialIndex, targetBoneInfo);
			}
			catch (InvalidDataException) when (!requireCompleteBoneRemap)
			{
				// HD2SDK GetMeshData catches absent remaps and writes index 0 for that influence.
				output[influence] = 0;
			}
		}
		if (targetComponent.FormatName == "vec4_uint8")
		{
			for (var i = 0; i < 4; i++) destination[i] = checked((byte)Math.Min(output[i], byte.MaxValue));
		}
		else if (targetComponent.FormatName == "vec4_uint32")
		{
			for (var i = 0; i < 4; i++) BitConverter.GetBytes(output[i]).CopyTo(destination[(i * 4)..]);
		}
		else
		{
			throw new InvalidDataException($"Unsupported target bone-index format '{targetComponent.FormatName}'.");
		}
	}

	private static uint ResolveSourceBoneHash(uint sourceFakeIndex, uint sourceMaterialIndex, UnitBoneInfo sourceBoneInfo, IReadOnlyList<uint> sourceTransformHashes)
	{
		if (sourceMaterialIndex >= sourceBoneInfo.Remaps.Count) throw new InvalidDataException("The source material has no BoneInfo remap.");
		var remap = sourceBoneInfo.Remaps[(int)sourceMaterialIndex];
		if (sourceFakeIndex >= remap.FakeIndices.Count) throw new InvalidDataException("A source vertex bone index is outside its source material remap.");
		var realIndexPosition = remap.FakeIndices[(int)sourceFakeIndex];
		if (realIndexPosition >= sourceBoneInfo.RealIndices.Count) throw new InvalidDataException("A source BoneInfo remap points outside its real-index table.");
		var transformIndex = sourceBoneInfo.RealIndices[(int)realIndexPosition];
		if (transformIndex >= sourceTransformHashes.Count) throw new InvalidDataException("A source BoneInfo real index is absent from TransformInfo.");
		return sourceTransformHashes[(int)transformIndex];
	}

	private static uint GetTargetRemappedIndex(uint targetTransformIndex, uint materialIndex, UnitBoneInfo targetBoneInfo)
	{
		if (materialIndex >= targetBoneInfo.Remaps.Count) throw new InvalidDataException("The rebuilt target material has no BoneInfo remap.");
		var realIndexPosition = IndexOf(targetBoneInfo.RealIndices, targetTransformIndex);
		if (realIndexPosition < 0) throw new InvalidDataException("The rebuilt target BoneInfo does not contain the requested real bone index.");
		var remappedIndex = IndexOf(targetBoneInfo.Remaps[(int)materialIndex].FakeIndices, checked((uint)realIndexPosition));
		if (remappedIndex < 0) throw new InvalidDataException("The rebuilt target material remap does not contain the requested bone.");
		return checked((uint)remappedIndex);
	}

	private static void WriteComponent(Span<byte> destination, string format, UnitVertexComponentValue source)
	{
		if (source.FormatName == format && source.RawData.Length == destination.Length)
		{
			source.RawData.CopyTo(destination);
			return;
		}
		WriteFloatValues(destination, format, source.FloatValues);
	}

	private static void WriteFloatValues(Span<byte> destination, string format, IReadOnlyList<float> values)
	{
		float Get(int index, float fallback = 0f) => index < values.Count ? values[index] : fallback;
		switch (format)
		{
			case "float": BitConverter.GetBytes(Get(0)).CopyTo(destination); break;
			case "vec2_float": WriteSingles(destination, Get(0), Get(1)); break;
			case "vec3_float": WriteSingles(destination, Get(0), Get(1), Get(2)); break;
			case "vec4_float": WriteSingles(destination, Get(0), Get(1), Get(2), Get(3)); break;
			case "vec2_half": WriteHalves(destination, Get(0), Get(1)); break;
			case "vec4_half": WriteHalves(destination, Get(0), Get(1), Get(2), Get(3)); break;
			case "vec4_1010102": BitConverter.GetBytes(EncodeTenBitUnsigned(Get(0), Get(1), Get(2), Get(3, 1f))).CopyTo(destination); break;
			case "unk_normal": BitConverter.GetBytes(EncodePackedOctNormal(Get(0), Get(1), Get(2, 1f))).CopyTo(destination); break;
		}
	}

	private static void WriteSingles(Span<byte> destination, params float[] values)
	{
		for (var i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(destination[(i * 4)..]);
	}

	private static void WriteHalves(Span<byte> destination, params float[] values)
	{
		for (var i = 0; i < values.Length; i++) BitConverter.GetBytes((Half)values[i]).CopyTo(destination[(i * 2)..]);
	}

	private static uint EncodeTenBitUnsigned(float x, float y, float z, float w)
		=> EncodeBits(x, 10) | (EncodeBits(y, 10) << 10) | (EncodeBits(z, 10) << 20) | (EncodeBits(w, 2) << 30);

	private static uint EncodePackedOctNormal(float x, float y, float z)
	{
		var length = MathF.Abs(x) + MathF.Abs(y) + MathF.Abs(z);
		if (length <= float.Epsilon) return 0;
		x /= length; y /= length; z /= length;
		if (z < 0f)
		{
			var oldX = x;
			x = (1f - MathF.Abs(y)) * MathF.Sign(oldX);
			y = (1f - MathF.Abs(oldX)) * MathF.Sign(y);
		}
		return EncodeBits(x * .5f + .5f, 16) | (EncodeBits(y * .5f + .5f, 16) << 16);
	}

	private static uint EncodeBits(float value, int bits)
		=> checked((uint)Math.Clamp(MathF.Round(Math.Clamp(value, 0f, 1f) * ((1 << bits) - 1)), 0, (1 << bits) - 1));

	private static int IndexOf(IReadOnlyList<uint> values, uint value)
	{
		for (var index = 0; index < values.Count; index++) if (values[index] == value) return index;
		return -1;
	}

	private readonly record struct VertexMaterialIndexes(uint Source, uint Target);
	private sealed record SectionAssignment(int SourceSectionIndex, UnitRawMeshSectionData SourceSection, UnitRawMeshSectionData TargetSection, int TargetSectionIndex);
	private sealed record SourceMaterialBindings(IReadOnlyList<UnitMaterialBinding> Bindings, IReadOnlyList<ulong> SourceMaterialIds);
}

public sealed record SdkStyleMeshReencodeResult(
	UnitMeshModel Model,
	int TargetMeshInfoIndex,
	int SourceMeshInfoIndex,
	int TargetBoneInfoIndex,
	UnitBoneInfo RebuiltTargetBoneInfo,
	IReadOnlyList<ulong> SourceMaterialIds);