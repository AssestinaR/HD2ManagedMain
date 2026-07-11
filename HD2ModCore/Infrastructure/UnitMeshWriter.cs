using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：把 UnitMeshModel 的 RawMesh 数据写回 Unit TocData 与 GPU sidecar，作为自动修复重导出的基础。
// Purpose: Writes UnitMeshModel RawMesh data back to Unit TocData and GPU sidecar data as the basis for automated repair export.
public sealed class UnitMeshWriter : IUnitMeshWriter
{
	private const uint UnsupportedOffset = 0;

	public UnitMeshWriteResult Write(UnitMeshModel model, ReadOnlySpan<byte> originalTocData, ReadOnlySpan<byte> originalCompositeTocData = default)
	{
		var (tocData, writableModel) = PrepareTocForExpandedMetadata(model, originalTocData);
		var meshTocData = tocData;
		byte[]? compositeTocData = null;
		if (writableModel.StreamInfoOffset == UnsupportedOffset && !originalCompositeTocData.IsEmpty)
		{
			compositeTocData = originalCompositeTocData.ToArray();
			meshTocData = compositeTocData;
		}

		WriteMeshMaterialSlots(meshTocData, writableModel.Meshes);
		WriteMaterialBindings(tocData, writableModel);
		var gpuData = BuildGpuData(writableModel, meshTocData);
		return new UnitMeshWriteResult(tocData, gpuData, compositeTocData, compositeTocData is null ? null : gpuData);
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
			var wantsExpansion = mesh.MaterialSlotIds.Count > originalMaterialCount || mesh.Sections.Count > originalSectionCount;
			if (wantsExpansion)
			{
				// MeshInfo records embed their slot and section tables; append a complete replacement record
				// and redirect only the documented mesh-info table entry.
				var replacementOffset = tocData.Count;
				EnsureWritableRange(tocData.ToArray(), meshOffset, 128, $"mesh {mesh.Index} record header");
				tocData.AddRange(tocData.Skip(meshOffset).Take(128));
				var materialOffset = replacementOffset + 128;
				for (var i = 0; i < mesh.MaterialSlotIds.Count; i++)
				{
					AppendUInt32(tocData, mesh.MaterialSlotIds[i]);
				}

				var sectionsOffset = tocData.Count;
				for (var i = 0; i < mesh.Sections.Count; i++)
				{
					AppendUInt32(tocData, mesh.Sections[i].MaterialIndex);
					AppendUInt32(tocData, mesh.Sections[i].VertexOffset);
					AppendUInt32(tocData, mesh.Sections[i].NumVertices);
					AppendUInt32(tocData, mesh.Sections[i].IndexOffset);
					AppendUInt32(tocData, mesh.Sections[i].NumIndices);
					AppendUInt32(tocData, mesh.Sections[i].GroupIndex);
				}

				var writableTocData = tocData.ToArray();
				WriteUInt32(writableTocData, replacementOffset + 104, checked((uint)mesh.MaterialSlotIds.Count));
				WriteUInt32(writableTocData, replacementOffset + 108, 128);
				WriteUInt32(writableTocData, replacementOffset + 120, checked((uint)mesh.Sections.Count));
				WriteUInt32(writableTocData, replacementOffset + 124, checked((uint)(sectionsOffset - replacementOffset)));
				WriteUInt32(writableTocData, checked((int)model.MeshInfoOffset + 4 + mesh.Index * 4), checked((uint)(replacementOffset - model.MeshInfoOffset)));
				tocData = writableTocData.ToList();
				updatedMeshes.Add(mesh with
				{
					Offset = checked((uint)replacementOffset),
					NumMaterials = checked((uint)mesh.MaterialSlotIds.Count),
					MaterialOffset = checked((uint)materialOffset),
					NumSections = checked((uint)mesh.Sections.Count),
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
			var rawMeshes = model.RawMeshData
				.Where(mesh => mesh.StreamIndex == (uint)stream.Index)
				.OrderBy(mesh => GetFirstVertexOffset(model, mesh))
				.ThenBy(mesh => GetFirstIndexOffset(model, mesh))
				.ToArray();

			var vertexBufferOffset = checked((uint)gpuData.Count);
			var vertexCount = 0u;
			foreach (var mesh in rawMeshes)
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
			foreach (var mesh in rawMeshes)
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
			WriteMeshSectionOffsets(tocData, model, rawMeshes);
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

	private static void WriteMeshSectionOffsets(byte[] tocData, UnitMeshModel model, IReadOnlyList<UnitRawMeshData> rawMeshes)
	{
		var vertexOffset = 0u;
		var indexOffset = 0u;
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
				var numIndices = checked((uint)((rawSection?.Triangles.Count ?? 0) * 3));
				WriteUInt32(tocData, section.Offset, rawSection?.MaterialIndex ?? section.MaterialIndex);
				WriteUInt32(tocData, section.Offset + 4, vertexOffset);
				WriteUInt32(tocData, section.Offset + 8, checked((uint)rawMesh.Vertices.Count));
				WriteUInt32(tocData, section.Offset + 12, indexOffset);
				WriteUInt32(tocData, section.Offset + 16, numIndices);
				indexOffset = checked(indexOffset + numIndices);
			}

			vertexOffset = checked(vertexOffset + (uint)rawMesh.Vertices.Count);
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
		cursor += 4;
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
