using HD2ModAdaptation.PatchReconstruction;
using HD2ModAdaptation.PatchReconstruction.UnitMesh;
using HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;
using Xunit;

namespace HD2ModAdaptation.Tests;

// Purpose: Verifies the SDK-style reconstruction path stays separate from legacy source-to-target bone remapping.
public sealed class SdkStyleUnitReconstructionPipelineTests
{
	private static readonly AssetKey SourceUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x1111);
	private static readonly AssetKey TargetUnitKey = new(PatchUnitMeshReader.UnitTypeId, 0x2222);

	[Fact]
	public void CreatePlan_BindsTargetAvatarAndExplicitMeshes()
	{
		var request = new SdkStyleReconstructionRequest(
			"source.patch_0",
			"target_archive",
			TargetUnitKey,
			new[] { SdkStyleAvatarRigConstants.AvatarArchiveName },
			new[] { new TargetShellMeshMapping(SourceUnitKey, 0, 0) });
		var source = CreatePatchUnit(SourceUnitKey, CreateModel(10, 0x100, lodIndex: 0, bonesRef: 0x500));
		var target = CreateGameUnit(TargetUnitKey, "target_archive", CreateModel(20, 0x200, lodIndex: 1, bonesRef: 0x600), stateMachineRef: 0x700);
		var avatar = CreateAvatarRig(SdkStyleAvatarRigConstants.AvatarUnitAssetKey, SdkStyleAvatarRigConstants.AvatarArchiveName, bonesRef: 0x800, stateMachineRef: 0x900);

		var plan = new SdkStyleUnitReconstructionPipeline().CreatePlan(request, target, avatar, new[] { source });

		var binding = Assert.Single(plan.MeshBindings);
		Assert.Equal(source, binding.SourceUnit);
		Assert.Equal(0, binding.SourceMeshInfoIndex);
		Assert.Equal(0, binding.TargetMeshInfoIndex);
		Assert.Equal(1, binding.TargetBoneInfoIndex);
		Assert.Equal(new uint[] { 20 }, binding.TargetMaterialSlotIds);
		Assert.Equal(TargetUnitKey, plan.Resources.TargetUnitAssetKey);
		Assert.Equal(SdkStyleAvatarRigConstants.AvatarUnitAssetKey, plan.Resources.AvatarUnitAssetKey);
		Assert.Equal(0x800ul, plan.Resources.AvatarBonesReference);
		Assert.Equal(0x900ul, plan.Resources.AvatarStateMachineReference);
		Assert.Equal(SdkStyleAvatarRigConstants.AvatarRigObjectName, plan.Resources.AvatarRigObjectName);
	}

	[Fact]
	public void CreatePlan_RejectsDuplicateTargetMeshMappings()
	{
		var request = new SdkStyleReconstructionRequest(
			"source.patch_0",
			"target_archive",
			TargetUnitKey,
			Array.Empty<string>(),
			new[]
			{
				new TargetShellMeshMapping(SourceUnitKey, 0, 0),
				new TargetShellMeshMapping(SourceUnitKey, 0, 0)
			});
		var source = CreatePatchUnit(SourceUnitKey, CreateModel(10, 0x100));
		var target = CreateGameUnit(TargetUnitKey, "target_archive", CreateModel(20, 0x200), stateMachineRef: 0x700);
		var avatar = CreateAvatarRig(SdkStyleAvatarRigConstants.AvatarUnitAssetKey, SdkStyleAvatarRigConstants.AvatarArchiveName, bonesRef: 0x800, stateMachineRef: 0x900);

		Assert.Throws<InvalidDataException>(() => new SdkStyleUnitReconstructionPipeline().CreatePlan(request, target, avatar, new[] { source }));
	}

	[Fact]
	public void CreatePlan_RejectsMismatchedAvatarUnit()
	{
		var request = new SdkStyleReconstructionRequest(
			"source.patch_0",
			"target_archive",
			TargetUnitKey,
			Array.Empty<string>(),
			new[] { new TargetShellMeshMapping(SourceUnitKey, 0, 0) });
		var source = CreatePatchUnit(SourceUnitKey, CreateModel(10, 0x100));
		var target = CreateGameUnit(TargetUnitKey, "target_archive", CreateModel(20, 0x200), stateMachineRef: 0x700);
		var wrongAvatar = CreateAvatarRig(new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x3333), SdkStyleAvatarRigConstants.AvatarArchiveName, bonesRef: 0x800, stateMachineRef: 0x900);

		Assert.Throws<InvalidDataException>(() => new SdkStyleUnitReconstructionPipeline().CreatePlan(request, target, wrongAvatar, new[] { source }));
	}

	[Fact]
	public void SdkStyleUnitOutput_CanRecordMultipleTargetUnits()
	{
		var secondTargetKey = new AssetKey(PatchUnitMeshReader.UnitTypeId, 0x3333);
		var output = new SdkStyleUnitOutput(
			TargetUnitKey,
			SdkStyleAvatarRigConstants.AvatarUnitAssetKey,
			Array.Empty<PatchArchiveAdditionalEntry>(),
			new[] { SourceUnitKey },
			Array.Empty<ulong>())
		{
			TargetUnitAssetKeys = new[] { TargetUnitKey, secondTargetKey }
		};

		Assert.Equal(new[] { TargetUnitKey, secondTargetKey }, output.TargetUnitAssetKeys);
		Assert.Equal(TargetUnitKey, output.TargetUnitAssetKey);
	}

	private static PatchUnitMesh CreatePatchUnit(AssetKey key, UnitMeshModel model)
	{
		var payload = new PatchEntryPayload(CreateEntry(key), CreateWritableTocData(), Array.Empty<byte>(), Array.Empty<byte>());
		return new PatchUnitMesh(payload.Entry, payload, model, null);
	}

	private static GameDataUnitMesh CreateGameUnit(AssetKey key, string archiveName, UnitMeshModel model, ulong stateMachineRef)
	{
		var payload = new PatchEntryPayload(CreateEntry(key), CreateWritableTocData(stateMachineRef), Array.Empty<byte>(), Array.Empty<byte>());
		return new GameDataUnitMesh(key, archiveName, payload, model, null);
	}

	private static SdkStyleAvatarRigResource CreateAvatarRig(AssetKey key, string archiveName, ulong bonesRef, ulong stateMachineRef)
	{
		var payload = new PatchEntryPayload(CreateEntry(key), CreateWritableTocData(stateMachineRef, bonesRef), Array.Empty<byte>(), Array.Empty<byte>());
		return new SdkStyleAvatarRigResource(key, archiveName, payload, bonesRef, stateMachineRef);
	}

	private static PatchTocEntry CreateEntry(AssetKey key) => new(key, "source.patch", "source.patch");

	private static UnitMeshModel CreateModel(uint materialSlot, ulong materialId, int lodIndex = 0, ulong bonesRef = 0)
	{
		var component = new UnitStreamComponentInfo(0, "position", 0, "vec3_float", 0, 0, 12);
		var stream = new UnitStreamInfo(0, 128, 0, 1, 0, 3, 12, 0, 3, 0, 0, 0, 0, 0, new[] { component });
		var sectionInfo = new UnitMeshSectionInfo(300, 0, materialSlot, 0, 3, 0, 3, 0);
		var meshInfo = new UnitMeshInfo(0, 500, 1, lodIndex, 0, 0, 1, 0, 1, 650, UnitMeshSemanticInfo.Empty(lodIndex, 0), new[] { materialSlot }, new[] { sectionInfo });
		var vertices = Enumerable.Range(0, 3)
			.Select(index => new UnitRawVertexRecord((uint)index, new byte[12], Array.Empty<UnitVertexComponentValue>()))
			.ToArray();
		var section = new UnitRawMeshSectionData(0, materialSlot, new[] { new UnitTriangleIndices(0, 1, 2) });
		var rawMesh = new UnitRawMeshData(0, 1, lodIndex, 0, new[] { section }, section.Triangles, vertices);
		return new UnitMeshModel(0, 0, bonesRef, 0, 0, 0, 0, 496, 800, 900, UnitCustomizationInfo.Empty, Array.Empty<UnitBoneInfo>(), new[] { stream }, new[] { meshInfo }, new[] { new UnitMaterialBinding(materialSlot, materialId) }, Array.Empty<UnitRawMeshSummary>(), new[] { rawMesh });
	}

	private static byte[] CreateWritableTocData(ulong stateMachineRef = 0, ulong bonesRef = 0)
	{
		var data = new byte[1200];
		WriteUInt64(data, 8, bonesRef);
		WriteUInt64(data, 32, stateMachineRef);
		return data;
	}

	private static void WriteUInt64(byte[] data, int offset, ulong value)
	{
		for (var i = 0; i < sizeof(ulong); i++)
		{
			data[offset + i] = (byte)(value >> (i * 8));
		}
	}
}