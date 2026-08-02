namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.Canonical;

// Purpose: Defines explicit source-to-target Unit/MeshInfoIndex mappings for the canonical replacement boundary.
// SDK reference entry points: CreatePatchFromActive(), GetEntryByLoadArchive(), and AddEntryToPatchID().
public readonly record struct CanonicalMeshKey(AssetKey UnitKey, int MeshInfoIndex)
{
	public bool IsValid => UnitKey != default && MeshInfoIndex >= 0;
}

public enum CanonicalSourceMeshState
{
	Available = 0,
	Empty = 1,
	Unreadable = 2
}

// Purpose: Declares how a source placement obtains its final target-rig skinning semantics.
public enum CanonicalSkinningMode
{
	PreserveSourceWeights = 0,
	BindStaticToTargetMeshTransform = 1,
	BindStaticToAvatarBone = 2,
	HiddenPlaceholder = 3
}

// Purpose: Makes an intentional static-mesh attachment explicit instead of relying on palette index zero.
public sealed record CanonicalBoneAnchor(uint? AvatarBoneHash = null)
{
	public static CanonicalBoneAnchor TargetMeshTransform { get; } = new();
	public bool UsesTargetMeshTransform => AvatarBoneHash is null;
}

public sealed record CanonicalReplacementMapping(
	CanonicalMeshKey Source,
	CanonicalMeshKey Target,
	CanonicalSourceMeshState SourceMeshState = CanonicalSourceMeshState.Available,
	CanonicalSkinningMode SkinningMode = CanonicalSkinningMode.PreserveSourceWeights,
	CanonicalBoneAnchor? BoneAnchor = null);

public sealed record CanonicalPlanDiagnostic(string Code, string Message);

public sealed record CanonicalReplacementPlanValidation(
	CanonicalReplacementPlan? Plan,
	IReadOnlyList<CanonicalPlanDiagnostic> Diagnostics)
{
	public bool IsValid => Plan is not null && Diagnostics.Count == 0;
}

public sealed class CanonicalReplacementPlan
{
	private CanonicalReplacementPlan(IReadOnlyList<CanonicalReplacementMapping> mappings)
	{
		Mappings = mappings;
	}

	public IReadOnlyList<CanonicalReplacementMapping> Mappings { get; }

	public static CanonicalReplacementPlanValidation TryCreate(IEnumerable<CanonicalReplacementMapping> mappings)
	{
		ArgumentNullException.ThrowIfNull(mappings);

		var materialized = mappings.ToArray();
		var diagnostics = new List<CanonicalPlanDiagnostic>();
		var targets = new HashSet<CanonicalMeshKey>();

		foreach (var mapping in materialized)
		{
			if (!mapping.Source.IsValid)
			{
				diagnostics.Add(new("InvalidSourceKey", "Source Unit key and MeshInfoIndex must be explicit and valid."));
			}

			if (mapping.SourceMeshState != CanonicalSourceMeshState.Available)
			{
				diagnostics.Add(new("UnavailableSourceMesh", $"Source {mapping.Source} is {mapping.SourceMeshState} and cannot be used for replacement."));
			}

			if (!mapping.Target.IsValid)
			{
				diagnostics.Add(new("InvalidTargetKey", "Target Unit key and MeshInfoIndex must be explicit and valid."));
			}

			if (!targets.Add(mapping.Target))
			{
				diagnostics.Add(new("DuplicateTargetMapping", $"Target {mapping.Target} is mapped more than once."));
			}
		}

		if (materialized.Length == 0)
		{
			diagnostics.Add(new("EmptyPlan", "At least one explicit replacement mapping is required."));
		}

		return diagnostics.Count == 0
			? new(new CanonicalReplacementPlan(Array.AsReadOnly(materialized)), Array.Empty<CanonicalPlanDiagnostic>())
			: new(null, Array.AsReadOnly(diagnostics.ToArray()));
	}
}

public sealed record CanonicalSourceResourceRequest(CanonicalMeshKey SourceKey)
{
	public bool IsReadOnly => true;
}

public enum CanonicalTargetResourceOrigin
{
	GameData = 0
}

public sealed record CanonicalTargetResourceRequest(
	CanonicalMeshKey TargetKey,
	CanonicalTargetResourceOrigin Origin = CanonicalTargetResourceOrigin.GameData)
{
	public bool IsGameDataRead => Origin == CanonicalTargetResourceOrigin.GameData;
}