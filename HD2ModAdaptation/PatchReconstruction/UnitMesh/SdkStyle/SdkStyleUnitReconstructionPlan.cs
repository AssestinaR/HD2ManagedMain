namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Records the SDK-style reconstruction work order before mesh extraction and Unit serialization are implemented.
public sealed record SdkStyleUnitReconstructionPlan(
	GameDataUnitMesh TargetShell,
	SdkStyleAvatarRigResource AvatarRig,
	IReadOnlyList<SdkStyleMeshBinding> MeshBindings,
	SdkStyleResourcePlan Resources,
	TargetShellDependencyPolicy DependencyPolicy);

public sealed record SdkStyleAvatarRigResource(
	AssetKey AssetKey,
	string ArchiveName,
	PatchEntryPayload Payload,
	ulong BonesReference,
	ulong StateMachineReference,
	UnitTransformInfo TransformInfo);

public sealed record SdkStyleMeshBinding(
	PatchUnitMesh SourceUnit,
	int SourceMeshInfoIndex,
	int TargetMeshInfoIndex,
	int TargetBoneInfoIndex,
	IReadOnlyList<uint> TargetMaterialSlotIds);

public sealed record SdkStyleResourcePlan(
	AssetKey TargetUnitAssetKey,
	AssetKey AvatarUnitAssetKey,
	ulong AvatarBonesReference,
	ulong AvatarStateMachineReference,
	string AvatarRigObjectName,
	string AvatarMeshNamePrefix)
{
	public bool HasAvatarBones => AvatarBonesReference != 0;
	public bool HasAvatarStateMachine => AvatarStateMachineReference != 0;
}