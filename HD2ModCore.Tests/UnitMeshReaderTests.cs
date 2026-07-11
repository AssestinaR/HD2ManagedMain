using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证 Unit mesh 只读解析器能读取最小 Stingray Unit 的 stream、mesh 与 material 摘要。
// Purpose: Verifies the Unit mesh reader parses stream, mesh, and material summaries from a minimal Stingray Unit.
public sealed class UnitMeshReaderTests
{
	[Fact]
	public void Read_MinimalUnit_ReturnsMeshSummary()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();

		var model = reader.Read(tocData, gpuData);

		Assert.Equal(0x00A4CD36u, model.Version);
		Assert.Equal(0x1122334455667788ul, model.NameHash);
		Assert.Single(model.Streams);
		Assert.Single(model.Meshes);
		Assert.Single(model.Materials);
		Assert.Single(model.RawMeshes);

		var stream = model.Streams[0];
		Assert.Equal(3u, stream.NumVertices);
		Assert.Equal(3u, stream.NumIndices);
		Assert.Equal(12u, stream.VertexStride);
		Assert.Equal("position", stream.Components[0].TypeName);
		Assert.Equal("vec3_float", stream.Components[0].FormatName);

		var mesh = model.Meshes[0];
		Assert.Equal(0x12345678u, mesh.MeshId);
		Assert.Equal(0, mesh.LodIndex);
		Assert.Equal(0u, mesh.StreamIndex);
		Assert.Equal(123u, mesh.MaterialSlotIds[0]);
		Assert.Equal(3u, mesh.Sections[0].NumVertices);
		Assert.Equal(3u, mesh.Sections[0].NumIndices);

		var material = model.Materials[0];
		Assert.Equal(123u, material.SectionId);
		Assert.Equal(0x8877665544332211ul, material.MaterialId);

		var rawMesh = model.RawMeshes[0];
		Assert.Equal(3u, rawMesh.VertexCount);
		Assert.Equal(3u, rawMesh.IndexCount);
		Assert.True(rawMesh.HasGpuVertexRange);
		Assert.True(rawMesh.HasGpuIndexRange);

