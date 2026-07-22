
namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// 浣滅敤锛氭妸 UnitMeshModel 鐨?RawMesh 鏁版嵁鍐欏洖 Unit TocData 涓?GPU sidecar锛屼綔涓鸿嚜鍔ㄤ慨澶嶉噸瀵煎嚭鐨勫熀纭€銆?
// Purpose: Writes UnitMeshModel RawMesh data back to Unit TocData and GPU sidecar data as the basis for automated repair export.
public sealed class UnitMeshWriter
{
	private const uint UnsupportedOffset = 0;
	private readonly bool allowBoneInfoRelocation;
	private readonly bool allowTransformInfoRelocation;

	public UnitMeshWriter(bool allowBoneInfoRelocation = false, bool allowTransformInfoRelocation = false)
	{
		this.allowBoneInfoRelocation = allowBoneInfoRelocation;
		this.allowTransformInfoRelocation = allowTransformInfoRelocation;
	}

	public UnitMeshWriteResult Write(UnitMeshModel model, ReadOnlySpan<byte> originalTocData, ReadOnlySpan<byte> originalCompositeTocData = default)
	{
		var transformRelocation = allowTransformInfoRelocation
			? RelocateTransformInfo(model, originalTocData)
			: new MetadataRelocation(originalTocData.ToArray(), model);
		var relocation = allowBoneInfoRelocation
			? RelocateBoneInfos(transformRelocation.Model, transformRelocation.TocData)
			: new MetadataRelocation(transformRelocation.TocData, transformRelocation.Model);
		var indexSafeModel = PromoteSharedStreamIndexBuffers(relocation.Model);
		var (tocData, writableModel) = PrepareTocForExpandedMetadata(indexSafeModel, relocation.TocData);
		var meshTocData = tocData;
		byte[]? compositeTocData = null;
		if (writableModel.StreamInfoOffset == UnsupportedOffset && !originalCompositeTocData.IsEmpty)
		{
			compositeTocData = originalCompositeTocData.ToArray();
			meshTocData = compositeTocData;
		}

		WriteMeshMaterialSlots(meshTocData, writableModel.Meshes);
		WriteMeshCullingBounds(meshTocData, writableModel.Meshes);
		WriteStreamLayouts(meshTocData, writableModel.Streams);
		WriteMaterialBindings(tocData, writableModel);
		WriteBoneInfos(tocData, writableModel);
		var gpuData = BuildGpuData(writableModel, meshTocData);
		return new UnitMeshWriteResult(tocData, gpuData, compositeTocData, compositeTocData is null ? null : gpuData);
	}

	private static void WriteMeshCullingBounds(byte[] tocData, IReadOnlyList<UnitMeshInfo> meshes)
	{
		foreach (var mesh in meshes)
		{
			if (mesh.CullingBounds.Values.Count == 0) continue;
			if (mesh.CullingBounds.Values.Count != 7) throw new InvalidDataException($"MeshInfo {mesh.Index} culling bounds do not contain 7 floats.");
			var offset = checked((int)mesh.Offset + 8);
			EnsureWritableRange(tocData, offset, 7 * sizeof(float), $"mesh {mesh.Index} culling bounds");
			for (var index = 0; index < 7; index++) WriteSingle(tocData, offset + index * sizeof(float), mesh.CullingBounds.Values[index]);
		}
	}

	private static UnitMeshModel PromoteSharedStreamIndexBuffers(UnitMeshModel model)
	{
		var streams = model.Streams.Select(stream =>
		{
			if (stream.IndexBufferType == 1) return stream;
			var requires32Bit = model.RawMeshData
				.Where(mesh => mesh.StreamIndex == (uint)stream.Index)
				.SelectMany(mesh => mesh.Triangles)
				.Any(triangle => Math.Max(triangle.A, Math.Max(triangle.B, triangle.C)) > ushort.MaxValue);
			return requires32Bit ? stream with { IndexBufferType = 1 } : stream;
		}).ToArray();
		return model with { Streams = streams };
	}

