namespace HD2ModAdaptation.PatchReconstruction.UnitMesh.SdkStyle;

// Purpose: Converts explicit current target-shell reconstruction work items into patch additions without selecting archives or making material-provider decisions.
public sealed class SdkStyleTargetShellPatchOutputBuilder
{
	private readonly SdkStyleTargetShellUnitReconstructor unitReconstructor;

	public SdkStyleTargetShellPatchOutputBuilder(SdkStyleTargetShellUnitReconstructor? unitReconstructor = null)
	{
		this.unitReconstructor = unitReconstructor ?? new SdkStyleTargetShellUnitReconstructor();
	}

	public static SdkStyleTargetShellPatchOutputBuilder CreateWithSectionRebuild(
		bool propagateSourceMaterials = false,
		IReadOnlySet<ulong>? allowedSourceMaterialIds = null)
		=> new(new SdkStyleTargetShellUnitReconstructor(
			writer: new UnitMeshWriter(allowBoneInfoRelocation: true, allowTransformInfoRelocation: true),
			allowSectionRebuild: true,
			propagateSourceMaterials: propagateSourceMaterials,
			allowedSourceMaterialIds: allowedSourceMaterialIds));

	public SdkStyleTargetShellPatchOutput Build(IReadOnlyCollection<SdkStyleTargetShellPatchWorkItem> workItems)
	{
		ArgumentNullException.ThrowIfNull(workItems);
		if (workItems.Count == 0) throw new InvalidDataException("At least one current target Unit work item is required.");

		var targetKeys = new HashSet<AssetKey>();
		var additions = new List<PatchArchiveAdditionalEntry>();
		var results = new List<SdkStyleTargetShellPatchUnitResult>();
		var replacedSourceUnits = new HashSet<AssetKey>();
		foreach (var item in workItems.OrderBy(item => item.TargetUnit.AssetKey.FileId))
		{
			if (!targetKeys.Add(item.TargetUnit.AssetKey)) throw new InvalidDataException($"Target Unit 0x{item.TargetUnit.AssetKey.FileId:x16} occurs more than once.");
			var result = unitReconstructor.Reconstruct(item.TargetUnit, item.SourceUnits, item.MeshMappings);
			additions.Add(CreateAdditionalEntry(item.TargetUnit.Payload.Entry, result.WriteResult.TocData, Array.Empty<byte>(), result.WriteResult.GpuData));
			if (item.DependencyPolicy == TargetShellDependencyPolicy.EmbedReferencedComposite && result.WriteResult.CompositeTocData is not null && item.TargetUnit.CompositePayload is not null)
			{
				additions.Add(CreateAdditionalEntry(item.TargetUnit.CompositePayload.Entry, result.WriteResult.CompositeTocData, Array.Empty<byte>(), result.WriteResult.CompositeGpuData ?? Array.Empty<byte>()));
			}
			else if (item.DependencyPolicy == TargetShellDependencyPolicy.EmbedReferencedComposite && item.TargetUnit.CompositePayload is null)
			{
				throw new InvalidDataException($"Target Unit 0x{item.TargetUnit.AssetKey.FileId:x16} has no Composite payload to embed.");
			}

			// A minify-only target shell also replaces its obsolete source Unit: preserving
			// that old Unit would let the game resolve legacy data alongside this shell.
			foreach (var sourceUnit in item.SourceUnits) replacedSourceUnits.Add(sourceUnit.Entry.AssetKey);
			results.Add(new SdkStyleTargetShellPatchUnitResult(item.TargetUnit.AssetKey, result.Replacements.Count, result.MinifiedTargetMeshInfoIndexes.Count, result.CoveredTargetMeshCount, result.RebuiltBoneInfoIndexes, result.Model.BoneInfos, result.Model.Materials, result.ReplacementMaterialIds));
		}
		return new SdkStyleTargetShellPatchOutput(additions, replacedSourceUnits.OrderBy(key => key.FileId).ToArray(), results);
	}

	private static PatchArchiveAdditionalEntry CreateAdditionalEntry(PatchTocEntry entry, byte[] tocData, byte[] streamData, byte[] gpuData)
		=> new(entry.AssetKey, tocData, streamData, gpuData, entry.Unknown1, entry.Unknown2, entry.Unknown3, entry.Unknown4);
}

public sealed record SdkStyleTargetShellPatchWorkItem(
	GameDataUnitMesh TargetUnit,
	IReadOnlyCollection<PatchUnitMesh> SourceUnits,
	IReadOnlyCollection<TargetShellMeshMapping> MeshMappings,
	TargetShellDependencyPolicy DependencyPolicy = TargetShellDependencyPolicy.ReferenceCurrentGame);

public sealed record SdkStyleTargetShellPatchOutput(
	IReadOnlyList<PatchArchiveAdditionalEntry> AdditionalEntries,
	IReadOnlyList<AssetKey> ReplacedSourceUnitAssetKeys,
	IReadOnlyList<SdkStyleTargetShellPatchUnitResult> UnitResults);

public sealed record SdkStyleTargetShellPatchUnitResult(
	AssetKey TargetUnitAssetKey,
	int ReplacementCount,
	int MinifiedCount,
	int CoveredTargetMeshCount,
	IReadOnlyList<int> RebuiltBoneInfoIndexes,
	IReadOnlyList<UnitBoneInfo> BoneInfos,
	IReadOnlyList<UnitMaterialBinding> MaterialBindings,
	IReadOnlyList<ulong> ReplacementMaterialIds);