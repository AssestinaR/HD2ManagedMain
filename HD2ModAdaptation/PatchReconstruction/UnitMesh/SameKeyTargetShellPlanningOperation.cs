namespace HD2ModAdaptation.PatchReconstruction.UnitMesh;

// Purpose: Produces a no-write, same-key target-shell mesh plan from explicit source and selected current target Unit payloads.
public sealed class SameKeyTargetShellPlanningOperation
{
	private readonly UnitMeshReplacementStrategy replacementStrategy;

	public SameKeyTargetShellPlanningOperation(UnitMeshReplacementStrategy? replacementStrategy = null)
	{
		this.replacementStrategy = replacementStrategy ?? new UnitMeshReplacementStrategy(allowExperimentalFallback: true);
	}

	public SameKeyTargetShellMeshPlan CreatePlan(
		PatchUnitMesh sourceUnit,
		GameDataUnitMesh targetUnit,
		int? sourceMeshInfoIndex = null)
	{
		ArgumentNullException.ThrowIfNull(sourceUnit);
		ArgumentNullException.ThrowIfNull(targetUnit);
		if (sourceUnit.Entry.AssetKey != targetUnit.AssetKey)
		{
			throw new InvalidDataException("Same-key planning requires source and target Units with the identical AssetKey.");
		}
		if (sourceMeshInfoIndex is < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index cannot be negative.");
		}
		if (sourceMeshInfoIndex.HasValue && !sourceUnit.Model.RawMeshData.Any(mesh => mesh.MeshInfoIndex == sourceMeshInfoIndex.Value))
		{
			throw new ArgumentOutOfRangeException(nameof(sourceMeshInfoIndex), "Source mesh info index does not exist in the source Unit.");
		}

		var candidates = replacementStrategy.FindCandidates(targetUnit.Model, sourceUnit.Model)
			.Where(candidate => !sourceMeshInfoIndex.HasValue || candidate.SourceMeshInfoIndex == sourceMeshInfoIndex.Value)
			.ToArray();
		var selected = UnitMeshReplacementStrategy.SelectNonConflictingCandidates(candidates);
		var mappings = selected
			.Select(candidate => new TargetShellMeshMapping(sourceUnit.Entry.AssetKey, candidate.SourceMeshInfoIndex, candidate.TargetMeshInfoIndex))
			.ToArray();
		var replacedTargetIndexes = mappings.Select(mapping => mapping.TargetMeshInfoIndex).ToHashSet();
		var minifiedTargetIndexes = targetUnit.Model.RawMeshData
			.Select(mesh => mesh.MeshInfoIndex)
			.Where(index => !replacedTargetIndexes.Contains(index))
			.OrderBy(index => index)
			.ToArray();

		return new SameKeyTargetShellMeshPlan(
			sourceUnit.Entry.AssetKey,
			targetUnit.ArchiveName,
			candidates,
			mappings,
			minifiedTargetIndexes,
			BuildReason(mappings.Length, minifiedTargetIndexes.Length, candidates.Length));
	}

	private static string BuildReason(int replacementCount, int minifiedCount, int candidateCount)
		=> replacementCount == 0
			? $"Same-key planning produced a minify-only target shell with {minifiedCount} minified target slot(s) because no source replacement candidates were found."
			: $"Same-key planning produced {replacementCount} replacement(s) and {minifiedCount} minified target slot(s) from {candidateCount} candidate(s).";
}

public sealed record SameKeyTargetShellMeshPlan(
	AssetKey UnitAssetKey,
	string TargetArchiveName,
	IReadOnlyList<UnitMeshReplacementCandidate> Candidates,
	IReadOnlyList<TargetShellMeshMapping> MeshMappings,
	IReadOnlyList<int> MinifiedTargetMeshInfoIndexes,
	string Reason)
{
	public int ReplacementCount => MeshMappings.Count;
	public bool HasExperimentalCandidate => Candidates.Any(candidate => candidate.Kind == UnitMeshReplacementCandidateKind.ExperimentalFallback);
	public bool HasFullTargetShellCoverage => MeshMappings.Count + MinifiedTargetMeshInfoIndexes.Count > 0;
}