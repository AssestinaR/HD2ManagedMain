using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 UnitMeshAdaptationPlanner 能把 source Unit mesh dry-run 适配到原版 target Unit 模板。
// Purpose: Verifies UnitMeshAdaptationPlanner can dry-run adapt source Unit meshes onto vanilla target Unit templates.
public sealed class UnitMeshAdaptationPlannerTests
{
	[Fact]
	public void BuildPlan_CompatibleSourceAndTarget_ProducesWritableDryRunPayload()
	{
		var target = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildMinimalGpuData());
		var source = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildReplacementGpuData());
		var archive = CreateArchiveUnitMesh(target.Model, target.Payload.TocData, target.Payload.GpuResourceData);
		var planner = CreatePlanner();

		var plan = planner.BuildPlan(source, archive);

		Assert.True(plan.CanWrite, plan.Reason);
		Assert.NotNull(plan.WriteResult);
		Assert.NotEmpty(plan.WriteResult!.TocData);
		Assert.NotEmpty(plan.WriteResult.GpuData);
		Assert.NotNull(plan.EditedModel);
		Assert.Single(plan.Candidates);
		Assert.Single(plan.Steps);
		Assert.Equal(1, plan.ReplacementCount);
		Assert.Equal(0, plan.MinifiedCount);
		Assert.Contains(plan.EditedModel!.RawMeshData, mesh => mesh.MeshInfoIndex == 0 && mesh.Vertices[0].Components[0].FloatValues.SequenceEqual([10f, 20f, 30f]));
	}

	[Fact]
	public void BuildPlan_SourceMeshFilter_UsesRequestedSourceMesh()
	{
		var target = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildMinimalGpuData());
		var source = CreatePatchUnitMesh(CreateModel([
			CreateRawMesh(meshInfoIndex: 0, meshId: 0x20000000, lodIndex: 0, materialSlots: [123], vertexSeed: 20),
			CreateRawMesh(meshInfoIndex: 1, meshId: 0x12345678, lodIndex: 0, materialSlots: [123], vertexSeed: 60)
		], componentFormat: 2));
		var archive = CreateArchiveUnitMesh(target.Model, target.Payload.TocData, target.Payload.GpuResourceData);
		var planner = CreatePlanner();

		var plan = planner.BuildPlan(source, archive, sourceMeshInfoIndex: 1);

		Assert.True(plan.CanWrite, plan.Reason);
		Assert.NotNull(plan.EditedModel);
		var replacement = Assert.Single(plan.Steps, step => step.Kind == UnitMeshAdaptationStepKind.ReplaceWithSource);
		Assert.Equal(1, replacement.SourceMeshInfoIndex);
		Assert.Contains(plan.EditedModel!.RawMeshData, mesh => mesh.MeshInfoIndex == 0 && mesh.Vertices[0].Data[0] == 60);
	}

	[Fact]
	public void BuildPlan_MinifiesCullingTargetMeshBeforeReplacement()
	{
		var target = CreatePatchUnitMesh(CreateModel([
			CreateRawMesh(meshInfoIndex: 0, meshId: 0x10000000, lodIndex: -1, materialSlots: [999], vertexSeed: 10),
			CreateRawMesh(meshInfoIndex: 1, meshId: 0x12345678, lodIndex: 0, materialSlots: [123], vertexSeed: 20)
		], componentFormat: 2, isCullingBody: meshInfoIndex => meshInfoIndex == 0));
		var source = CreatePatchUnitMesh(CreateModel(CreateRawMesh(meshInfoIndex: 1, meshId: 0x12345678, lodIndex: 0, materialSlots: [123], vertexSeed: 60), componentFormat: 2));
		var archive = CreateArchiveUnitMesh(target.Model, target.Payload.TocData, target.Payload.GpuResourceData);
		var planner = CreatePlanner();

		var plan = planner.BuildPlan(source, archive);

		Assert.NotNull(plan.EditedModel);
		Assert.Equal(1, plan.MinifiedCount);
		Assert.Contains(plan.Steps, step => step.Kind == UnitMeshAdaptationStepKind.MinifyTarget && step.TargetMeshInfoIndex == 0);
		Assert.Contains(plan.EditedModel!.RawMeshData, mesh => mesh.MeshInfoIndex == 0 && mesh.Vertices.Count == 3 && mesh.Vertices[0].Data[0] == 0);
		Assert.Contains(plan.EditedModel!.RawMeshData, mesh => mesh.MeshInfoIndex == 1 && mesh.Vertices[0].Data[0] == 60);
	}

	[Fact]
	public void BuildPlan_IncompatibleLayouts_WritesMinifyOnlyPlan()
	{
		var target = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildMinimalGpuData());
		var source = CreatePatchUnitMesh(BuildVec2ComponentUnitTocData(), BuildMinimalGpuData());
		var archive = CreateArchiveUnitMesh(target.Model, target.Payload.TocData, target.Payload.GpuResourceData);
		var planner = CreatePlanner();

		var plan = planner.BuildPlan(source, archive);

		Assert.True(plan.CanWrite, plan.Reason);
		Assert.Empty(plan.Candidates);
		Assert.Equal(0, plan.ReplacementCount);
		Assert.Equal(1, plan.MinifiedCount);
		Assert.NotNull(plan.WriteResult);
		Assert.NotNull(plan.EditedModel);
		Assert.Contains("minify-only", plan.Reason, StringComparison.OrdinalIgnoreCase);
		Assert.Contains(plan.EditedModel!.RawMeshData, mesh => mesh.MeshInfoIndex == 0 && mesh.Vertices.Count == 3 && mesh.Vertices[0].Data[0] == 0);
	}

	[Fact]
	public void BuildPlan_MissingSourceMeshFilter_Throws()
	{
		var target = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildMinimalGpuData());
		var source = CreatePatchUnitMesh(BuildMinimalUnitTocData(), BuildMinimalGpuData());
		var archive = CreateArchiveUnitMesh(target.Model, target.Payload.TocData, target.Payload.GpuResourceData);
		var planner = CreatePlanner();

		Assert.Throws<ArgumentOutOfRangeException>(() => planner.BuildPlan(source, archive, sourceMeshInfoIndex: 99));
	}

	private static UnitMeshAdaptationPlanner CreatePlanner()
		=> new(new UnitMeshReplacementStrategy(), new UnitMeshMinifier(), new UnitMeshRetargeter(), new UnitMeshWriter());

	private static PatchUnitMesh CreatePatchUnitMesh(byte[] tocData, byte[] gpuData)
	{
		var model = new UnitMeshReader().Read(tocData, gpuData);
		var entry = new PatchTocEntry(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1111111111111111),
			"source.patch_0",
			"source.patch_0",
			TocDataSize: (uint)tocData.Length,
			GpuResourceSize: (uint)gpuData.Length);
		return new PatchUnitMesh(entry, new PatchEntryPayload(entry, tocData, Array.Empty<byte>(), gpuData), model);
	}

	private static PatchUnitMesh CreatePatchUnitMesh(UnitMeshModel model)
	{
		var entry = new PatchTocEntry(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x1111111111111111),
			"source.patch_0",
			"source.patch_0");
		return new PatchUnitMesh(entry, new PatchEntryPayload(entry, Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), model);
	}

	private static ArchiveUnitMesh CreateArchiveUnitMesh(UnitMeshModel model, byte[] tocData, byte[] gpuData)
	{
		var entry = new ArchiveTocEntry(
			new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x2222222222222222),
			"target_archive",
			TocDataSize: (uint)tocData.Length,
			GpuResourceSize: 1);
		return new ArchiveUnitMesh(entry, new ArchiveEntryPayload(entry, tocData, Array.Empty<byte>(), gpuData), model);
	}

	private static UnitMeshModel CreateModel(uint meshId, int lodIndex, uint[] materialSlots, uint componentFormat = 1, byte vertexSeed = 1)
		=> CreateModel(CreateRawMesh(meshInfoIndex: 0, meshId, lodIndex, materialSlots, vertexSeed), componentFormat);

	private static UnitMeshModel CreateModel(params UnitRawMeshData[] rawMeshes)
		=> CreateModel(rawMeshes, componentFormat: 1);

	private static UnitMeshModel CreateModel(UnitRawMeshData rawMesh, uint componentFormat = 1)
		=> CreateModel([rawMesh], componentFormat);

	private static UnitMeshModel CreateModel(UnitRawMeshData[] rawMeshes, uint componentFormat, Func<int, bool>? isCullingBody = null)
	{
		var streamIndexes = rawMeshes.Select(mesh => mesh.StreamIndex).Distinct().ToArray();
		var streams = streamIndexes.Select(index => CreateStream((int)index, componentFormat)).ToArray();
		var meshes = rawMeshes.Select(mesh => new UnitMeshInfo(
			mesh.MeshInfoIndex,
			0,
			mesh.MeshId,
			mesh.LodIndex,
			0,
			mesh.StreamIndex,
			(uint)mesh.Vertices.Count,
			(uint)(mesh.Triangles.Count * 3),
			(uint)mesh.Sections.Count,
			(uint)mesh.Sections.Count,
			UnitMeshSemanticInfo.Empty(mesh.LodIndex, mesh.MeshInfoIndex) with { IsCullingBody = isCullingBody?.Invoke(mesh.MeshInfoIndex) ?? false },
			mesh.Sections.Select(section => section.MaterialSlotId).ToArray(),
			mesh.Sections.Select((section, index) => new UnitMeshSectionInfo((uint)index, section.MaterialIndex, section.MaterialSlotId, 0, (uint)mesh.Vertices.Count, 0, (uint)(section.Triangles.Count * 3), 0)).ToArray())).ToArray();
		return new UnitMeshModel(
			0,
			0,
			0,
			0x00A4CD36,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			Array.Empty<UnitBoneInfo>(),
			streams,
			meshes,
			Array.Empty<UnitMaterialBinding>(),
			rawMeshes.Select(mesh => new UnitRawMeshSummary(mesh.MeshInfoIndex, mesh.MeshId, mesh.LodIndex, mesh.StreamIndex, (uint)mesh.Vertices.Count, (uint)(mesh.Triangles.Count * 3), (uint)mesh.Sections.Count, (uint)mesh.Sections.Count, true, true)).ToArray(),
			rawMeshes);
	}

	private static UnitStreamInfo CreateStream(int index, uint componentFormat)
		=> new(
			index,
			0,
			0,
			1,
			0,
			3,
			12,
			0,
			3,
			0,
			0,
			36,
			36,
			6,
			[new UnitStreamComponentInfo(0, "position", componentFormat, "vec3_float", 0, 0, 12)]);

	private static UnitRawMeshData CreateRawMesh(int meshInfoIndex, uint meshId, int lodIndex, uint[] materialSlots, byte vertexSeed)
	{
		var sections = materialSlots.Select(slot => new UnitRawMeshSectionData(0, slot, [new UnitTriangleIndices(0, 1, 2)])).ToArray();
		return new UnitRawMeshData(
			meshInfoIndex,
			meshId,
			lodIndex,
			0,
			sections,
			sections.SelectMany(section => section.Triangles).ToArray(),
			[
				new UnitRawVertexRecord(0, CreateVertex(vertexSeed), Array.Empty<UnitVertexComponentValue>()),
				new UnitRawVertexRecord(1, CreateVertex((byte)(vertexSeed + 1)), Array.Empty<UnitVertexComponentValue>()),
				new UnitRawVertexRecord(2, CreateVertex((byte)(vertexSeed + 2)), Array.Empty<UnitVertexComponentValue>()),
			]);
	}

	private static byte[] CreateVertex(byte seed)
		=> [seed, 0, 0, 0, seed, 0, 0, 0, seed, 0, 0, 0];

	private static byte[] BuildMinimalUnitTocData()
	{
		var data = new byte[0x400];

		const int streamInfoOffset = 0x80;
		const int streamRecordOffset = 0x20;
		const int meshInfoOffset = 0x260;
		const int meshRecordOffset = 0x20;
		const int materialsOffset = 0x340;

		WriteUInt64(data, 0x00, 0x1122334455667788ul);
		WriteUInt64(data, 0x08, 0x0102030405060708ul);
		WriteUInt64(data, 0x10, 0);
		WriteUInt32(data, 0x2c, 0x00A4CD36u);
		WriteUInt32(data, 0x58, 0);
		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x60, 0x3f0);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, 0x70, materialsOffset);

		WriteUInt32(data, streamInfoOffset, 1);
		WriteUInt32(data, streamInfoOffset + 4, streamRecordOffset);
		WriteUInt32(data, streamInfoOffset + 8, 0x12345678u);
		WriteUInt32(data, streamInfoOffset + 12, 0);

		var stream = streamInfoOffset + streamRecordOffset;
		WriteUInt64(data, stream, 0xabcdeful);
		WriteUInt32(data, stream + 8, 0);
		WriteUInt32(data, stream + 12, 2);
		WriteUInt32(data, stream + 16, 0);
		WriteUInt64(data, stream + 20, 0);
		var streamFields = stream + 8 + 320;
		WriteUInt64(data, streamFields, 1); streamFields += 8;
		WriteUInt64(data, streamFields, 0x1000); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 3); streamFields += 4;
		WriteUInt32(data, streamFields, 12); streamFields += 4;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0x2000); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 3); streamFields += 4;
		WriteUInt32(data, streamFields, 0); streamFields += 4;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt64(data, streamFields, 0); streamFields += 8;
		WriteUInt32(data, streamFields, 0); streamFields += 4;
		WriteUInt32(data, streamFields, 36); streamFields += 4;
		WriteUInt32(data, streamFields, 36); streamFields += 4;
		WriteUInt32(data, streamFields, 6);

		WriteUInt32(data, meshInfoOffset, 1);
		WriteUInt32(data, meshInfoOffset + 4, meshRecordOffset);
		WriteUInt32(data, meshInfoOffset + 8, 0x12345678u);

		var mesh = meshInfoOffset + meshRecordOffset;
		var meshCursor = mesh + 40;
		WriteUInt32(data, meshCursor, 0x12345678u); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		meshCursor += 40;
		WriteUInt32(data, meshCursor, 1); meshCursor += 4;
		WriteUInt32(data, meshCursor, 112); meshCursor += 4;
		WriteUInt64(data, meshCursor, 0); meshCursor += 8;
		WriteUInt32(data, meshCursor, 1); meshCursor += 4;
		WriteUInt32(data, meshCursor, 116); meshCursor += 4;
		WriteUInt32(data, meshCursor, 123); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0);

		WriteUInt32(data, materialsOffset, 1);
		WriteUInt32(data, materialsOffset + 4, 123);
		WriteUInt64(data, materialsOffset + 8, 0x8877665544332211ul);

		return data;
	}

	private static byte[] BuildVec2ComponentUnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int stream = 0x80 + 0x20;
		WriteUInt32(data, stream + 12, 1);
		return data;
	}

	private static byte[] BuildMinimalGpuData()
	{
		var data = new byte[64];
		WriteSingle(data, 0, 1f);
		WriteSingle(data, 4, 2f);
		WriteSingle(data, 8, 3f);
		WriteSingle(data, 12, 4f);
		WriteSingle(data, 16, 5f);
		WriteSingle(data, 20, 6f);
		WriteSingle(data, 24, 7f);
		WriteSingle(data, 28, 8f);
		WriteSingle(data, 32, 9f);
		WriteUInt16(data, 36, 0);
		WriteUInt16(data, 38, 1);
		WriteUInt16(data, 40, 2);
		return data;
	}

	private static byte[] BuildReplacementGpuData()
	{
		var data = new byte[64];
		WriteSingle(data, 0, 10f);
		WriteSingle(data, 4, 20f);
		WriteSingle(data, 8, 30f);
		WriteSingle(data, 12, 40f);
		WriteSingle(data, 16, 50f);
		WriteSingle(data, 20, 60f);
		WriteSingle(data, 24, 70f);
		WriteSingle(data, 28, 80f);
		WriteSingle(data, 32, 90f);
		WriteUInt16(data, 36, 0);
		WriteUInt16(data, 38, 2);
		WriteUInt16(data, 40, 1);
		return data;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
		data[offset + 2] = (byte)(value >> 16);
		data[offset + 3] = (byte)(value >> 24);
	}

	private static void WriteUInt16(byte[] data, int offset, ushort value)
	{
		data[offset] = (byte)value;
		data[offset + 1] = (byte)(value >> 8);
	}

	private static void WriteSingle(byte[] data, int offset, float value) => WriteInt32(data, offset, BitConverter.SingleToInt32Bits(value));

	private static void WriteInt32(byte[] data, int offset, int value) => WriteUInt32(data, offset, unchecked((uint)value));

	private static void WriteUInt64(byte[] data, int offset, ulong value)
	{
		WriteUInt32(data, offset, (uint)value);
		WriteUInt32(data, offset + 4, (uint)(value >> 32));
	}
}