	private static (byte[] TocData, UnitMeshModel Model) PrepareTocForExpandedMetadata(UnitMeshModel model, ReadOnlySpan<byte> originalTocData)
	{
		if (model.StreamInfoOffset == UnsupportedOffset)
		{
			return (originalTocData.ToArray(), model);
		}

		var tocData = originalTocData.ToArray().ToList();
		var updatedMeshes = new List<UnitMeshInfo>(model.Meshes.Count);
		foreach (var mesh in model.Meshes)
		{
			var rawMesh = model.RawMeshData.FirstOrDefault(raw => raw.MeshInfoIndex == mesh.Index);
			var meshOffset = checked((int)mesh.Offset);
			var originalMaterialCount = checked((int)ReadUInt32(tocData.ToArray(), meshOffset + 104));
			var originalSectionCount = checked((int)ReadUInt32(tocData.ToArray(), meshOffset + 120));
			var effectiveSectionCount = Math.Max(mesh.Sections.Count, rawMesh?.Sections.Count ?? 0);
			var effectiveMaterialSlots = mesh.MaterialSlotIds
				.Concat(rawMesh?.Sections.Select(section => section.MaterialSlotId) ?? Array.Empty<uint>())
				.Distinct()
				.ToArray();
			var wantsExpansion = effectiveMaterialSlots.Length > originalMaterialCount || effectiveSectionCount > originalSectionCount || rawMesh is not null && rawMesh.Sections.Count != mesh.Sections.Count;
			if (wantsExpansion)
			{
				// MeshInfo records embed their slot and section tables; append a complete replacement record
				// and redirect only the documented mesh-info table entry.
				var replacementOffset = tocData.Count;
				EnsureWritableRange(tocData.ToArray(), meshOffset, 128, $"mesh {mesh.Index} record header");
				tocData.AddRange(tocData.Skip(meshOffset).Take(128));
				var materialOffset = replacementOffset + 128;
				for (var i = 0; i < effectiveMaterialSlots.Length; i++)
				{
					AppendUInt32(tocData, effectiveMaterialSlots[i]);
				}

				var sectionsOffset = tocData.Count;
				for (var i = 0; i < effectiveSectionCount; i++)
				{
					var section = i < mesh.Sections.Count
						? mesh.Sections[i]
						: mesh.Sections[^1] with { MaterialIndex = checked((uint)effectiveMaterialSlots.ToList().IndexOf(rawMesh!.Sections[i].MaterialSlotId)), MaterialSlotId = rawMesh.Sections[i].MaterialSlotId, NumIndices = checked((uint)(rawMesh.Sections[i].Triangles.Count * 3)) };
					AppendUInt32(tocData, section.MaterialIndex);
					AppendUInt32(tocData, section.VertexOffset);
					AppendUInt32(tocData, section.NumVertices);
					AppendUInt32(tocData, section.IndexOffset);
					AppendUInt32(tocData, section.NumIndices);
					AppendUInt32(tocData, section.GroupIndex);
				}

				var writableTocData = tocData.ToArray();
				WriteUInt32(writableTocData, replacementOffset + 104, checked((uint)effectiveMaterialSlots.Length));
				WriteUInt32(writableTocData, replacementOffset + 108, 128);
				WriteUInt32(writableTocData, replacementOffset + 120, checked((uint)effectiveSectionCount));
				WriteUInt32(writableTocData, replacementOffset + 124, checked((uint)(sectionsOffset - replacementOffset)));
				WriteUInt32(writableTocData, checked((int)model.MeshInfoOffset + 4 + mesh.Index * 4), checked((uint)(replacementOffset - model.MeshInfoOffset)));
				tocData = writableTocData.ToList();
				updatedMeshes.Add(mesh with
				{
					Offset = checked((uint)replacementOffset),
					NumMaterials = checked((uint)effectiveMaterialSlots.Length),
					MaterialOffset = checked((uint)materialOffset),
					NumSections = checked((uint)effectiveSectionCount),
					SectionsOffset = checked((uint)sectionsOffset),
					Sections = mesh.Sections.Select((section, index) => section with { Offset = checked((uint)(sectionsOffset + index * 24)) }).ToArray()
				});
				continue;
			}

			updatedMeshes.Add(mesh);
		}

		var materialsOffset = model.MaterialsOffset;
		if (model.MaterialsOffset != UnsupportedOffset && model.Materials.Count > 0)
		{
			var writableTocData = tocData.ToArray();
			var originalBindingCount = checked((int)ReadUInt32(writableTocData, checked((int)materialsOffset)));
			if (model.Materials.Count > originalBindingCount)
			{
				materialsOffset = checked((uint)tocData.Count);
				tocData.AddRange(BuildMaterialBindingPayload(model.Materials));
				writableTocData = tocData.ToArray();
				WriteUInt32(writableTocData, 0x70, materialsOffset);
				tocData = writableTocData.ToList();
			}
		}

		if (model.EndingOffset != UnsupportedOffset)
		{
			var writableTocData = tocData.ToArray();
			WriteUInt32(writableTocData, 0x60, checked((uint)tocData.Count));
			tocData = writableTocData.ToList();
			AppendUInt64(tocData, checked((ulong)model.Meshes.Count));
		}

		return (tocData.ToArray(), model with
		{
			MaterialsOffset = materialsOffset,
			Meshes = updatedMeshes
		});
	}

