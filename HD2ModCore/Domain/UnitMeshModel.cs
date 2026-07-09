namespace HD2ModCore.Domain;

// 作用：描述 Unit 中可解析的 mesh/stream/material 结构摘要。
// Purpose: Describes parsed mesh/stream/material structure from a Unit resource.
public sealed record UnitMeshModel(
	uint Version,
	ulong NameHash,
	ulong BonesRef,
	ulong CompositeRef,
	uint CustomizationInfoOffset,
	uint BoneInfoOffset,
	uint StreamInfoOffset,
	uint MeshInfoOffset,
	uint MaterialsOffset,
	uint EndingOffset,
	UnitCustomizationInfo CustomizationInfo,
	IReadOnlyList<UnitBoneInfo> BoneInfos,
	IReadOnlyList<UnitStreamInfo> Streams,
	IReadOnlyList<UnitMeshInfo> Meshes,
	IReadOnlyList<UnitMaterialBinding> Materials,
	IReadOnlyList<UnitRawMeshSummary> RawMeshes,
	IReadOnlyList<UnitRawMeshData> RawMeshData);

public sealed record UnitCustomizationInfo(
	string BodyType,
	string Slot,
	string Weight,
	string PieceType)
{
	public static UnitCustomizationInfo Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);

	public bool HasValue => BodyType.Length > 0 || Slot.Length > 0 || Weight.Length > 0 || PieceType.Length > 0;
}

public sealed record UnitBoneInfo(
	int Index,
	uint Offset,
	uint NumBones,
	uint MatrixOffset,
	uint RealIndicesOffset,
	uint RemapDataOffset,
	IReadOnlyList<uint> RealIndices,
	IReadOnlyList<UnitBoneRemap> Remaps);

public sealed record UnitBoneRemap(
	int MaterialIndex,
	uint Offset,
	IReadOnlyList<uint> FakeIndices);

public sealed record UnitStreamInfo(
	int Index,
	uint Offset,
	ulong ComponentInfoId,
	ulong NumComponents,
	ulong VertexBufferId,
	uint NumVertices,
	uint VertexStride,
	ulong IndexBufferId,
	uint NumIndices,
	uint IndexBufferType,
	uint VertexBufferOffset,
	uint VertexBufferSize,
	uint IndexBufferOffset,
	uint IndexBufferSize,
	IReadOnlyList<UnitStreamComponentInfo> Components);

public sealed record UnitStreamComponentInfo(
	uint Type,
	string TypeName,
	uint Format,
	string FormatName,
	uint Index,
	ulong Unknown,
	uint Size);

public sealed record UnitMeshInfo(
	int Index,
	uint Offset,
	uint MeshId,
	int LodIndex,
	uint TransformIndex,
	uint StreamIndex,
	uint NumMaterials,
	uint MaterialOffset,
	uint NumSections,
	uint SectionsOffset,
	UnitMeshSemanticInfo SemanticInfo,
	IReadOnlyList<uint> MaterialSlotIds,
	IReadOnlyList<UnitMeshSectionInfo> Sections);

public sealed record UnitMeshSemanticInfo(
	string Name,
	string Slot,
	string PieceType,
	string BodyType,
	string Weight,
	int LodIndex,
	int MeshInfoIndex,
	bool IsCullingBody,
	bool IsStaticMesh,
	bool IsLod)
{
	public static UnitMeshSemanticInfo Empty(int lodIndex, int meshInfoIndex)
		=> new(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, lodIndex, meshInfoIndex, false, false, lodIndex is not 0 and not -1);

	public bool HasValue => Slot.Length > 0 || PieceType.Length > 0 || BodyType.Length > 0 || Weight.Length > 0;

	public bool IsVisualMesh => !IsCullingBody && !IsStaticMesh;
}

public sealed record UnitMeshSectionInfo(
	uint Offset,
	uint MaterialIndex,
	uint MaterialSlotId,
	uint VertexOffset,
	uint NumVertices,
	uint IndexOffset,
	uint NumIndices,
	uint GroupIndex);

public sealed record UnitMaterialBinding(
	uint SectionId,
	ulong MaterialId);

public sealed record UnitRawMeshSummary(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	uint StreamIndex,
	uint VertexCount,
	uint IndexCount,
	uint MaterialCount,
	uint SectionCount,
	bool HasGpuVertexRange,
	bool HasGpuIndexRange);

public sealed record UnitRawMeshData(
	int MeshInfoIndex,
	uint MeshId,
	int LodIndex,
	uint StreamIndex,
	IReadOnlyList<UnitRawMeshSectionData> Sections,
	IReadOnlyList<UnitTriangleIndices> Triangles,
	IReadOnlyList<UnitRawVertexRecord> Vertices);

public sealed record UnitRawMeshSectionData(
	uint MaterialIndex,
	uint MaterialSlotId,
	IReadOnlyList<UnitTriangleIndices> Triangles);

public sealed record UnitTriangleIndices(
	uint A,
	uint B,
	uint C);

public sealed record UnitRawVertexRecord(
	uint Index,
	byte[] Data,
	IReadOnlyList<UnitVertexComponentValue> Components);

public sealed record UnitVertexComponentValue(
	uint Type,
	string TypeName,
	uint Format,
	string FormatName,
	uint Index,
	float[] FloatValues,
	uint[] UIntValues,
	byte[] RawData);