		var rawMeshData = Assert.Single(model.RawMeshData);
		var triangle = Assert.Single(rawMeshData.Triangles);
		Assert.Equal(0u, triangle.A);
		Assert.Equal(1u, triangle.B);
		Assert.Equal(2u, triangle.C);
		Assert.Equal(3, rawMeshData.Vertices.Count);
		Assert.Equal(12, rawMeshData.Vertices[0].Data.Length);
		var position = Assert.Single(rawMeshData.Vertices[0].Components);
		Assert.Equal("position", position.TypeName);
		Assert.Equal([1f, 2f, 3f], position.FloatValues);
	}

	[Fact]
	public void Read_UnitWithBoneInfo_ReturnsMaterialRemaps()
	{
		var tocData = BuildUnitTocDataWithBoneInfo();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();

		var model = reader.Read(tocData, gpuData);

		var boneInfo = Assert.Single(model.BoneInfos);
		Assert.Equal(0x120u, boneInfo.Offset);
		Assert.Equal(3u, boneInfo.NumBones);
		Assert.Equal([10u, 20u, 30u], boneInfo.RealIndices);

		Assert.Equal(2, boneInfo.Remaps.Count);
		Assert.Equal(0, boneInfo.Remaps[0].MaterialIndex);
		Assert.Equal([0u, 1u, 2u], boneInfo.Remaps[0].FakeIndices);
		Assert.Equal(1, boneInfo.Remaps[1].MaterialIndex);
		Assert.Equal([2u, 0u], boneInfo.Remaps[1].FakeIndices);
	}

	[Fact]
	public void Murmur32_MatchesHd2SdkKnownMeshNameHash()
	{
		Assert.Equal(0x2ed924fau, MurmurHash.Murmur32("RightArm_Undergarment_Slim_lod0"));
		Assert.Equal(0x4a24d412u, MurmurHash.Murmur32("RightLeg_Undergarment_Any_lod0"));
	}

	[Fact]
	public void Read_WithBoneNamesMatchingMeshId_UsesSdkMeshNameFallback()
	{
		var tocData = BuildMinimalUnitTocData();
		var meshName = "RightArm_Undergarment_Slim_lod0";
		var meshId = MurmurHash.Murmur32(meshName);
		WriteUInt32(tocData, 0x260 + 8, meshId);
		WriteUInt32(tocData, 0x260 + 0x20 + 40, meshId);
		var gpuData = BuildMinimalGpuData();
		var boneNames = new UnitBoneNames([meshId], [meshName]);
		var reader = new UnitMeshReader();

		var model = reader.Read(tocData, gpuData, boneNames: boneNames);

		var semantic = Assert.Single(model.Meshes).SemanticInfo;
		Assert.Equal(meshName, semantic.Name);
		Assert.Equal("RightArm", semantic.Slot);
		Assert.Equal("Undergarment", semantic.PieceType);
		Assert.Equal("Slim", semantic.BodyType);
		Assert.True(semantic.HasValue);
	}

	[Fact]
	public void UnitBoneNamesReader_ReadsHashesAndNames()
	{
		var meshName = "RightLeg_Undergarment_Any_lod0";
		var meshId = MurmurHash.Murmur32(meshName);
		var data = BuildBoneNamesData(meshId, meshName);
		var reader = new UnitBoneNamesReader();

		var names = reader.Read(data);

		Assert.Equal([meshId], names.Hashes);
		Assert.Equal([meshName], names.Names);
	}

	[Fact]
	public void Read_ZeroMeshInfoOffset_Throws()
	{
		var tocData = BuildMinimalUnitTocData();
		WriteUInt32(tocData, 0x64, 0);
		var reader = new UnitMeshReader();

		Assert.Throws<InvalidDataException>(() => reader.Read(tocData, ReadOnlySpan<byte>.Empty));
	}

	[Fact]
	public void Write_ReadRoundTrip_PreservesMinimalRawMesh()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var written = writer.Write(model, tocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		Assert.Equal(54, written.GpuData.Length);
		var stream = reparsed.Streams[0];
		Assert.Equal(0u, stream.VertexBufferOffset);
		Assert.Equal(36u, stream.VertexBufferSize);
		Assert.Equal(48u, stream.IndexBufferOffset);
		Assert.Equal(6u, stream.IndexBufferSize);
		Assert.Equal(3u, stream.NumVertices);
		Assert.Equal(3u, stream.NumIndices);

		var mesh = reparsed.Meshes[0];
		Assert.Equal(0u, mesh.Sections[0].VertexOffset);
		Assert.Equal(3u, mesh.Sections[0].NumVertices);
		Assert.Equal(0u, mesh.Sections[0].IndexOffset);
		Assert.Equal(3u, mesh.Sections[0].NumIndices);

		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		var triangle = Assert.Single(rawMeshData.Triangles);
		Assert.Equal(0u, triangle.A);
		Assert.Equal(1u, triangle.B);
		Assert.Equal(2u, triangle.C);
		var position = Assert.Single(rawMeshData.Vertices[0].Components);
		Assert.Equal([1f, 2f, 3f], position.FloatValues);
	}

	[Fact]
	public void Minify_WriteReadRoundTrip_ReplacesRawMeshWithPlaceholderTriangle()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var minifier = new UnitMeshMinifier();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var minified = minifier.MinifyAll(model);
		var written = writer.Write(minified, tocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		Assert.Equal(54, written.GpuData.Length);
		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(3, rawMeshData.Vertices.Count);
		var triangle = Assert.Single(rawMeshData.Triangles);
		Assert.Equal(0u, triangle.A);
		Assert.Equal(1u, triangle.B);
		Assert.Equal(2u, triangle.C);

		Assert.Equal([0f, 0f, 0f], rawMeshData.Vertices[0].Components[0].FloatValues);
		Assert.Equal([0.001f, 0f, 0f], rawMeshData.Vertices[1].Components[0].FloatValues);
		Assert.Equal([0f, 0.001f, 0f], rawMeshData.Vertices[2].Components[0].FloatValues);
	}

	[Fact]
	public void Retarget_WriteReadRoundTrip_ReplacesTargetRawMeshWithSourceRawMesh()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildMinimalUnitTocData();
		var targetGpuData = BuildMinimalGpuData();
		var sourceGpuData = BuildReplacementGpuData();
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter();
		var writer = new UnitMeshWriter();

		var targetModel = reader.Read(targetTocData, targetGpuData);
		var sourceModel = reader.Read(sourceTocData, sourceGpuData);
		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);
		var written = writer.Write(retargeted, targetTocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(123u, rawMeshData.Sections[0].MaterialSlotId);
		Assert.Equal([10f, 20f, 30f], rawMeshData.Vertices[0].Components[0].FloatValues);
		Assert.Equal([40f, 50f, 60f], rawMeshData.Vertices[1].Components[0].FloatValues);
		Assert.Equal([70f, 80f, 90f], rawMeshData.Vertices[2].Components[0].FloatValues);
	}

	[Fact]
	public void Retarget_WriteReadRoundTrip_PreservesTargetSlotAndUsesSourceMaterial()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildMinimalUnitTocData();
		var meshCursor = 0x260 + 0x20 + 40;
		meshCursor += 4; // mesh id
		meshCursor += 4; // transform index
		meshCursor += 4; // lod index
		meshCursor += 4; // stream index
		meshCursor += 4; // unknown
		meshCursor += 4; // unknown
		meshCursor += 40;
		meshCursor += 4; // material count
		meshCursor += 4; // material offset
		meshCursor += 8; // unknown
		meshCursor += 4; // section count
		meshCursor += 4; // sections offset
		WriteUInt32(sourceTocData, meshCursor, 456);
		WriteUInt32(sourceTocData, 0x340 + 4, 456);
		WriteUInt64(sourceTocData, 0x340 + 8, 0x0102030405060708ul);
		var targetGpuData = BuildMinimalGpuData();
		var sourceGpuData = BuildReplacementGpuData();
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter(propagateSourceMaterials: true);
		var writer = new UnitMeshWriter();

		var targetModel = reader.Read(targetTocData, targetGpuData);
		var sourceModel = reader.Read(sourceTocData, sourceGpuData);
		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);
		var written = writer.Write(retargeted, targetTocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		var mesh = Assert.Single(reparsed.Meshes);
		Assert.Equal([123u], mesh.MaterialSlotIds);
		Assert.Equal(123u, mesh.Sections[0].MaterialSlotId);

		var material = Assert.Single(reparsed.Materials);
		Assert.Equal(123u, material.SectionId);
		Assert.Equal(0x0102030405060708ul, material.MaterialId);

		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(123u, rawMeshData.Sections[0].MaterialSlotId);
	}

	[Fact]
	public void Retarget_SourceMaterialNotAllowed_FallsBackToTargetMaterial()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildMinimalUnitTocData();
		var meshCursor = 0x260 + 0x20 + 40;
		meshCursor += 4; // mesh id
		meshCursor += 4; // transform index
		meshCursor += 4; // lod index
		meshCursor += 4; // stream index
		meshCursor += 4; // unknown
		meshCursor += 4; // unknown
		meshCursor += 40;
		meshCursor += 4; // material count
		meshCursor += 4; // material offset
		meshCursor += 8; // unknown
		meshCursor += 4; // section count
		meshCursor += 4; // sections offset
		WriteUInt32(sourceTocData, meshCursor, 456);
		WriteUInt32(sourceTocData, 0x340 + 4, 456);
		WriteUInt64(sourceTocData, 0x340 + 8, 0x0102030405060708ul);
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter(propagateSourceMaterials: true, allowedSourceMaterialIds: new HashSet<ulong>());

		var targetModel = reader.Read(targetTocData, BuildMinimalGpuData());
		var sourceModel = reader.Read(sourceTocData, BuildReplacementGpuData());
		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		var material = Assert.Single(retargeted.Materials);
		Assert.Equal(123u, material.SectionId);
		Assert.Equal(0x8877665544332211ul, material.MaterialId);
		Assert.Equal(123u, Assert.Single(retargeted.RawMeshData).Sections[0].MaterialSlotId);
	}

	[Fact]
	public void Retarget_DuplicateSourceBindingsWithSameMaterial_UsesSourceMaterial()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		var sourceModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with { MaterialSlotIds = [700u] }],
			Materials = [new UnitMaterialBinding(700u, 0x3333333333333333ul), new UnitMaterialBinding(700u, 0x3333333333333333ul)],
			RawMeshData = [targetModel.RawMeshData[0] with { Sections = [targetModel.RawMeshData[0].Sections[0] with { MaterialSlotId = 700u }] }]
		};
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal(0x3333333333333333ul, Assert.Single(retargeted.Materials).MaterialId);
	}

	[Fact]
	public void Retarget_PartiallyAllowedSourceMaterials_PropagatesAllowedSlotsOnly()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		targetModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with { MaterialSlotIds = [123u, 456u] }],
			Materials = [new UnitMaterialBinding(123u, 0x1111111111111111ul), new UnitMaterialBinding(456u, 0x2222222222222222ul)],
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections =
				[
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 123u },
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 456u }
				]
			}]
		};
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [700u, 800u] }],
			Materials = [new UnitMaterialBinding(700u, 0x3333333333333333ul), new UnitMaterialBinding(800u, 0x4444444444444444ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with
			{
				Sections =
				[
					sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 700u },
					sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 800u }
				]
			}]
		};
		var retargeter = new UnitMeshRetargeter(
			allowExperimentalLayoutFallback: true,
			propagateSourceMaterials: true,
			allowedSourceMaterialIds: new HashSet<ulong> { 0x3333333333333333ul });

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal([123u, 456u], Assert.Single(retargeted.Meshes).MaterialSlotIds);
		Assert.Equal([new UnitMaterialBinding(123u, 0x3333333333333333ul), new UnitMaterialBinding(456u, 0x2222222222222222ul)], retargeted.Materials);
		Assert.Equal(123u, retargeted.RawMeshData[0].Sections[0].MaterialSlotId);
		Assert.Equal(456u, retargeted.RawMeshData[0].Sections[1].MaterialSlotId);
		Assert.Equal(0u, retargeted.RawMeshData[0].Sections[0].MaterialIndex);
		Assert.Equal(1u, retargeted.RawMeshData[0].Sections[1].MaterialIndex);
	}

	[Fact]
	public void Retarget_TargetBindingOrderDiffersFromMeshSlots_UsesTargetMeshMaterialIndex()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		targetModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with { MaterialSlotIds = [123u, 456u] }],
			Materials = [new UnitMaterialBinding(456u, 0x2222222222222222ul), new UnitMaterialBinding(123u, 0x1111111111111111ul)],
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections = [targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 456u }]
			}]
		};
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [700u, 800u] }],
			Materials = [new UnitMaterialBinding(700u, 0x3333333333333333ul), new UnitMaterialBinding(800u, 0x4444444444444444ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with
			{
				Sections = [sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 800u }]
			}]
		};
		var retargeter = new UnitMeshRetargeter(
			allowExperimentalLayoutFallback: true,
			propagateSourceMaterials: true,
			allowedSourceMaterialIds: new HashSet<ulong>());

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		var section = Assert.Single(retargeted.RawMeshData[0].Sections);
		Assert.Equal(456u, section.MaterialSlotId);
		Assert.Equal(1u, section.MaterialIndex);
		Assert.Equal([new UnitMaterialBinding(456u, 0x2222222222222222ul), new UnitMaterialBinding(123u, 0x1111111111111111ul)], retargeted.Materials);
	}

	[Fact]
	public void Retarget_SourceHasMoreSlotsThanCopiedSections_UsesCopiedSectionMaterial()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		targetModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with { MaterialSlotIds = [123u] }],
			Materials = [new UnitMaterialBinding(123u, 0x1111111111111111ul)],
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections = [targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 123u }]
			}]
		};
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [700u, 800u] }],
			Materials = [new UnitMaterialBinding(700u, 0x3333333333333333ul), new UnitMaterialBinding(800u, 0x4444444444444444ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with
			{
				Sections =
				[
					sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 800u },
					sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 700u }
				]
			}]
		};
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal([123u], Assert.Single(retargeted.Meshes).MaterialSlotIds);
		Assert.Equal([new UnitMaterialBinding(123u, 0x4444444444444444ul)], retargeted.Materials);
		var section = Assert.Single(retargeted.RawMeshData[0].Sections);
		Assert.Equal(123u, section.MaterialSlotId);
		Assert.Equal(0u, section.MaterialIndex);
	}

	[Fact]
	public void Retarget_SourceRawSectionSlotNotInMeshInfo_StillUsesSourceMaterial()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [] }],
			Materials = [new UnitMaterialBinding(700u, 0x3333333333333333ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with
			{
				Sections = [sourceModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 700u }]
			}]
		};
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal([123u], Assert.Single(retargeted.Meshes).MaterialSlotIds);
		Assert.Equal([new UnitMaterialBinding(123u, 0x3333333333333333ul)], retargeted.Materials);
		Assert.Equal(123u, Assert.Single(retargeted.RawMeshData).Sections[0].MaterialSlotId);
	}

	[Fact]
	public void Retarget_SourceSlotCollisionStillUsesTargetBindingSlot()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		targetModel = targetModel with { Materials = [new UnitMaterialBinding(123, 0x1111111111111111ul), new UnitMaterialBinding(456, 0x2222222222222222ul)] };
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [456] }],
			Materials = [new UnitMaterialBinding(456, 0x3333333333333333ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with { Sections = [sourceModel.RawMeshData[0].Sections[0] with { MaterialSlotId = 456 }] }]
		};
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal([123u], Assert.Single(retargeted.Meshes).MaterialSlotIds);
		Assert.Equal(123u, Assert.Single(retargeted.RawMeshData).Sections[0].MaterialSlotId);
		Assert.Equal([new UnitMaterialBinding(123, 0x3333333333333333ul), new UnitMaterialBinding(456, 0x2222222222222222ul)], retargeted.Materials);
	}

	[Fact]
	public void Retarget_DuplicateSourceMaterialBindings_FallsBackToTargetBinding()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		var sourceModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with { MaterialSlotIds = [456] }],
			Materials = [new UnitMaterialBinding(456, 0x3333333333333333ul), new UnitMaterialBinding(456, 0x4444444444444444ul)],
			RawMeshData = [sourceModel.RawMeshData[0] with { Sections = [sourceModel.RawMeshData[0].Sections[0] with { MaterialSlotId = 456 }] }]
		};
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true, propagateSourceMaterials: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal([123u], Assert.Single(retargeted.Meshes).MaterialSlotIds);
		Assert.Equal(123u, Assert.Single(retargeted.RawMeshData).Sections[0].MaterialSlotId);
		Assert.Equal([new UnitMaterialBinding(123, 0x8877665544332211ul)], retargeted.Materials);
	}

	[Fact]
	public void Retarget_BoneInfoRemapsBoneIndexComponents()
	{
		var targetModel = CreateBoneRetargetModel([1u, 0u, 2u], [0u]);
		var sourceModel = CreateBoneRetargetModel([2u, 0u, 1u], [0u, 1u, 2u, 3u]);
		var retargeter = new UnitMeshRetargeter();

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		var vertex = Assert.Single(retargeted.RawMeshData[0].Vertices);
		Assert.Equal([2, 1, 0, 3], vertex.Data.Skip(12).Take(4).ToArray());
	}

	[Fact]
	public void Retarget_BoneInfoRemapsBoneIndexComponentsPerSectionMaterial()
	{
		var targetModel = CreateMultiSectionBoneRetargetModel(
			[[0u], [1u, 0u, 2u]],
			[0u, 0u]);
		var sourceModel = CreateMultiSectionBoneRetargetModel(
			[[2u], [2u, 0u, 1u]],
			[0u, 0u]);
		var retargeter = new UnitMeshRetargeter();

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal(0, retargeted.RawMeshData[0].Vertices[0].Data[12]);
		Assert.Equal(2, retargeted.RawMeshData[0].Vertices[1].Data[12]);
	}

	[Fact]
	public void Retarget_BoneInfoUsesSectionMaterialIndexWhenSectionsAreReordered()
	{
		var targetModel = CreateMultiSectionBoneRetargetModel(
			[[0u], [1u, 0u, 2u]],
			[0u, 0u]);
		var targetSections = targetModel.RawMeshData[0].Sections.ToArray();
		targetModel = targetModel with
		{
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections = [targetSections[1], targetSections[0]],
				Triangles = [.. targetSections[1].Triangles, .. targetSections[0].Triangles]
			}]
		};
		var sourceModel = CreateMultiSectionBoneRetargetModel(
			[[2u], [2u, 0u, 1u]],
			[0u, 0u]);
		var sourceSections = sourceModel.RawMeshData[0].Sections.ToArray();
		sourceModel = sourceModel with
		{
			RawMeshData = [sourceModel.RawMeshData[0] with
			{
				Sections = [sourceSections[1], sourceSections[0]],
				Triangles = [.. sourceSections[1].Triangles, .. sourceSections[0].Triangles]
			}]
		};
		var retargeter = new UnitMeshRetargeter();

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		Assert.Equal(2, retargeted.RawMeshData[0].Vertices[1].Data[12]);
		Assert.Equal(0, retargeted.RawMeshData[0].Vertices[0].Data[12]);
	}

	[Fact]
	public void Retarget_ExperimentalFallbackWithReferencedVertexCompactionKeepsTriangles()
	{
		var targetModel = CreateLargeFallbackRetargetModel(vertexCount: 4, [0u, 1u, 2u]);
		var sourceModel = CreateLargeFallbackRetargetModel(vertexCount: ushort.MaxValue + 4, [65536u, 65537u, 65538u]);
		var retargeter = new UnitMeshRetargeter(allowExperimentalLayoutFallback: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		var rawMesh = Assert.Single(retargeted.RawMeshData);
		Assert.Equal(3, rawMesh.Vertices.Count);
		Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(rawMesh.Triangles));
		Assert.Equal([65536f, 0f, 0f], rawMesh.Vertices[0].Components[0].FloatValues);
		Assert.Equal([65537f, 0f, 0f], rawMesh.Vertices[1].Components[0].FloatValues);
		Assert.Equal([65538f, 0f, 0f], rawMesh.Vertices[2].Components[0].FloatValues);
	}

	[Fact]
	public void Write_ReadRoundTrip_PreservesMultiSectionRawMesh()
	{
		var tocData = BuildTwoSectionUnitTocData();
		var gpuData = BuildTwoSectionGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var written = writer.Write(model, tocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		Assert.Equal(60, written.GpuData.Length);
		var stream = reparsed.Streams[0];
		Assert.Equal(36u, stream.VertexBufferSize);
		Assert.Equal(48u, stream.IndexBufferOffset);
		Assert.Equal(12u, stream.IndexBufferSize);
		Assert.Equal(6u, stream.NumIndices);

		var mesh = reparsed.Meshes[0];
		Assert.Equal(2u, mesh.NumSections);
		Assert.Equal(0u, mesh.Sections[0].IndexOffset);
		Assert.Equal(3u, mesh.Sections[0].NumIndices);
		Assert.Equal(3u, mesh.Sections[1].IndexOffset);
		Assert.Equal(3u, mesh.Sections[1].NumIndices);

		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(2, rawMeshData.Sections.Count);
		Assert.Equal(123u, rawMeshData.Sections[0].MaterialSlotId);
		Assert.Equal(456u, rawMeshData.Sections[1].MaterialSlotId);
		Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(rawMeshData.Sections[0].Triangles));
		Assert.Equal(new UnitTriangleIndices(0, 2, 1), Assert.Single(rawMeshData.Sections[1].Triangles));
	}

	[Fact]
	public void Write_ReadRoundTrip_PreservesUInt32IndexBuffer()
	{
		var tocData = BuildUInt32IndexUnitTocData();
		var gpuData = BuildUInt32IndexGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var written = writer.Write(model, tocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		Assert.Equal(60, written.GpuData.Length);
		var stream = reparsed.Streams[0];
		Assert.Equal(1u, stream.IndexBufferType);
		Assert.Equal(48u, stream.IndexBufferOffset);
		Assert.Equal(12u, stream.IndexBufferSize);

		var rawMeshData = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(new UnitTriangleIndices(0, 1, 2), Assert.Single(rawMeshData.Triangles));
	}

	[Fact]
	public void Write_ReadRoundTrip_ExpandsMeshMetadataAndMaterialBindings()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var rawMesh = Assert.Single(model.RawMeshData);
		var secondSection = rawMesh.Sections[0] with
		{
			MaterialIndex = 1,
			MaterialSlotId = 456u,
			Triangles = [new UnitTriangleIndices(0, 2, 1)]
		};
		var expandedRawMesh = rawMesh with
		{
			Sections = [rawMesh.Sections[0], secondSection],
			Triangles = rawMesh.Sections[0].Triangles.Concat(secondSection.Triangles).ToArray()
		};
		var expandedMesh = model.Meshes[0] with
		{
			MaterialSlotIds = [123u, 456u],
			Sections =
			[
				model.Meshes[0].Sections[0],
				model.Meshes[0].Sections[0] with { MaterialIndex = 1, IndexOffset = 3, NumIndices = 3 }
			]
		};
		var expandedModel = model with
		{
			Meshes = [expandedMesh],
			Materials = [new UnitMaterialBinding(123u, 0x8877665544332211ul), new UnitMaterialBinding(456u, 0x1122334455667788ul)],
			RawMeshData = [expandedRawMesh]
		};

		var written = writer.Write(expandedModel, tocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		var mesh = Assert.Single(reparsed.Meshes);
		Assert.Equal([123u, 456u], mesh.MaterialSlotIds);
		Assert.Equal(2u, mesh.NumSections);
		Assert.Equal([0u, 1u], mesh.Sections.Select(section => section.MaterialIndex));
		Assert.Equal(
			[new UnitMaterialBinding(123u, 0x8877665544332211ul), new UnitMaterialBinding(456u, 0x1122334455667788ul)],
			reparsed.Materials);
		var reparsedRawMesh = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(2, reparsedRawMesh.Sections.Count);
		Assert.Equal(456u, reparsedRawMesh.Sections[1].MaterialSlotId);
		Assert.Equal(new UnitTriangleIndices(0, 2, 1), Assert.Single(reparsedRawMesh.Sections[1].Triangles));
	}

	[Fact]
	public void Retarget_WriteReadRoundTrip_ExpandsMaterialSectionsWhenExplicitlyEnabled()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildMinimalUnitTocData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();
		var targetModel = reader.Read(targetTocData, BuildMinimalGpuData());
		var sourceModel = reader.Read(sourceTocData, BuildMinimalGpuData());
		var sourceRawMesh = Assert.Single(sourceModel.RawMeshData);
		var sourceSecondSection = sourceRawMesh.Sections[0] with
		{
			MaterialIndex = 1,
			MaterialSlotId = 789u,
			Triangles = [new UnitTriangleIndices(0, 2, 1)]
		};
		sourceModel = sourceModel with
		{
			Meshes = [sourceModel.Meshes[0] with
			{
				MaterialSlotIds = [456u, 789u],
				Sections =
				[
					sourceModel.Meshes[0].Sections[0] with { MaterialSlotId = 456u },
					sourceModel.Meshes[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 789u, IndexOffset = 3, NumIndices = 3 }
				]
			}],
			Materials = [new UnitMaterialBinding(456u, 0x0102030405060708ul), new UnitMaterialBinding(789u, 0x1020304050607080ul)],
			RawMeshData = [sourceRawMesh with
			{
				Sections = [sourceRawMesh.Sections[0] with { MaterialSlotId = 456u }, sourceSecondSection],
				Triangles = [.. sourceRawMesh.Sections[0].Triangles, .. sourceSecondSection.Triangles]
			}]
		};
		var retargeter = new UnitMeshRetargeter(
			allowExperimentalLayoutFallback: true,
			propagateSourceMaterials: true,
			allowMaterialSectionExpansion: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);
		var written = writer.Write(retargeted, targetTocData);
		var reparsed = reader.Read(written.TocData, written.GpuData);

		var mesh = Assert.Single(reparsed.Meshes);
		Assert.Equal([123u, 0u], mesh.MaterialSlotIds);
		Assert.Equal([0u, 1u], mesh.Sections.Select(section => section.MaterialIndex));
		Assert.Equal(
			[new UnitMaterialBinding(123u, 0x0102030405060708ul), new UnitMaterialBinding(0u, 0x1020304050607080ul)],
			reparsed.Materials);
		var rawMesh = Assert.Single(reparsed.RawMeshData);
		Assert.Equal(2, rawMesh.Sections.Count);
		Assert.Equal(0u, rawMesh.Sections[1].MaterialSlotId);
		Assert.Equal(new UnitTriangleIndices(0, 2, 1), Assert.Single(rawMesh.Sections[1].Triangles));
	}

	[Fact]
	public void Retarget_MaterialExpansion_PreservesConflictingTargetSlotAndAppendsSourceBinding()
	{
		var targetModel = CreateLargeFallbackRetargetModel(4, [0u, 1u, 2u]);
		targetModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with
			{
				MaterialSlotIds = [123u, 456u],
				Sections =
				[
					targetModel.Meshes[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 123u },
					targetModel.Meshes[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 456u }
				]
			}],
			Materials = [new UnitMaterialBinding(123u, 0x1111111111111111ul), new UnitMaterialBinding(456u, 0x2222222222222222ul)],
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections =
				[
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 123u },
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 456u }
				]
			}]
		};
		var sourceModel = targetModel with
		{
			Meshes = [targetModel.Meshes[0] with { MaterialSlotIds = [700u, 800u] }],
			Materials = [new UnitMaterialBinding(700u, 0x1111111111111111ul), new UnitMaterialBinding(800u, 0x3333333333333333ul)],
			RawMeshData = [targetModel.RawMeshData[0] with
			{
				Sections =
				[
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 0, MaterialSlotId = 700u },
					targetModel.RawMeshData[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 800u }
				]
			}]
		};
		var retargeter = new UnitMeshRetargeter(
			allowExperimentalLayoutFallback: true,
			propagateSourceMaterials: true,
			allowMaterialSectionExpansion: true);

		var retargeted = retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0);

		var mesh = Assert.Single(retargeted.Meshes);
		Assert.Equal([123u, 456u, 0u], mesh.MaterialSlotIds);
		Assert.Equal([0u, 2u], mesh.Sections.Select(section => section.MaterialIndex));
		Assert.Equal(
			[new UnitMaterialBinding(123u, 0x1111111111111111ul), new UnitMaterialBinding(456u, 0x2222222222222222ul), new UnitMaterialBinding(0u, 0x3333333333333333ul)],
			retargeted.Materials);
		Assert.Equal([123u, 0u], retargeted.RawMeshData[0].Sections.Select(section => section.MaterialSlotId));
	}

	[Fact]
	public void Write_RawMeshWithTooManySections_Throws()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var rawMesh = model.RawMeshData[0];
		var expandedRawMesh = rawMesh with { Sections = [rawMesh.Sections[0], rawMesh.Sections[0]] };
		var expandedModel = model with { RawMeshData = [expandedRawMesh] };

		var ex = Assert.Throws<InvalidDataException>(() => writer.Write(expandedModel, tocData));
		Assert.Contains("more sections", ex.Message);
	}

	[Fact]
	public void Write_VertexDataLargerThanStride_Throws()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var rawMesh = model.RawMeshData[0];
		var vertices = rawMesh.Vertices
			.Select((vertex, index) => index == 0 ? vertex with { Data = new byte[13] } : vertex)
			.ToArray();
		var invalidRawMesh = rawMesh with { Vertices = vertices };
		var invalidModel = model with { RawMeshData = [invalidRawMesh] };

		var ex = Assert.Throws<InvalidDataException>(() => writer.Write(invalidModel, tocData));
		Assert.Contains("larger than the stream vertex stride", ex.Message);
	}

	[Fact]
	public void Write_UInt16IndexOverflow_Throws()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var rawMesh = model.RawMeshData[0];
		var vertices = Enumerable.Range(0, 65537)
			.Select(index => rawMesh.Vertices[0] with { Index = (uint)index })
			.ToArray();
		var section = rawMesh.Sections[0] with { Triangles = [new UnitTriangleIndices(0, 1, 65536)] };
		var invalidRawMesh = rawMesh with
		{
			Sections = [section],
			Triangles = section.Triangles,
			Vertices = vertices
		};
		var invalidModel = model with { RawMeshData = [invalidRawMesh] };

		var ex = Assert.Throws<InvalidDataException>(() => writer.Write(invalidModel, tocData));
		Assert.Contains("16-bit Unit index buffer", ex.Message);
	}

	[Fact]
	public void Write_TriangleReferencesMissingVertex_Throws()
	{
		var tocData = BuildMinimalUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var writer = new UnitMeshWriter();

		var model = reader.Read(tocData, gpuData);
		var rawMesh = model.RawMeshData[0];
		var section = rawMesh.Sections[0] with { Triangles = [new UnitTriangleIndices(0, 1, 3)] };
		var invalidRawMesh = rawMesh with
		{
			Sections = [section],
			Triangles = section.Triangles
		};
		var invalidModel = model with { RawMeshData = [invalidRawMesh] };

		var ex = Assert.Throws<InvalidDataException>(() => writer.Write(invalidModel, tocData));
		Assert.Contains("outside the mesh vertex range", ex.Message);
	}

	[Fact]
	public void Retarget_DifferentVertexStride_Throws()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildStride16UnitTocData();
		var targetGpuData = BuildMinimalGpuData();
		var sourceGpuData = BuildStride16GpuData();
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter();

		var targetModel = reader.Read(targetTocData, targetGpuData);
		var sourceModel = reader.Read(sourceTocData, sourceGpuData);

		var ex = Assert.Throws<InvalidDataException>(() => retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0));
		Assert.Contains("vertex strides differ", ex.Message);
	}

	[Fact]
	public void Retarget_DifferentComponentCount_Throws()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildTwoComponentUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter();

		var targetModel = reader.Read(targetTocData, gpuData);
		var sourceModel = reader.Read(sourceTocData, gpuData);

		var ex = Assert.Throws<InvalidDataException>(() => retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0));
		Assert.Contains("component counts differ", ex.Message);
	}

	[Fact]
	public void Retarget_DifferentComponentLayout_Throws()
	{
		var targetTocData = BuildMinimalUnitTocData();
		var sourceTocData = BuildVec2ComponentUnitTocData();
		var gpuData = BuildMinimalGpuData();
		var reader = new UnitMeshReader();
		var retargeter = new UnitMeshRetargeter();

		var targetModel = reader.Read(targetTocData, gpuData);
		var sourceModel = reader.Read(sourceTocData, gpuData);

		var ex = Assert.Throws<InvalidDataException>(() => retargeter.ReplaceRawMesh(targetModel, 0, sourceModel, 0));
		Assert.Contains("component layouts differ", ex.Message);
	}

	private static UnitMeshModel CreateBoneRetargetModel(uint[] remapFakeIndices, uint[] vertexBoneIndices)
		=> CreateBoneRetargetModel([remapFakeIndices], [vertexBoneIndices], [new UnitTriangleIndices(0, 0, 0)]);

	private static UnitMeshModel CreateMultiSectionBoneRetargetModel(uint[][] remapFakeIndicesByMaterial, uint[] vertexBoneIndices)
		=> CreateBoneRetargetModel(
			remapFakeIndicesByMaterial,
			vertexBoneIndices.Select(index => new[] { index, 0u, 0u, 0u }).ToArray(),
			Enumerable.Range(0, remapFakeIndicesByMaterial.Length).Select(index => new UnitTriangleIndices((uint)index, (uint)index, (uint)index)).ToArray());

	private static UnitMeshModel CreateBoneRetargetModel(uint[][] remapFakeIndicesByMaterial, uint[][] vertexBoneIndices, UnitTriangleIndices[] triangles)
	{
		var stream = new UnitStreamInfo(
			0,
			0,
			0,
			2,
			0,
			1,
			16,
			0,
			3,
			0,
			0,
			16,
			16,
			6,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			]);
		var sections = remapFakeIndicesByMaterial
			.Select((_, index) => new UnitRawMeshSectionData((uint)index, 123u + (uint)index, [triangles[index]]))
			.ToArray();
		var vertices = vertexBoneIndices.Select((boneIndices, vertexIndex) =>
		{
			var vertexData = new byte[16];
			for (var i = 0; i < Math.Min(4, boneIndices.Length); i++)
			{
				vertexData[12 + i] = (byte)boneIndices[i];
			}

			return new UnitRawVertexRecord(
				(uint)vertexIndex,
				vertexData,
				[
					new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, [0f, 0f, 0f], [], vertexData.Take(12).ToArray()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, [], boneIndices, vertexData.Skip(12).Take(4).ToArray())
				]);
		}).ToArray();

		var rawMesh = new UnitRawMeshData(0, 0x12345678, 0, 0, sections, sections.SelectMany(section => section.Triangles).ToArray(), vertices);
		var mesh = new UnitMeshInfo(
			0,
			0,
			0x12345678,
			0,
			0,
			0,
			1,
			0,
			(uint)sections.Length,
			0,
			UnitMeshSemanticInfo.Empty(0, 0),
			sections.Select(section => section.MaterialSlotId).ToArray(),
			sections.Select((section, index) => new UnitMeshSectionInfo((uint)index, section.MaterialIndex, section.MaterialSlotId, 0, (uint)vertices.Length, (uint)(index * 3), 3, 0)).ToArray());
		var boneInfo = new UnitBoneInfo(
			0,
			0,
			3,
			0,
			0,
			0,
			[10u, 20u, 30u],
			remapFakeIndicesByMaterial.Select((remap, index) => new UnitBoneRemap(index, 0, remap)).ToArray());

		return new UnitMeshModel(
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			[boneInfo],
			[stream],
			[mesh],
			Array.Empty<UnitMaterialBinding>(),
			Array.Empty<UnitRawMeshSummary>(),
			[rawMesh]);
	}

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
		WriteUInt32(data, stream + 8, 0); // component type: position
		WriteUInt32(data, stream + 12, 2); // component format: vec3_float
		WriteUInt32(data, stream + 16, 0); // component index
		WriteUInt64(data, stream + 20, 0); // component unknown
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

	private static byte[] BuildUnitTocDataWithBoneInfo()
	{
		var source = BuildMinimalUnitTocData();
		var data = new byte[0x800];
		Array.Copy(source, data, source.Length);

		const int streamInfoOffset = 0x300;
		const int meshInfoOffset = 0x500;
		const int materialsOffset = 0x680;
		Array.Copy(source, 0x80, data, streamInfoOffset, 0x1d0);
		Array.Copy(source, 0x260, data, meshInfoOffset, 0x100);
		Array.Copy(source, 0x340, data, materialsOffset, 0x10);

		WriteUInt32(data, 0x5c, streamInfoOffset);
		WriteUInt32(data, 0x60, 0x700);
		WriteUInt32(data, 0x64, meshInfoOffset);
		WriteUInt32(data, 0x70, materialsOffset);

		const int boneInfoOffset = 0x100;
		const int boneInfoRecordOffset = 0x20;
		const int boneInfoRecord = boneInfoOffset + boneInfoRecordOffset;
		const int realIndicesOffset = 0x40;
		const int remapOffset = 0x50;

		WriteUInt32(data, 0x58, boneInfoOffset);
		WriteUInt32(data, boneInfoOffset, 1);
		WriteUInt32(data, boneInfoOffset + 4, boneInfoRecordOffset);

		WriteUInt32(data, boneInfoRecord, 3);
		WriteUInt32(data, boneInfoRecord + 4, 0x10);
		WriteUInt32(data, boneInfoRecord + 8, realIndicesOffset);
		WriteUInt32(data, boneInfoRecord + 12, remapOffset);

		WriteUInt32(data, boneInfoRecord + realIndicesOffset, 10);
		WriteUInt32(data, boneInfoRecord + realIndicesOffset + 4, 20);
		WriteUInt32(data, boneInfoRecord + realIndicesOffset + 8, 30);

		var remap = boneInfoRecord + remapOffset;
		WriteUInt32(data, remap, 2);
		WriteUInt32(data, remap + 4, 20);
		WriteUInt32(data, remap + 8, 3);
		WriteUInt32(data, remap + 12, 32);
		WriteUInt32(data, remap + 16, 2);

		WriteUInt32(data, remap + 20, 0);
		WriteUInt32(data, remap + 24, 1);
		WriteUInt32(data, remap + 28, 2);
		WriteUInt32(data, remap + 32, 2);
		WriteUInt32(data, remap + 36, 0);

		return data;
	}

	private static byte[] BuildTwoComponentUnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int stream = 0x80 + 0x20;
		const int streamFields = stream + 8 + 320;
		WriteUInt32(data, stream + 28, 1); // second component type: normal
		WriteUInt32(data, stream + 32, 0); // second component format: float
		WriteUInt32(data, streamFields, 2);
		WriteUInt32(data, streamFields + 4, 0);
		return data;
	}

	private static byte[] BuildVec2ComponentUnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int stream = 0x80 + 0x20;
		WriteUInt32(data, stream + 12, 1); // component format: vec2_float
		return data;
	}

	private static byte[] BuildUInt32IndexUnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int streamFields = 0x80 + 0x20 + 8 + 320;
		WriteUInt32(data, streamFields + 68, 1);
		WriteUInt32(data, streamFields + 100, 12);
		return data;
	}

	private static byte[] BuildStride16UnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int streamFields = 0x80 + 0x20 + 8 + 320;
		WriteUInt32(data, streamFields + 28, 16);
		WriteUInt32(data, streamFields + 92, 48);
		WriteUInt32(data, streamFields + 96, 48);
		return data;
	}

	private static byte[] BuildTwoSectionUnitTocData()
	{
		var data = BuildMinimalUnitTocData();
		const int streamFields = 0x80 + 0x20 + 8 + 320;
		WriteUInt32(data, streamFields + 80, 6);
		WriteUInt32(data, streamFields + 104, 12);

		const int mesh = 0x260 + 0x20;
		var meshCursor = mesh + 40 + 24 + 40;
		WriteUInt32(data, meshCursor, 2); meshCursor += 4;
		WriteUInt32(data, meshCursor, 112); meshCursor += 4;
		WriteUInt64(data, meshCursor, 0); meshCursor += 8;
		WriteUInt32(data, meshCursor, 2); meshCursor += 4;
		WriteUInt32(data, meshCursor, 120); meshCursor += 4;
		WriteUInt32(data, meshCursor, 123); meshCursor += 4;
		WriteUInt32(data, meshCursor, 456); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 1); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 3); meshCursor += 4;
		WriteUInt32(data, meshCursor, 0);

		WriteUInt32(data, 0x340, 2);
		WriteUInt32(data, 0x344, 123);
		WriteUInt32(data, 0x348, 456);
		WriteUInt64(data, 0x34c, 0x8877665544332211ul);
		WriteUInt64(data, 0x354, 0x0123456789abcdeful);
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

	private static byte[] BuildBoneNamesData(uint hash, string name)
	{
		var nameBytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
		var data = new byte[20 + nameBytes.Length];
		WriteUInt32(data, 0, 1);
		WriteUInt32(data, 4, 1);
		WriteSingle(data, 8, 0f);
		WriteUInt32(data, 12, hash);
		WriteUInt32(data, 16, 1);
		nameBytes.CopyTo(data.AsSpan(20));
		return data;
	}

	private static byte[] BuildTwoSectionGpuData()
	{
		var data = BuildMinimalGpuData();
		WriteUInt16(data, 42, 0);
		WriteUInt16(data, 44, 2);
		WriteUInt16(data, 46, 1);
		return data;
	}

	private static byte[] BuildUInt32IndexGpuData()
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
		WriteUInt32(data, 36, 0);
		WriteUInt32(data, 40, 1);
		WriteUInt32(data, 44, 2);
		return data;
	}

	private static byte[] BuildStride16GpuData()
	{
		var data = new byte[64];
		WriteSingle(data, 0, 10f);
		WriteSingle(data, 4, 20f);
		WriteSingle(data, 8, 30f);
		WriteSingle(data, 16, 40f);
		WriteSingle(data, 20, 50f);
		WriteSingle(data, 24, 60f);
		WriteSingle(data, 32, 70f);
		WriteSingle(data, 36, 80f);
		WriteSingle(data, 40, 90f);
		WriteUInt16(data, 48, 0);
		WriteUInt16(data, 50, 1);
		WriteUInt16(data, 52, 2);
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

	private static UnitMeshModel CreateLargeFallbackRetargetModel(int vertexCount, IReadOnlyList<uint> triangleIndices)
	{
		var vertices = Enumerable.Range(0, vertexCount)
			.Select(index =>
			{
				var data = CreatePositionVertexData(index, stride: 12);
				return new UnitRawVertexRecord(
					(uint)index,
					data,
					[new UnitVertexComponentValue(0, "position", 0, "vec3_float", 0, [index, 0f, 0f], [], data)]);
			})
			.ToArray();
		var triangle = new UnitTriangleIndices(triangleIndices[0], triangleIndices[1], triangleIndices[2]);
		var rawMesh = new UnitRawMeshData(
			0,
			0x12345678,
			0,
			0,
			[new UnitRawMeshSectionData(0, 123, [triangle])],
			[triangle],
			vertices);

		return new UnitMeshModel(
			0x00A4CD36,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			0,
			UnitCustomizationInfo.Empty,
			[],
			[new UnitStreamInfo(0, 0, 0, 1, 0, (uint)vertexCount, 12, 0, 3, 0, 0, (uint)(vertexCount * 12), 0, 6, [new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, 12)])],
			[new UnitMeshInfo(0, 0, 0x12345678, 0, 0, 0, 1, 0, 1, 0, UnitMeshSemanticInfo.Empty(0, 0), [123], [new UnitMeshSectionInfo(0, 0, 123, 0, (uint)vertexCount, 0, 3, 0)])],
			[new UnitMaterialBinding(123, 0x8877665544332211ul)],
			[new UnitRawMeshSummary(0, 0x12345678, 0, 0, (uint)vertexCount, 3, 1, 1, true, true)],
			[rawMesh]);
	}

	private static byte[] CreatePositionVertexData(int x, int stride)
	{
		var data = new byte[stride];
		WriteSingle(data, 0, x);
		WriteSingle(data, 4, 0f);
		WriteSingle(data, 8, 0f);
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