	private static void AppendUInt32(List<byte> data, uint value)
	{
		data.Add((byte)value);
		data.Add((byte)(value >> 8));
		data.Add((byte)(value >> 16));
		data.Add((byte)(value >> 24));
	}

	private static void WriteUInt16(byte[] data, int offset, ushort value)
	{
		EnsureWritableRange(data, offset, 2, "uint16");
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteSingle(byte[] data, int offset, float value)
		=> WriteUInt32(data, offset, unchecked((uint)BitConverter.SingleToInt32Bits(value)));

	private static void AppendUInt64(List<byte> data, ulong value)
	{
		AppendUInt32(data, unchecked((uint)value));
		AppendUInt32(data, unchecked((uint)(value >> 32)));
	}

	private static void WriteStreamLayouts(byte[] tocData, IReadOnlyList<UnitStreamInfo> streams)
	{
		foreach (var stream in streams)
		{
			if (stream.Offset == UnsupportedOffset) continue;
			EnsureWritableRange(tocData, checked((int)stream.Offset), 8, "stream ComponentInfoId");
			WriteUInt64(tocData, checked((int)stream.Offset), stream.ComponentInfoId);
			var componentOffset = checked((int)stream.Offset + 8);
			const int componentBlockSize = 320;
			if (stream.Components.Count * 20 > componentBlockSize) throw new InvalidDataException("A Unit stream has too many components for its StreamInfo component block.");
			EnsureWritableRange(tocData, componentOffset, componentBlockSize + 40, "stream layout");
			Array.Clear(tocData, componentOffset, componentBlockSize);
			for (var index = 0; index < stream.Components.Count; index++)
			{
				var component = stream.Components[index];
				var offset = componentOffset + index * 20;
				WriteUInt32(tocData, offset, component.Type);
				WriteUInt32(tocData, offset + 4, component.Format);
				WriteUInt32(tocData, offset + 8, component.Index);
				WriteUInt64(tocData, offset + 12, component.Unknown);
			}

			var headerOffset = componentOffset + componentBlockSize;
			WriteUInt64(tocData, headerOffset, checked((ulong)stream.Components.Count));
			WriteUInt32(tocData, headerOffset + 28, stream.VertexStride);
		}
	}

	private static byte[] BuildMaterialBindingPayload(IReadOnlyList<UnitMaterialBinding> materials)
	{
		var payload = new byte[checked(4 + materials.Count * 12)];
		WriteUInt32(payload, 0, checked((uint)materials.Count));
		for (var i = 0; i < materials.Count; i++)
		{
			WriteUInt32(payload, 4 + i * 4, materials[i].SectionId);
			WriteUInt64(payload, 4 + materials.Count * 4 + i * 8, materials[i].MaterialId);
		}
		return payload;
	}

	private static byte[] BuildGpuData(UnitMeshModel model, byte[] tocData)
	{
		var gpuData = new List<byte>();
		foreach (var stream in model.Streams)
		{
			var streamMeshes = model.RawMeshData
				.Where(mesh => mesh.StreamIndex == (uint)stream.Index)
				.ToArray();
			var vertexOrderedMeshes = streamMeshes
				.OrderBy(mesh => GetFirstVertexOffset(model, mesh))
				.ThenBy(mesh => mesh.MeshInfoIndex)
				.ToArray();
			var indexOrderedMeshes = streamMeshes
				.OrderBy(mesh => GetFirstIndexOffset(model, mesh))
				.ThenBy(mesh => mesh.MeshInfoIndex)
				.ToArray();

			var vertexBufferOffset = checked((uint)gpuData.Count);
			var vertexCount = 0u;
			foreach (var mesh in vertexOrderedMeshes)
			{
				foreach (var vertex in mesh.Vertices)
				{
					WriteVertex(gpuData, stream.VertexStride, vertex.Data);
					vertexCount++;
				}
			}

			var vertexBufferSize = checked((uint)gpuData.Count - vertexBufferOffset);
			PadToAlignment(gpuData, 16);
			var indexBufferOffset = checked((uint)gpuData.Count);
			var indexCount = 0u;
			var indexStride = stream.IndexBufferType == 1 ? 4 : 2;
			foreach (var mesh in indexOrderedMeshes)
			{
				foreach (var section in mesh.Sections)
				{
					foreach (var triangle in section.Triangles)
					{
						ValidateTriangleReferences(mesh, triangle);
						WriteIndex(gpuData, triangle.A, indexStride);
						WriteIndex(gpuData, triangle.B, indexStride);
						WriteIndex(gpuData, triangle.C, indexStride);
						indexCount += 3;
					}
				}
			}

			var indexBufferSize = checked((uint)gpuData.Count - indexBufferOffset);
			WriteStreamGpuFields(tocData, stream, vertexCount, vertexBufferOffset, vertexBufferSize, indexCount, indexBufferOffset, indexBufferSize);
			WriteMeshVertexOffsets(tocData, model, vertexOrderedMeshes);
			WriteMeshIndexOffsets(tocData, model, indexOrderedMeshes);
		}

		return gpuData.ToArray();
	}

	private static void PadToAlignment(List<byte> data, int alignment)
	{
		if (alignment <= 0)
		{
			return;
		}

		var padding = (alignment - data.Count % alignment) % alignment;
		for (var i = 0; i < padding; i++)
		{
			data.Add(0);
		}
	}

	private static void ValidateTriangleReferences(UnitRawMeshData mesh, UnitTriangleIndices triangle)
	{
		var vertexCount = checked((uint)mesh.Vertices.Count);
		if (triangle.A >= vertexCount || triangle.B >= vertexCount || triangle.C >= vertexCount)
		{
			throw new InvalidDataException("RawMeshData contains triangle indices that reference vertices outside the mesh vertex range.");
		}
	}

	private static uint GetFirstVertexOffset(UnitMeshModel model, UnitRawMeshData mesh)
	{
		var meshInfo = FindMeshInfo(model, mesh.MeshInfoIndex);
		return meshInfo?.Sections.Count > 0 ? meshInfo.Sections[0].VertexOffset : 0;
	}

	private static uint GetFirstIndexOffset(UnitMeshModel model, UnitRawMeshData mesh)
	{
		var meshInfo = FindMeshInfo(model, mesh.MeshInfoIndex);
		return meshInfo?.Sections.Count > 0 ? meshInfo.Sections[0].IndexOffset : 0;
	}

	private static void WriteMeshVertexOffsets(byte[] tocData, UnitMeshModel model, IReadOnlyList<UnitRawMeshData> rawMeshes)
	{
		var vertexOffset = 0u;
		foreach (var rawMesh in rawMeshes)
		{
			var meshInfo = FindMeshInfo(model, rawMesh.MeshInfoIndex);
			if (meshInfo is null)
			{
				continue;
			}

			if (rawMesh.Sections.Count > meshInfo.Sections.Count)
			{
				throw new InvalidDataException("RawMeshData contains more sections than the target Unit MeshInfo can describe.");
			}

			for (var i = 0; i < meshInfo.Sections.Count; i++)
			{
				var section = meshInfo.Sections[i];
				var rawSection = i < rawMesh.Sections.Count ? rawMesh.Sections[i] : null;
				WriteUInt32(tocData, section.Offset, rawSection?.MaterialIndex ?? section.MaterialIndex);
				WriteUInt32(tocData, section.Offset + 4, vertexOffset);
				WriteUInt32(tocData, section.Offset + 8, checked((uint)rawMesh.Vertices.Count));
			}

			vertexOffset = checked(vertexOffset + (uint)rawMesh.Vertices.Count);
		}
	}

	private static void WriteMeshIndexOffsets(byte[] tocData, UnitMeshModel model, IReadOnlyList<UnitRawMeshData> rawMeshes)
	{
		var indexOffset = 0u;
		foreach (var rawMesh in rawMeshes)
		{
			var meshInfo = FindMeshInfo(model, rawMesh.MeshInfoIndex);
			if (meshInfo is null) continue;
			if (rawMesh.Sections.Count > meshInfo.Sections.Count) throw new InvalidDataException("RawMeshData contains more sections than the target Unit MeshInfo can describe.");
			for (var i = 0; i < meshInfo.Sections.Count; i++)
			{
				var rawSection = i < rawMesh.Sections.Count ? rawMesh.Sections[i] : null;
				var numIndices = checked((uint)((rawSection?.Triangles.Count ?? 0) * 3));
				WriteUInt32(tocData, meshInfo.Sections[i].Offset + 12, indexOffset);
				WriteUInt32(tocData, meshInfo.Sections[i].Offset + 16, numIndices);
				indexOffset = checked(indexOffset + numIndices);
			}
		}
	}

	private static void WriteMeshMaterialSlots(byte[] tocData, IReadOnlyList<UnitMeshInfo> meshes)
	{
		foreach (var mesh in meshes)
		{
			if (mesh.MaterialSlotIds.Count == 0 || mesh.Sections.Count == 0)
			{
				continue;
			}

			var offset = checked((int)mesh.Sections[0].Offset - mesh.MaterialSlotIds.Count * 4);
			EnsureWritableRange(tocData, offset, checked(mesh.MaterialSlotIds.Count * 4), $"mesh {mesh.Index} material slots");
			for (var i = 0; i < mesh.MaterialSlotIds.Count; i++)
			{
				WriteUInt32(tocData, offset + i * 4, mesh.MaterialSlotIds[i]);
			}
		}
	}

	private static void WriteMaterialBindings(byte[] tocData, UnitMeshModel model)
	{
		if (model.MaterialsOffset == UnsupportedOffset || model.Materials.Count == 0)
		{
			return;
		}

		var offset = checked((int)model.MaterialsOffset);
		EnsureWritableRange(tocData, offset, 4, "unit material binding count");
		var count = checked((int)ReadUInt32(tocData, offset));
		if (count != model.Materials.Count)
		{
			throw new InvalidDataException("Cannot rewrite Unit material bindings because the edited material binding count differs from the original payload.");
		}

		var sectionIdsOffset = offset + 4;
		var materialIdsOffset = sectionIdsOffset + count * 4;
		EnsureWritableRange(tocData, sectionIdsOffset, checked(count * 12), "unit material bindings");
		for (var i = 0; i < count; i++)
		{
			WriteUInt32(tocData, sectionIdsOffset + i * 4, model.Materials[i].SectionId);
			WriteUInt64(tocData, materialIdsOffset + i * 8, model.Materials[i].MaterialId);
		}
	}

	private static void WriteBoneInfos(byte[] tocData, UnitMeshModel model)
	{
		if (model.BoneInfoOffset == UnsupportedOffset || model.BoneInfos.Count == 0)
		{
			return;
		}

		var boneInfoOffset = checked((int)model.BoneInfoOffset);
		EnsureWritableRange(tocData, boneInfoOffset, 4 + model.BoneInfos.Count * 4, "bone info table");
		var storedCount = checked((int)ReadUInt32(tocData, boneInfoOffset));
		if (storedCount != model.BoneInfos.Count)
		{
			throw new InvalidDataException("Cannot rewrite BoneInfo because its record count differs from the current target payload.");
		}

		var matrixByTransformIndex = BuildMatrixMap(model.BoneInfos);

		for (var index = 0; index < model.BoneInfos.Count; index++)
		{
			var recordStart = checked(boneInfoOffset + (int)ReadUInt32(tocData, boneInfoOffset + 4 + index * 4));
			var recordEnd = index + 1 < model.BoneInfos.Count
				? checked(boneInfoOffset + (int)ReadUInt32(tocData, boneInfoOffset + 4 + (index + 1) * 4))
				: checked((int)(model.StreamInfoOffset == UnsupportedOffset ? model.MeshInfoOffset : model.StreamInfoOffset));
			var payload = SerializeBoneInfo(model.BoneInfos[index], matrixByTransformIndex);
			if (payload.Length > recordEnd - recordStart)
			{
				throw new InvalidDataException($"Rebuilt BoneInfo {index} needs {payload.Length} bytes but the current target record has {recordEnd - recordStart} bytes.");
			}
			payload.CopyTo(tocData.AsSpan(recordStart, payload.Length));
			tocData.AsSpan(recordStart + payload.Length, recordEnd - recordStart - payload.Length).Clear();
		}
	}

	internal static byte[] SerializeBoneInfo(UnitBoneInfo boneInfo, IReadOnlyDictionary<uint, byte[]> matrixByTransformIndex)
	{
		var count = boneInfo.RealIndices.Count;
		var matrixOffset = 16;
		var realIndicesOffset = checked(matrixOffset + count * 64);
		var remapDataOffset = checked(realIndicesOffset + count * 4);
		var remapTableSize = checked(4 + boneInfo.Remaps.Count * 8);
		var remapValuesSize = checked(boneInfo.Remaps.Sum(remap => remap.FakeIndices.Count) * 4);
		var payload = new byte[checked(remapDataOffset + remapTableSize + remapValuesSize)];
		WriteUInt32(payload, 0, checked((uint)count));
		WriteUInt32(payload, 4, checked((uint)matrixOffset));
		WriteUInt32(payload, 8, checked((uint)realIndicesOffset));
		WriteUInt32(payload, 12, checked((uint)remapDataOffset));
		for (var index = 0; index < count; index++)
		{
			var matrix = index < boneInfo.BoneMatrices.Count
				? boneInfo.BoneMatrices[index]
				: matrixByTransformIndex.TryGetValue(boneInfo.RealIndices[index], out var fallback) ? fallback : null;
			if (matrix is null || matrix.Length != 64)
			{
				throw new InvalidDataException($"No current-target inverse joint matrix exists for transform index {boneInfo.RealIndices[index]}.");
			}
			matrix.CopyTo(payload.AsSpan(matrixOffset + index * 64, 64));
			WriteUInt32(payload, realIndicesOffset + index * 4, boneInfo.RealIndices[index]);
		}

		WriteUInt32(payload, remapDataOffset, checked((uint)boneInfo.Remaps.Count));
		var valuesOffset = remapTableSize;
		for (var index = 0; index < boneInfo.Remaps.Count; index++)
		{
			var remap = boneInfo.Remaps[index];
			var tableOffset = remapDataOffset + 4 + index * 8;
			WriteUInt32(payload, tableOffset, checked((uint)valuesOffset));
			WriteUInt32(payload, tableOffset + 4, checked((uint)remap.FakeIndices.Count));
			foreach (var fakeIndex in remap.FakeIndices)
			{
				WriteUInt32(payload, remapDataOffset + valuesOffset, fakeIndex);
				valuesOffset += 4;
			}
		}
		return payload;
	}

	private static MetadataRelocation RelocateTransformInfo(UnitMeshModel model, ReadOnlySpan<byte> originalTocData)
	{
		if (model.TransformInfoOffset == UnsupportedOffset || model.TransformInfo.NameHashes.Count == 0) return new MetadataRelocation(originalTocData.ToArray(), model);
		var oldStart = checked((int)model.TransformInfoOffset);
		EnsureWritableRange(originalTocData.ToArray(), oldStart, 16, "current TransformInfo header");
		var oldCount = checked((int)ReadUInt32(originalTocData.ToArray(), oldStart));
		var oldLength = checked(16 + oldCount * 136);
		var oldEnd = checked(oldStart + oldLength);
		EnsureWritableRange(originalTocData.ToArray(), oldStart, oldLength, "current TransformInfo block");
		var replacement = SerializeTransformInfo(model.TransformInfo).ToList();
		PadToAlignment(replacement, 16);
		var alignedOldEnd = checked((oldEnd + 15) & ~15);
		EnsureWritableRange(originalTocData.ToArray(), oldEnd, alignedOldEnd - oldEnd, "current TransformInfo alignment");
		var delta = replacement.Count - (alignedOldEnd - oldStart);
		if (delta == 0)
		{
			var unchanged = originalTocData.ToArray();
			replacement.CopyTo(unchanged, oldStart);
			return new MetadataRelocation(unchanged, model);
		}
		var relocated = new byte[checked(originalTocData.Length + delta)];
		originalTocData[..oldStart].CopyTo(relocated);
		replacement.CopyTo(relocated, oldStart);
		originalTocData[alignedOldEnd..].CopyTo(relocated.AsSpan(oldStart + replacement.Count));
		foreach (var headerOffset in new[] { 0x4c, 0x50, 0x54, 0x58, 0x5c, 0x60, 0x64, 0x70 }) ShiftHeaderOffset(relocated, headerOffset, alignedOldEnd, delta, $"header field 0x{headerOffset:x}");
		return new MetadataRelocation(relocated, ShiftModelOffsets(model, alignedOldEnd, delta));
	}

	private static byte[] SerializeTransformInfo(UnitTransformInfo info)
	{
		var count = info.NameHashes.Count;
		if (info.LocalTransforms.Count != count || info.Matrices.Count != count || info.Entries.Count != count) throw new InvalidDataException("TransformInfo arrays have inconsistent counts.");
		var data = new byte[checked(16 + count * 136)];
		WriteUInt32(data, 0, checked((uint)count)); WriteUInt32(data, 4, info.Reserved0); WriteUInt32(data, 8, info.Reserved1); WriteUInt32(data, 12, info.Reserved2);
		var matrixOffset = 16 + count * 64;
		var entryOffset = matrixOffset + count * 64;
		var hashOffset = entryOffset + count * 4;
		for (var index = 0; index < count; index++)
		{
			var local = info.LocalTransforms[index];
			if (local.Rotation.Count != 9 || local.Position.Count != 3 || local.Scale.Count != 3) throw new InvalidDataException("A TransformInfo local transform has an invalid component count.");
			var cursor = 16 + index * 64;
			foreach (var value in local.Rotation.Concat(local.Position).Concat(local.Scale).Append(local.Padding)) { WriteSingle(data, cursor, value); cursor += 4; }
			if (info.Matrices[index].Values.Count != 16) throw new InvalidDataException("A TransformInfo matrix does not contain 16 floats.");
			cursor = matrixOffset + index * 64;
			foreach (var value in info.Matrices[index].Values) { WriteSingle(data, cursor, value); cursor += 4; }
			WriteUInt16(data, entryOffset + index * 4, info.Entries[index].Increment);
			WriteUInt16(data, entryOffset + index * 4 + 2, info.Entries[index].ParentIndex);
			WriteUInt32(data, hashOffset + index * 4, info.NameHashes[index]);
		}
		return data;
	}

	private static MetadataRelocation RelocateBoneInfos(UnitMeshModel model, ReadOnlySpan<byte> originalTocData)
	{
		if (model.BoneInfoOffset == UnsupportedOffset || model.BoneInfos.Count == 0)
		{
			return new MetadataRelocation(originalTocData.ToArray(), model);
		}

		if (model.StreamInfoOffset == UnsupportedOffset)
		{
			throw new InvalidDataException("Cross-armor BoneInfo relocation does not support Composite-backed Units yet.");
		}

		var oldBoneStart = checked((int)model.BoneInfoOffset);
		var oldBoneEnd = checked((int)model.StreamInfoOffset);
		EnsureWritableRange(originalTocData.ToArray(), oldBoneStart, oldBoneEnd - oldBoneStart, "current BoneInfo block");
		var matrixByTransformIndex = BuildMatrixMap(model.BoneInfos);
		var payloads = model.BoneInfos.Select(boneInfo => SerializeBoneInfo(boneInfo, matrixByTransformIndex)).ToArray();
		var replacement = new List<byte>(4 + payloads.Length * 4 + payloads.Sum(payload => payload.Length));
		AppendUInt32(replacement, checked((uint)payloads.Length));
		var relativeOffset = checked(4 + payloads.Length * 4);
		foreach (var payload in payloads)
		{
			AppendUInt32(replacement, checked((uint)relativeOffset));
			relativeOffset = checked(relativeOffset + payload.Length);
		}
		foreach (var payload in payloads) replacement.AddRange(payload);
		PadToAlignment(replacement, 16);

		var delta = replacement.Count - (oldBoneEnd - oldBoneStart);
		if (delta == 0)
		{
			var unchanged = originalTocData.ToArray();
			replacement.CopyTo(unchanged, oldBoneStart);
			return new MetadataRelocation(unchanged, model);
		}

		var relocated = new byte[checked(originalTocData.Length + delta)];
		originalTocData[..oldBoneStart].CopyTo(relocated);
		replacement.CopyTo(relocated, oldBoneStart);
		originalTocData[oldBoneEnd..].CopyTo(relocated.AsSpan(oldBoneStart + replacement.Count));
		ShiftHeaderOffset(relocated, 0x5c, oldBoneEnd, delta, "StreamInfoOffset");
		ShiftHeaderOffset(relocated, 0x60, oldBoneEnd, delta, "EndingOffset");
		ShiftHeaderOffset(relocated, 0x64, oldBoneEnd, delta, "MeshInfoOffset");
		ShiftHeaderOffset(relocated, 0x70, oldBoneEnd, delta, "MaterialsOffset");
		return new MetadataRelocation(relocated, ShiftModelOffsets(model, oldBoneEnd, delta));
	}

	private static IReadOnlyDictionary<uint, byte[]> BuildMatrixMap(IReadOnlyList<UnitBoneInfo> boneInfos)
	{
		var matrixByTransformIndex = new Dictionary<uint, byte[]>();
		foreach (var boneInfo in boneInfos)
		{
			for (var i = 0; i < Math.Min(boneInfo.RealIndices.Count, boneInfo.BoneMatrices.Count); i++)
			{
				matrixByTransformIndex.TryAdd(boneInfo.RealIndices[i], boneInfo.BoneMatrices[i]);
			}
		}
		return matrixByTransformIndex;
	}

	private static void ShiftHeaderOffset(byte[] tocData, int headerOffset, int threshold, int delta, string description)
	{
		var value = ReadUInt32(tocData, headerOffset);
		if (value != UnsupportedOffset && value >= threshold)
		{
			WriteUInt32(tocData, headerOffset, checked((uint)((int)value + delta)));
		}
		else if (value != UnsupportedOffset && value < threshold)
		{
			throw new InvalidDataException($"Cannot relocate BoneInfo because {description} precedes the BoneInfo successor block.");
		}
	}

	private static UnitMeshModel ShiftModelOffsets(UnitMeshModel model, int threshold, int delta)
	{
		uint Shift(uint value) => value != UnsupportedOffset && value >= threshold ? checked((uint)((int)value + delta)) : value;
		return model with
		{
			CustomizationInfoOffset = Shift(model.CustomizationInfoOffset),
			BoneInfoOffset = Shift(model.BoneInfoOffset),
			StreamInfoOffset = Shift(model.StreamInfoOffset),
			EndingOffset = Shift(model.EndingOffset),
			MeshInfoOffset = Shift(model.MeshInfoOffset),
			MaterialsOffset = Shift(model.MaterialsOffset),
			Streams = model.Streams.Select(stream => stream with { Offset = Shift(stream.Offset) }).ToArray(),
			Meshes = model.Meshes.Select(mesh => mesh with
			{
				Offset = Shift(mesh.Offset),
				MaterialOffset = Shift(mesh.MaterialOffset),
				SectionsOffset = Shift(mesh.SectionsOffset),
				Sections = mesh.Sections.Select(section => section with { Offset = Shift(section.Offset) }).ToArray()
			}).ToArray()
		};
	}
	private sealed record MetadataRelocation(byte[] TocData, UnitMeshModel Model);

	private static UnitMeshInfo? FindMeshInfo(UnitMeshModel model, int meshInfoIndex)
	{
		return model.Meshes.FirstOrDefault(mesh => mesh.Index == meshInfoIndex);
	}

	private static void WriteStreamGpuFields(byte[] tocData, UnitStreamInfo stream, uint vertexCount, uint vertexBufferOffset, uint vertexBufferSize, uint indexCount, uint indexBufferOffset, uint indexBufferSize)
	{
		if (stream.Offset == UnsupportedOffset)
		{
			return;
		}

		var cursor = checked((int)stream.Offset + 8 + 320);
		cursor += 24;
		WriteUInt32(tocData, cursor, vertexCount); cursor += 4;
		cursor += 4;
		cursor += 32;
		WriteUInt32(tocData, cursor, indexCount); cursor += 4;
		WriteUInt32(tocData, cursor, stream.IndexBufferType); cursor += 4;
		cursor += 16;
		WriteUInt32(tocData, cursor, vertexBufferOffset); cursor += 4;
		WriteUInt32(tocData, cursor, vertexBufferSize); cursor += 4;
		WriteUInt32(tocData, cursor, indexBufferOffset); cursor += 4;
		WriteUInt32(tocData, cursor, indexBufferSize);
	}

	private static void WriteVertex(List<byte> output, uint stride, byte[] vertexData)
	{
		var strideLength = checked((int)stride);
		if (vertexData.Length > strideLength)
		{
			throw new InvalidDataException("Raw vertex data is larger than the stream vertex stride.");
		}

		output.AddRange(vertexData);
		for (var i = vertexData.Length; i < strideLength; i++)
		{
			output.Add(0);
		}
	}

	private static void WriteIndex(List<byte> output, uint index, int stride)
	{
		if (stride == 2)
		{
			if (index > ushort.MaxValue)
			{
				throw new InvalidDataException("A 16-bit Unit index buffer cannot contain an index greater than 65535.");
			}

			output.Add((byte)index);
			output.Add((byte)(index >> 8));
			return;
		}

		output.Add((byte)index);
		output.Add((byte)(index >> 8));
		output.Add((byte)(index >> 16));
		output.Add((byte)(index >> 24));
	}

	private static void WriteUInt32(byte[] data, uint offset, uint value) => WriteUInt32(data, checked((int)offset), value);

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		EnsureWritableRange(data, offset, 4, "uint32");

		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteUInt64(byte[] data, int offset, ulong value)
	{
		WriteUInt32(data, offset, unchecked((uint)value));
		WriteUInt32(data, offset + 4, unchecked((uint)(value >> 32)));
	}

	private static uint ReadUInt32(byte[] data, int offset)
	{
		EnsureWritableRange(data, offset, 4, "uint32");
		return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
	}

	private static void EnsureWritableRange(byte[] data, int offset, int length, string description)
	{
		if (offset < 0 || length < 0 || offset > data.Length - length)
		{
			throw new InvalidDataException($"Unit TocData does not contain a valid {description} write range.");
		}
	}

}
