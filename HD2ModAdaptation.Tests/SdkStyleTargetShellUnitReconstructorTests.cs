using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies SDK-style target-shell reconstruction replaces explicit slots and minifies all other current target slots.
public sealed class SdkStyleTargetShellUnitReconstructorTests
{
	private static readonly AssetKey SourceKey = new(PatchUnitMeshReader.UnitTypeId, 0x1001);
	private static readonly AssetKey TargetKey = new(PatchUnitMeshReader.UnitTypeId, 0x2001);

	[Fact]
	public void Reconstruct_ReplacesMappedSlotAndMinifiesEveryOtherTargetSlot()
	{
		var source = CreatePatchUnit(SourceKey, CreateModel(vertexSeed: 7, meshCount: 1));
		var target = CreateTargetUnit(CreateModel(vertexSeed: 1, meshCount: 2));

		var result = new SdkStyleTargetShellUnitReconstructor().Reconstruct(
			target,
			new[] { source },
			new[] { new TargetShellMeshMapping(SourceKey, 0, 0) });

		Assert.Equal(new[] { 1 }, result.MinifiedTargetMeshInfoIndexes);
		Assert.Equal(2, result.CoveredTargetMeshCount);
		Assert.Equal(3, result.Model.RawMeshData.Single(mesh => mesh.MeshInfoIndex == 1).Vertices.Count);
		Assert.NotEmpty(result.WriteResult.TocData);
		Assert.NotEmpty(result.WriteResult.GpuData);
	}

	[Fact]
	public void Reconstruct_WithoutMappings_MinifiesEveryTargetSlot()
	{
		var target = CreateTargetUnit(CreateModel(vertexSeed: 1, meshCount: 2));

		var result = new SdkStyleTargetShellUnitReconstructor().Reconstruct(
			target,
			Array.Empty<PatchUnitMesh>(),
			Array.Empty<TargetShellMeshMapping>());

		Assert.Empty(result.Replacements);
		Assert.Equal(new[] { 0, 1 }, result.MinifiedTargetMeshInfoIndexes);
		Assert.All(result.Model.RawMeshData, mesh => Assert.Equal(3, mesh.Vertices.Count));
		Assert.NotEmpty(result.WriteResult.TocData);
		Assert.NotEmpty(result.WriteResult.GpuData);
	}

