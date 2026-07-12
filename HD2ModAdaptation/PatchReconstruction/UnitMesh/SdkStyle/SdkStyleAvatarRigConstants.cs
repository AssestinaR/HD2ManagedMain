namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Captures the fixed player avatar rig resources used by the SDK/autofix reconstruction path.
public static class SdkStyleAvatarRigConstants
{
	public const string AvatarArchiveName = "18235e0c9ec0e636";
	public const ulong AvatarUnitFileId = 5556372446766824087;
	public const string AvatarRigObjectName = "5556372446766824087_rig";
	public const string AvatarMeshNamePrefix = "5556372446766824087_lod";

	public static AssetKey AvatarUnitAssetKey { get; } = new(PatchUnitMeshReader.UnitTypeId, AvatarUnitFileId);
}