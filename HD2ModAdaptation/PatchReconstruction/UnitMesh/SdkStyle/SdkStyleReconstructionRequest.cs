namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Describes the explicit inputs for the SDK-style Unit reconstruction path without using legacy bone remaps.
public sealed record SdkStyleReconstructionRequest(
	string SourcePatchTocPath,
	string TargetArchiveName,
	AssetKey TargetUnitAssetKey,
	IReadOnlyCollection<string> DependencyArchiveNames,
	IReadOnlyCollection<TargetShellMeshMapping> MeshMappings,
	string AvatarArchiveName = SdkStyleAvatarRigConstants.AvatarArchiveName,
	AssetKey? AvatarUnitAssetKey = null,
	TargetShellDependencyPolicy DependencyPolicy = TargetShellDependencyPolicy.ReferenceCurrentGame)
{
	public AssetKey ResolvedAvatarUnitAssetKey => AvatarUnitAssetKey ?? SdkStyleAvatarRigConstants.AvatarUnitAssetKey;

	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(TargetArchiveName);
		ArgumentException.ThrowIfNullOrWhiteSpace(AvatarArchiveName);
		ArgumentNullException.ThrowIfNull(DependencyArchiveNames);
		ArgumentNullException.ThrowIfNull(MeshMappings);
		if (TargetUnitAssetKey.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException("The requested target asset is not a Unit resource.");
		}
		if (ResolvedAvatarUnitAssetKey.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException("The requested avatar rig asset is not a Unit resource.");
		}
		if (MeshMappings.Count == 0)
		{
			throw new InvalidDataException("At least one explicit source-to-target mesh mapping is required.");
		}
		if (MeshMappings.Any(mapping => mapping.SourceUnitAssetKey.TypeId != PatchUnitMeshReader.UnitTypeId))
		{
			throw new InvalidDataException("Every explicit source mapping must identify a Unit resource.");
		}
		if (DependencyArchiveNames.Any(string.IsNullOrWhiteSpace))
		{
			throw new InvalidDataException("Dependency archive names cannot be blank.");
		}
	}
}