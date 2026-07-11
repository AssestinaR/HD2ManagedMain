namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Persists all user-selected inputs needed to reconstruct one current target Unit without candidate guessing.
public sealed record TargetShellReconstructionRequest(
	string SourcePatchTocPath,
	string TargetArchiveName,
	AssetKey TargetUnitAssetKey,
	IReadOnlyCollection<string> DependencyArchiveNames,
	IReadOnlyCollection<TargetShellMeshMapping> MeshMappings,
	TargetShellDependencyPolicy DependencyPolicy = TargetShellDependencyPolicy.ReferenceCurrentGame)
{
	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePatchTocPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(TargetArchiveName);
		ArgumentNullException.ThrowIfNull(DependencyArchiveNames);
		ArgumentNullException.ThrowIfNull(MeshMappings);
		if (TargetUnitAssetKey.TypeId != PatchUnitMeshReader.UnitTypeId)
		{
			throw new InvalidDataException("The requested target asset is not a Unit resource.");
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