	[Fact]
	public void Reencode_RebuildingInverseJointMatrices_PreservesTheCompleteSourcePaletteOrderForEveryTargetSection()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1]), new UnitBoneRemap(1, 0, [0, 1])])],
			RawMeshData = [CreateModel(vertexSeed: 7, meshCount: 1).RawMeshData[0] with
			{
				Sections = [new UnitRawMeshSectionData(0, 20, [new UnitTriangleIndices(0, 1, 2)]), new UnitRawMeshSectionData(1, 21, [new UnitTriangleIndices(0, 1, 2)])]
			}]
		};
		var targetModel = sourceModel with { TransformNameHashes = [101, 102] };

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true).Reencode(targetModel, 0, sourceModel, 0);

		Assert.Equal(new uint[] { 0, 1 }, result.RebuiltTargetBoneInfo.RealIndices);
		Assert.All(result.RebuiltTargetBoneInfo.Remaps, remap => Assert.Equal(new uint[] { 0, 1 }, remap.FakeIndices));
	}

	[Fact]
	public void Reencode_RebuildingInverseJointMatrices_DoesNotReuseTheTargetShellPaletteOrder()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102, 103],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102, 103]),
			Streams = [CreateSkinnedStream()],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [2, 0, 1], [new UnitBoneRemap(0, 0, [1, 2, 0])])],
			RawMeshData = [CreateSkinnedRawMesh()]
		};
		var targetModel = sourceModel with
		{
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1, 2], [new UnitBoneRemap(0, 0, [0, 1, 2])])]
		};

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true).Reencode(targetModel, 0, sourceModel, 0);

		Assert.Equal(new uint[] { 2, 0, 1 }, result.RebuiltTargetBoneInfo.RealIndices);
		Assert.Equal(new uint[] { 1, 2, 0 }, Assert.Single(result.RebuiltTargetBoneInfo.Remaps).FakeIndices);
	}

	[Fact]
	public void Reencode_CanonicalTargetBake_OmitsInactiveSourcePaletteBones()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102, 103],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102, 103]),
			Streams = [CreateSkinnedStream()],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [2, 0, 1], [new UnitBoneRemap(0, 0, [1, 2, 0])])],
			RawMeshData = [CreateSkinnedRawMesh()]
		};
		var targetModel = sourceModel with
		{
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1, 2], [new UnitBoneRemap(0, 0, [0, 1, 2])])]
		};

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true, canonicalBoneHashOrder: [102, 103, 101]).Reencode(targetModel, 0, sourceModel, 0);

		Assert.Equal(new uint[] { 0 }, result.RebuiltTargetBoneInfo.RealIndices);
		Assert.Equal(new uint[] { 0 }, Assert.Single(result.RebuiltTargetBoneInfo.Remaps).FakeIndices);
	}

	[Fact]
	public void Reencode_CanonicalTargetBake_PreservesAnExtraEmptyTargetSection()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102]),
			Streams = [CreateSkinnedStream()],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1]), new UnitBoneRemap(1, 0, Array.Empty<uint>())])],
			RawMeshData = [CreateSkinnedRawMesh()]
		};
		var sourceSection = sourceModel.RawMeshData[0].Sections[0];
		var targetModel = sourceModel with
		{
			RawMeshData = [sourceModel.RawMeshData[0] with { Sections = [sourceSection, new UnitRawMeshSectionData(1, 21, Array.Empty<UnitTriangleIndices>())] }]
		};

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true, canonicalBoneHashOrder: [101, 102]).Reencode(targetModel, 0, sourceModel, 0);

		Assert.Equal(2, Assert.Single(result.Model.RawMeshData).Sections.Count);
		Assert.Empty(result.Model.RawMeshData[0].Sections[1].Triangles);
	}

	[Fact]
	public void Reencode_CanonicalTargetBake_NormalizesMultipleSourceIndexGroupsIntoCanonicalLayout()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102]),
			Streams = [new UnitStreamInfo(0, 128, 0, 4, 0, 3, 32, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(7, "bone_weight", 33, "vec4_half", 0, 0, 8),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 1, 0, 4)
			])],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1])])],
			RawMeshData = [CreateSkinnedRawMesh() with
			{
				Vertices = Enumerable.Range(0, 3).Select(index => new UnitRawVertexRecord((uint)index, Array.Empty<byte>(),
				[
					new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, [0f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(7, "bone_weight", 33, "vec4_half", 0, [.4f, .6f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, Array.Empty<float>(), [0u, 1u, 0u, 0u], Array.Empty<byte>()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 1, Array.Empty<float>(), [0u, 1u, 0u, 0u], Array.Empty<byte>())
				])).ToArray()
			}]
		};
		var targetModel = sourceModel with
		{
			Streams = [CreateSkinnedStream()],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1])])]
		};

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true, canonicalBoneHashOrder: [101, 102]).Reencode(targetModel, 0, sourceModel, 0);
		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];

		Assert.Equal(new byte[] { 1, 0, 0, 0 }, vertex.Data.Skip(20).Take(4));
		Assert.Equal(0.6f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertex.Data, 12)), 2);
		Assert.Equal(0.4f, (float)BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(vertex.Data, 14)), 2);
	}

	[Fact]
	public void Reencode_RebuildingInverseJointMatrices_SkipsUnreachableUnusedSourcePaletteBones()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			TransformNameHashes = [101, 102, 103],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102, 103]),
			Streams = [CreateSkinnedStream()],
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [2, 0, 1], [new UnitBoneRemap(0, 0, [1, 2, 0])])],
			RawMeshData = [CreateSkinnedRawMesh()]
		};
		var targetModel = sourceModel with
		{
			TransformNameHashes = [101, 102],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102]),
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1])])]
		};

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true).Reencode(targetModel, 0, sourceModel, 0);

		Assert.Equal(new uint[] { 0, 1 }, result.RebuiltTargetBoneInfo.RealIndices);
		Assert.Equal(new uint[] { 0, 1 }, Assert.Single(result.RebuiltTargetBoneInfo.Remaps).FakeIndices);
	}

	[Fact]
	public void Reencode_RebuildingInverseJointMatrices_TransformsOnlyVertexPositions()
	{
		var sourceModel = CreateSurfaceVectorModel(meshTransformX: 4f);
		var targetModel = CreateSurfaceVectorModel(meshTransformX: 0f);

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true).Reencode(targetModel, 0, sourceModel, 0);
		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];

		Assert.Equal(1f, BitConverter.ToSingle(vertex.Data, 0));
		Assert.Equal(new float[] { 0f, 1f, 0f }, ReadVector3(vertex.Data, 12));
		Assert.Equal(new float[] { 1f, 0f, 0f }, ReadVector3(vertex.Data, 24));
		Assert.Equal(new float[] { 0f, 0f, 1f }, ReadVector3(vertex.Data, 36));
	}

	[Fact]
	public void Reencode_RebuildingInverseJointMatrices_RetargetsPositionByWeightedBindPose()
	{
		var sourceModel = CreateWeightedBindPoseModel(0f, 0f);
		var targetModel = CreateWeightedBindPoseModel(2f, 6f);

		var result = new SdkStyleMeshReencoder(rebuildTargetInverseJointMatrices: true).Reencode(targetModel, 0, sourceModel, 0);
		var vertex = Assert.Single(result.Model.RawMeshData).Vertices[0];

		Assert.Equal(5f, BitConverter.ToSingle(vertex.Data, 0), 4);
	}

	[Fact]
	public void Reconstruct_WithSectionRebuild_HandlesDifferentSourceAndTargetSectionCounts()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1);
		var sourceRawMesh = sourceModel.RawMeshData[0];
		var sourceSection = sourceRawMesh.Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21 };
		var source = CreatePatchUnit(SourceKey, sourceModel with
		{
			Materials = [new UnitMaterialBinding(20, 0x200), new UnitMaterialBinding(21, 0x201)],
			Meshes = [sourceModel.Meshes[0] with
			{
				MaterialSlotIds = [20, 21],
				Sections = [sourceModel.Meshes[0].Sections[0], sourceModel.Meshes[0].Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21 }]
			}],
			RawMeshData = [sourceRawMesh with { Sections = [sourceSection, sourceSection] }]
		});
		var baseTarget = CreateTargetUnit(CreateModel(vertexSeed: 1, meshCount: 2));
		var rawTarget = baseTarget.Model.RawMeshData[0];
		var target = baseTarget with
		{
			Model = baseTarget.Model with
			{
				RawMeshData = baseTarget.Model.RawMeshData.Select(mesh => mesh.MeshInfoIndex == 0
					? mesh with { Sections = [rawTarget.Sections[0], rawTarget.Sections[0]] }
					: mesh).ToArray()
			}
		};

		var result = new SdkStyleTargetShellUnitReconstructor(allowSectionRebuild: true).Reconstruct(
			target,
			new[] { source },
			new[] { new TargetShellMeshMapping(SourceKey, 0, 0) });

		Assert.Equal(2, result.Model.RawMeshData.Single(mesh => mesh.MeshInfoIndex == 0).Sections.Count);
		Assert.Equal(new[] { 1 }, result.MinifiedTargetMeshInfoIndexes);
	}

	[Fact]
	public void Reconstruct_IgnoresEmptySourceSectionWhenEffectiveSectionCountMatchesTarget()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1);
		var sourceRawMesh = sourceModel.RawMeshData[0];
		var source = CreatePatchUnit(SourceKey, sourceModel with
		{
			RawMeshData = [sourceRawMesh with
			{
				Sections = [sourceRawMesh.Sections[0], sourceRawMesh.Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21, Triangles = Array.Empty<UnitTriangleIndices>() }]
			}]
		});
		var target = CreateTargetUnit(CreateModel(vertexSeed: 1, meshCount: 1));

		var result = new SdkStyleTargetShellUnitReconstructor().Reconstruct(
			target,
			[source],
			[new TargetShellMeshMapping(SourceKey, 0, 0)]);

		var rebuilt = Assert.Single(result.Model.RawMeshData);
		Assert.Single(rebuilt.Sections);
		Assert.Equal(sourceRawMesh.Sections[0].Triangles.Count, rebuilt.Sections[0].Triangles.Count);
	}

	[Fact]
	public void Reconstruct_PreservesEmptySourceSectionWhenOriginalCountMatchesTarget()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1);
		var sourceRawMesh = sourceModel.RawMeshData[0];
		var sections = new[]
		{
			sourceRawMesh.Sections[0],
			sourceRawMesh.Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21, Triangles = Array.Empty<UnitTriangleIndices>() }
		};
		var source = CreatePatchUnit(SourceKey, sourceModel with { RawMeshData = [sourceRawMesh with { Sections = sections }] });
		var baseTargetModel = CreateModel(vertexSeed: 1, meshCount: 1);
		var targetRawMesh = baseTargetModel.RawMeshData[0];
		var targetModel = baseTargetModel with
		{
			Meshes = baseTargetModel.Meshes.Select(mesh => mesh with
			{
				MaterialSlotIds = [20, 21],
				Sections = [mesh.Sections[0], mesh.Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21 }]
			}).ToArray(),
			Materials = [new UnitMaterialBinding(20, 0x100), new UnitMaterialBinding(21, 0x101)],
			RawMeshData = [targetRawMesh with { Sections = sections }]
		};
		var target = CreateTargetUnit(targetModel);

		var result = new SdkStyleTargetShellUnitReconstructor().Reconstruct(
			target,
			[source],
			[new TargetShellMeshMapping(SourceKey, 0, 0)]);

		var rebuilt = Assert.Single(result.Model.RawMeshData);
		Assert.Equal(2, rebuilt.Sections.Count);
		Assert.Empty(rebuilt.Sections[1].Triangles);
	}

	[Fact]
	public void Reconstruct_WithSectionRebuild_PropagatesOnlyAllowedSourceMaterials()
	{
		const ulong sourceMaterial = 0x200;
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Materials = [new UnitMaterialBinding(20, sourceMaterial)]
		};
		var source = CreatePatchUnit(SourceKey, sourceModel);
		var baseTarget = CreateModel(vertexSeed: 1, meshCount: 1);
		var rawTarget = baseTarget.RawMeshData[0];
		var targetModel = baseTarget with
		{
			Materials = [new UnitMaterialBinding(20, 0x100), new UnitMaterialBinding(21, 0x101)],
			RawMeshData = [rawTarget with { Sections = [rawTarget.Sections[0], rawTarget.Sections[0] with { MaterialIndex = 1, MaterialSlotId = 21 }] }]
		};

		var allowed = new SdkStyleMeshReencoder(
			allowSectionRebuild: true,
			propagateSourceMaterials: true,
			allowedSourceMaterialIds: new HashSet<ulong> { sourceMaterial }).Reencode(
			targetModel,
			0,
			sourceModel,
			0);
		var rejected = new SdkStyleMeshReencoder(
			allowSectionRebuild: true,
			propagateSourceMaterials: true,
			allowedSourceMaterialIds: new HashSet<ulong>()).Reencode(
			targetModel,
			0,
			sourceModel,
			0);

		Assert.Equal(sourceMaterial, Assert.Single(allowed.Model.Materials, binding => binding.SectionId == 20).MaterialId);
		Assert.Equal(new[] { sourceMaterial }, allowed.SourceMaterialIds);
		Assert.Equal(0x100ul, Assert.Single(rejected.Model.Materials, binding => binding.SectionId == 20).MaterialId);
		Assert.Empty(rejected.SourceMaterialIds);
	}

	[Fact]
	public void Reconstruct_PreservesSourceVertexColorLayoutWhenTargetStreamOmitsIt()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0x123456789abcdef0, 2, 0, 3, 16, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(5, "color", 4, "rgba_r8g8b8a8", 0, 0, 4)
			])]
		};
		var target = CreateTargetUnit(CreateModel(vertexSeed: 1, meshCount: 1), CreateToc(materialBindingCount: 1));

		var result = new SdkStyleTargetShellUnitReconstructor(planSourceStreamLayout: true).Reconstruct(
			target,
			[new PatchUnitMesh(new PatchTocEntry(SourceKey, "source.patch", "source.patch"), new PatchEntryPayload(new PatchTocEntry(SourceKey, "source.patch", "source.patch"), CreateToc(), Array.Empty<byte>(), Array.Empty<byte>()), sourceModel)],
			[new TargetShellMeshMapping(SourceKey, 0, 0)]);

		var stream = Assert.Single(result.Model.Streams);
		Assert.Equal(16u, stream.VertexStride);
		Assert.Contains(stream.Components, component => component.Type == 5 && component.Index == 0 && component.FormatName == "rgba_r8g8b8a8");
		Assert.Equal(2ul, stream.NumComponents);
		Assert.Equal(0x123456789abcdef0ul, BitConverter.ToUInt64(result.WriteResult.TocData, 128));
		Assert.Equal(16u, BitConverter.ToUInt32(result.WriteResult.TocData, 128 + 8 + 320 + 28));
		Assert.Equal(2ul, BitConverter.ToUInt64(result.WriteResult.TocData, 128 + 8 + 320));
	}

	[Fact]
	public void Reconstruct_DefaultsToCurrentTargetStreamLayout()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0x123456789abcdef0, 2, 0, 3, 16, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(5, "color", 4, "rgba_r8g8b8a8", 0, 0, 4)
			])]
		};
		var targetModel = CreateModel(vertexSeed: 1, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0xabcdef0123456789, 1, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12)
			])]
		};
		var target = CreateTargetUnit(targetModel, CreateToc(materialBindingCount: 1));

		var result = new SdkStyleTargetShellUnitReconstructor().Reconstruct(target, [CreatePatchUnit(SourceKey, sourceModel)], [new TargetShellMeshMapping(SourceKey, 0, 0)]);

		var stream = Assert.Single(result.Model.Streams);
		Assert.Equal(0xabcdef0123456789ul, stream.ComponentInfoId);
		Assert.Equal(12u, stream.VertexStride);
		Assert.Single(stream.Components);
	}

	[Fact]
	public void StreamPlanner_UsesSourceUvPrecisionAndDoesNotInventBoneIndexGroups()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0xbbbb, 3, 0, 3, 24, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(4, "uv", 1, "vec2_float", 0, 0, 8),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			])]
		};
		var targetModel = CreateModel(vertexSeed: 1, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0xaaaa, 5, 0, 3, 44, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(4, "uv", 33, "vec2_half", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 1, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 24, "vec4_uint32", 2, 0, 16)
			])]
		};

		var result = new SdkStyleVertexStreamPlanner().Plan(targetModel, [new SdkStyleStreamReplacement(0, sourceModel, 0)]);
		var stream = Assert.Single(result.Streams);

		Assert.Equal(0xbbbbul, stream.ComponentInfoId);
		Assert.Equal(24u, stream.VertexStride);
		Assert.Contains(stream.Components, component => component.Type == 4 && component.Index == 0 && component.FormatName == "vec2_float");
		var boneIndex = Assert.Single(stream.Components, component => component.Type == 6);
		Assert.Equal(0u, boneIndex.Index);
		Assert.Equal(new uint[] { 0, 4, 6 }, stream.Components.Select(component => component.Type));
	}

	[Fact]
	public void CanonicalSkinningPlanner_UsesCurrentVersionVec4HalfFormat()
	{
		var targetModel = CreateModel(vertexSeed: 1, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 5, 0, 3, 44, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(1, "normal", 30, "unk_normal", 0, 0, 4),
				new UnitStreamComponentInfo(4, "uv", 33, "vec2_half", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 24, "vec4_uint32", 1, 0, 16)
			])]
		};

		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1);
		var result = new SdkStyleVertexStreamPlanner().PlanCanonicalSkinning(targetModel, [new SdkStyleStreamReplacement(0, sourceModel, 0)]);
		var stream = Assert.Single(result.Streams);

		var weights = Assert.Single(stream.Components, component => component.Type == 7);
		Assert.Equal(35u, weights.Format);
		Assert.Equal("vec4_half", weights.FormatName);
		Assert.Equal(8u, weights.Size);
		Assert.Equal(36u, stream.VertexStride);
		Assert.Contains(stream.Components, component => component.Type == 4 && component.Format == 1 && component.FormatName == "vec2_float");
		Assert.Single(stream.Components, component => component.Type == 6);
	}

	[Fact]
	public void CanonicalSkinningPlanner_UsesSdkStyleFallbackWhenSourceAddsVertexColor()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 4, 0, 3, 32, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(5, "color", 4, "rgba_r8g8b8a8", 0, 0, 4),
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(4, "uv", 1, "vec2_float", 0, 0, 8),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			])]
		};
		var targetModel = sourceModel with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 3, 0, 3, 24, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(4, "uv", 1, "vec2_float", 0, 0, 8),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			])]
		};

		var stream = Assert.Single(new SdkStyleVertexStreamPlanner().PlanCanonicalSkinning(targetModel, [new SdkStyleStreamReplacement(0, sourceModel, 0)]).Streams);

		Assert.Contains(stream.Components, component => component.Type == 5 && component.Format == 4);
		Assert.Contains(stream.Components, component => component.Type == 7 && component.Format == 35);
		Assert.Contains(stream.Components, component => component.Type == 6 && component.Format == 28);
		Assert.Equal(36u, stream.VertexStride);
	}

	[Fact]
	public void CanonicalSkinningPlanner_UsesFourInfluenceRouteForThreeInfluenceSource()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			RawMeshData = [CreateModel(vertexSeed: 7, meshCount: 1).RawMeshData[0] with
			{
				Vertices = Enumerable.Range(0, 3).Select(index => new UnitRawVertexRecord((uint)index, Array.Empty<byte>(),
				[
					new UnitVertexComponentValue(7, "bone_weight", 35, "vec4_half", 0, [.5f, .3f, .2f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, Array.Empty<float>(), [0u, 1u, 2u, 0u], Array.Empty<byte>())
				])).ToArray()
			}]
		};
		var targetModel = sourceModel with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 4, 0, 3, 28, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(7, "bone_weight", 33, "vec2_half", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 1, 0, 0)
			])]
		};

		var result = new SdkStyleVertexStreamPlanner().PlanCanonicalSkinning(targetModel, [new SdkStyleStreamReplacement(0, sourceModel, 0)]);
		var weights = Assert.Single(Assert.Single(result.Streams).Components, component => component.Type == 7);

		Assert.Equal("vec4_half", weights.FormatName);
		Assert.Equal(8u, weights.Size);
	}

	[Fact]
	public void CanonicalSkinningReconstructor_ReencodesLegacyTargetSkinningFormats()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1);
		var targetModel = CreateModel(vertexSeed: 1, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 4, 0, 3, 32, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(7, "bone_weight", 999, "unknown", 0, 0, 8),
				new UnitStreamComponentInfo(6, "bone_index", 998, "unknown", 0, 0, 4)
			])]
		};
		var target = CreateTargetUnit(targetModel);

		var result = new SdkStyleTargetShellUnitReconstructor(planCanonicalSkinningLayout: true).Reconstruct(
			target, [CreatePatchUnit(SourceKey, sourceModel)], [new TargetShellMeshMapping(SourceKey, 0, 0)]);

		var stream = Assert.Single(result.Model.Streams);
		Assert.Contains(stream.Components, component => component.Type == 7 && component.FormatName == "vec4_half");
		Assert.Contains(stream.Components, component => component.Type == 6 && component.FormatName == "vec4_uint8");
	}

	[Fact]
	public void CanonicalSkinningReconstructor_ReencodesLegacyMinifyOnlyStreams()
	{
		var targetModel = CreateModel(vertexSeed: 1, meshCount: 1) with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 3, 0, 3, 20, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(7, "bone_weight", 33, "vec2_half", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			])]
		};

		var result = new SdkStyleTargetShellUnitReconstructor(planCanonicalSkinningLayout: true).Reconstruct(CreateTargetUnit(targetModel), Array.Empty<PatchUnitMesh>(), Array.Empty<TargetShellMeshMapping>());
		var stream = Assert.Single(result.Model.Streams);

		Assert.Contains(stream.Components, component => component.Type == 7 && component.Format == 35 && component.Size == 8);
		Assert.Contains(stream.Components, component => component.Type == 6 && component.Format == 28 && component.Size == 4);
		Assert.Equal(stream.Components.Sum(component => component.Size), stream.VertexStride);
		Assert.All(Assert.Single(result.Model.RawMeshData).Vertices, vertex => Assert.Equal((int)stream.VertexStride, vertex.Data.Length));
	}

	[Fact]
	public void TargetBakeDryRun_DoesNotRejectThreeInfluenceSourceBecauseTargetHasTwoWeights()
	{
		var sourceModel = CreateModel(vertexSeed: 7, meshCount: 1) with
		{
			Streams = [CreateSkinnedStream()],
			TransformNameHashes = [101, 102, 103],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [IdentityMatrix(), IdentityMatrix(), IdentityMatrix()], Array.Empty<UnitTransformEntry>(), [101, 102, 103]),
			BoneInfos = [new UnitBoneInfo(0, 0, 0, 0, 0, 0, [0, 1, 2], [new UnitBoneRemap(0, 0, [0, 1, 2])])],
			RawMeshData = [CreateSkinnedRawMesh() with
			{
				Vertices = Enumerable.Range(0, 3).Select(index => new UnitRawVertexRecord((uint)index, Array.Empty<byte>(),
				[
					new UnitVertexComponentValue(7, "bone_weight", 35, "vec4_half", 0, [.5f, .3f, .2f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, Array.Empty<float>(), [0u, 1u, 2u, 0u], Array.Empty<byte>())
				])).ToArray()
			}]
		};
		var targetModel = sourceModel with
		{
			Streams = [new UnitStreamInfo(0, 128, 0, 3, 0, 3, 20, 0, 3, 0, 0, 0, 0, 0,
			[
				new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
				new UnitStreamComponentInfo(7, "bone_weight", 33, "vec2_half", 0, 0, 4),
				new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
			])]
		};
		var avatar = new SdkStyleAvatarRigResource(SourceKey, "avatar", new PatchEntryPayload(new PatchTocEntry(SourceKey, "avatar", "avatar"), Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>()), 0, 0, sourceModel.TransformInfo);

		var diagnostic = new TargetBakeDryRunAnalyzer().Analyze(targetModel, 0, sourceModel, 0, avatar);

		Assert.Equal("TargetBakeDryRunReady", diagnostic.Status);
	}

	private static PatchUnitMesh CreatePatchUnit(AssetKey key, UnitMeshModel model)
	{
		var entry = new PatchTocEntry(key, "source.patch", "source.patch");
		return new PatchUnitMesh(entry, new PatchEntryPayload(entry, CreateToc(), Array.Empty<byte>(), Array.Empty<byte>()), model);
	}

	private static GameDataUnitMesh CreateTargetUnit(UnitMeshModel model, byte[]? tocData = null)
	{
		var entry = new PatchTocEntry(TargetKey, "target", "target");
		return new GameDataUnitMesh(TargetKey, "target", new PatchEntryPayload(entry, tocData ?? CreateToc(model.Materials.Count), Array.Empty<byte>(), Array.Empty<byte>()), model);
	}

	private static UnitMeshModel CreateModel(byte vertexSeed, int meshCount)
	{
		var stream = new UnitStreamInfo(0, 128, 0, 1, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, new[] { new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12) });
		var meshes = Enumerable.Range(0, meshCount).Select(index => new UnitMeshInfo(index, (uint)(500 + index * 100), (uint)(100 + index), 0, 0, 0, 1, 0, 1, (uint)(650 + index * 100), UnitMeshSemanticInfo.Empty(0, index), new uint[] { (uint)(20 + index) }, new[] { new UnitMeshSectionInfo((uint)(650 + index * 100), 0, (uint)(20 + index), 0, 3, 0, 3, 0) })).ToArray();
		var rawMeshes = Enumerable.Range(0, meshCount).Select(index => new UnitRawMeshData(index, (uint)(100 + index), 0, 0, new[] { new UnitRawMeshSectionData(0, (uint)(20 + index), new[] { new UnitTriangleIndices(0, 1, 2) }) }, new[] { new UnitTriangleIndices(0, 1, 2) }, Enumerable.Range(0, 3).Select(vertex => new UnitRawVertexRecord((uint)vertex, new[] { (byte)(vertexSeed + vertex), (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0, (byte)0 }, Array.Empty<UnitVertexComponentValue>())).ToArray())).ToArray();
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 496, 800, 900, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, meshes, meshes.Select(mesh => new UnitMaterialBinding(mesh.MaterialSlotIds[0], 0x100)).ToArray(), Array.Empty<UnitRawMeshSummary>(), rawMeshes)
		{
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [new UnitTransformMatrix([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1])], Array.Empty<UnitTransformEntry>(), Array.Empty<uint>())
		};
	}

	private static UnitStreamInfo CreateSkinnedStream()
		=> new(0, 128, 0, 3, 0, 3, 28, 0, 3, 0, 0, 0, 0, 0,
		[
			new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
			new UnitStreamComponentInfo(7, "bone_weight", 33, "vec4_half", 0, 0, 8),
			new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
		]);

	private static UnitRawMeshData CreateSkinnedRawMesh()
		=> new(0, 100, 0, 0,
			[new UnitRawMeshSectionData(0, 20, [new UnitTriangleIndices(0, 1, 2)])],
			[new UnitTriangleIndices(0, 1, 2)],
			Enumerable.Range(0, 3).Select(index => new UnitRawVertexRecord(
				(uint)index,
				Array.Empty<byte>(),
				[
					new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, [0f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(7, "bone_weight", 33, "vec4_half", 0, [1f, 0f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
					new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, Array.Empty<float>(), [0u, 0u, 0u, 0u], Array.Empty<byte>())
				])).ToArray());

	private static UnitTransformMatrix IdentityMatrix()
		=> new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

	private static UnitMeshModel CreateSurfaceVectorModel(float meshTransformX)
	{
		var stream = new UnitStreamInfo(0, 128, 0, 6, 0, 1, 60, 0, 3, 0, 0, 0, 0, 0,
		[
			new UnitStreamComponentInfo(0, "position", 2, "vec3_float", 0, 0, 12),
			new UnitStreamComponentInfo(1, "normal", 2, "vec3_float", 0, 0, 12),
			new UnitStreamComponentInfo(2, "tangent", 2, "vec3_float", 0, 0, 12),
			new UnitStreamComponentInfo(3, "bitangent", 2, "vec3_float", 0, 0, 12),
			new UnitStreamComponentInfo(7, "bone_weight", 33, "vec4_half", 0, 0, 8),
			new UnitStreamComponentInfo(6, "bone_index", 28, "vec4_uint8", 0, 0, 4)
		]);
		var mesh = new UnitMeshInfo(0, 500, 100, 0, 0, 0, 1, 0, 1, 650, UnitMeshSemanticInfo.Empty(0, 0), [20], [new UnitMeshSectionInfo(650, 0, 20, 0, 3, 0, 3, 0)]);
		var vertex = new UnitRawVertexRecord(0, Array.Empty<byte>(),
		[
			new UnitVertexComponentValue(0, "position", 2, "vec3_float", 0, [1f, 2f, 3f], Array.Empty<uint>(), Array.Empty<byte>()),
			new UnitVertexComponentValue(1, "normal", 2, "vec3_float", 0, [0f, 1f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
			new UnitVertexComponentValue(2, "tangent", 2, "vec3_float", 0, [1f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
			new UnitVertexComponentValue(3, "bitangent", 2, "vec3_float", 0, [0f, 0f, 1f], Array.Empty<uint>(), Array.Empty<byte>()),
			new UnitVertexComponentValue(7, "bone_weight", 33, "vec4_half", 0, [1f, 0f, 0f, 0f], Array.Empty<uint>(), Array.Empty<byte>()),
			new UnitVertexComponentValue(6, "bone_index", 28, "vec4_uint8", 0, Array.Empty<float>(), [0u, 0u, 0u, 0u], Array.Empty<byte>())
		]);
		return new UnitMeshModel(0, 0, 0, 0, 0, 0, 0, 496, 800, 900, UnitCustomizationInfo.Empty, [new UnitBoneInfo(0, 0, 1, 0, 0, 0, [0], [new UnitBoneRemap(0, 0, [0])])], [stream], [mesh], [new UnitMaterialBinding(20, 0x100)], Array.Empty<UnitRawMeshSummary>(), [new UnitRawMeshData(0, 100, 0, 0, [new UnitRawMeshSectionData(0, 20, [new UnitTriangleIndices(0, 0, 0)])], [new UnitTriangleIndices(0, 0, 0)], [vertex])])
		{
			TransformNameHashes = [101],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(), [new UnitTransformMatrix([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, meshTransformX, 0, 0, 1])], Array.Empty<UnitTransformEntry>(), [101])
		};
	}

	private static UnitMeshModel CreateWeightedBindPoseModel(float firstBoneX, float secondBoneX)
	{
		var model = CreateSurfaceVectorModel(0f);
		var vertex = model.RawMeshData[0].Vertices[0] with
		{
			Components = model.RawMeshData[0].Vertices[0].Components.Select(component => component.Type switch
			{
				7 => component with { FloatValues = [.5f, .5f, 0f, 0f] },
				6 => component with { UIntValues = [0u, 1u, 0u, 0u] },
				_ => component
			}).ToArray()
		};
		return model with
		{
			Meshes = model.Meshes.Select(mesh => mesh with { TransformIndex = 2 }).ToArray(),
			TransformNameHashes = [101, 102],
			TransformInfo = new UnitTransformInfo(0, 0, 0, Array.Empty<UnitLocalTransform>(),
			[
				TranslationMatrix(firstBoneX),
				TranslationMatrix(secondBoneX),
				IdentityMatrix()
			], Array.Empty<UnitTransformEntry>(), [101, 102]),
			BoneInfos = [new UnitBoneInfo(0, 0, 2, 0, 0, 0, [0, 1], [new UnitBoneRemap(0, 0, [0, 1])])],
			RawMeshData = [model.RawMeshData[0] with { Vertices = [vertex] }]
		};
	}

	private static UnitTransformMatrix TranslationMatrix(float x)
		=> new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, 0, 0, 1]);

	private static float[] ReadVector3(byte[] data, int offset)
		=> [BitConverter.ToSingle(data, offset), BitConverter.ToSingle(data, offset + 4), BitConverter.ToSingle(data, offset + 8)];

	private static byte[] CreateToc(int materialBindingCount = 2)
	{
		var data = new byte[1200];
		WriteUInt32(data, 0x60, 900); WriteUInt32(data, 0x70, 800); WriteUInt32(data, 496, 4); WriteUInt32(data, 604, 2); WriteUInt32(data, 620, 1);
		for (var index = 0; index < 2; index++) { var offset = 500 + index * 100; WriteUInt32(data, offset + 104, 1); WriteUInt32(data, offset + 108, 128); WriteUInt32(data, offset + 120, 1); WriteUInt32(data, offset + 124, 150); WriteUInt32(data, 650 + index * 100, 0); WriteUInt32(data, 654 + index * 100, 0); WriteUInt32(data, 658 + index * 100, 3); WriteUInt32(data, 662 + index * 100, 0); WriteUInt32(data, 666 + index * 100, 3); }
		WriteUInt32(data, 800, (uint)materialBindingCount); WriteUInt32(data, 804, 20); WriteUInt32(data, 808, 21); return data;
	}

	private static void WriteUInt32(byte[] data, int offset, uint value) { data[offset] = (byte)value; data[offset + 1] = (byte)(value >> 8); data[offset + 2] = (byte)(value >> 16); data[offset + 3] = (byte)(value >> 24); }
}