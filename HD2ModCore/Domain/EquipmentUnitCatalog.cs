using HD2ModAdaptation.Analysis;

namespace HD2ModCore.Domain;

// Purpose: Describes one indexed Armor or Helmet archive and its current visual Unit meshes.
public sealed record EquipmentUnitCatalogEntry(
	string ArchiveId,
	string Category,
	string DisplayName,
	IReadOnlyList<EquipmentUnitPart> Parts);

// Purpose: Describes one visible, non-LOD Unit mesh that can participate in source or target selection.
public sealed record EquipmentUnitPart(
	AssetKey UnitAssetKey,
	int MeshInfoIndex,
	uint MeshId,
	UnitMeshPartKind PartKind,
	UnitMeshPartLayer Layer,
	UnitMeshBodyVariant BodyVariant,
	string SemanticName,
	int Confidence,
	IReadOnlyList<string> SharedArchiveIds)
{
	public long StoredBytes { get; init; }
	public string StoredSizeText => StoredBytes <= 0 ? "大小未知" : StoredBytes switch
	{
		>= 1024 * 1024 => $"{StoredBytes / (1024d * 1024d):0.0} MiB",
		>= 1024 => $"{StoredBytes / 1024d:0.0} KiB",
		_ => $"{StoredBytes} B"
	};
}

// Purpose: Selects the preferred source body shape only when an exact target-shape match cannot decide the assignment.
public enum CrossArmorBodyVariantPreference { Slim, Stocky }

// Purpose: Selects the preferred layer only when a same-layer candidate is unavailable.
public enum CrossArmorLayerPreference { Armor, Undergarment, Accessory }

// Purpose: Identifies one physical target mesh. Multiple selected archives can reference this one output object.
public sealed record CrossArmorPhysicalTargetKey(AssetKey UnitAssetKey, int MeshInfoIndex);

// Purpose: Preserves a user-selected source assignment for one physical target across automatic plan refreshes.
public sealed record CrossArmorManualMapping(CrossArmorPhysicalTargetKey Target, AssetKey SourceUnitAssetKey, int SourceMeshInfoIndex);

// Purpose: Preserves a user decision to minify one physical target instead of accepting an automatic assignment.
public sealed record CrossArmorManualSuppression(CrossArmorPhysicalTargetKey Target);

// Purpose: Provides an immutable source/target preview for a cross-equipment transfer before any Patch is written.
public sealed record CrossArmorTransferPlan(
	IReadOnlyList<EquipmentUnitCatalogEntry> SourceCandidates,
	EquipmentUnitCatalogEntry? SelectedSource,
	IReadOnlyList<EquipmentUnitCatalogEntry> SelectedTargets,
	IReadOnlyList<CrossArmorTransferMapping> Mappings,
	IReadOnlyList<CrossArmorTransferImpact> Impacts,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool CanContinue => SelectedSource is not null && SelectedTargets.Count != 0 && Mappings.Count != 0 && Issues.All(issue => issue.Severity != CoreIssueSeverity.Error);
}

// Purpose: Predicts a same-part source mesh assignment or target mesh minification without writing an archive.
public sealed record CrossArmorTransferMapping(
	CrossArmorPhysicalTargetKey PhysicalTarget,
	EquipmentUnitPart Target,
	EquipmentUnitPart? Source,
	bool WillReplace,
	string Reason,
	IReadOnlyList<string> UsedByArchiveIds,
	IReadOnlyList<string> UsedByDisplayNames,
	bool IsManual,
	bool IsSuppressed)
{
	public int HitCount { get; init; }
}

// Purpose: Explains another indexed equipment archive affected because it shares a Unit that will be written.
public sealed record CrossArmorTransferImpact(
	string ArchiveId,
	string DisplayName,
	AssetKey SharedUnitAssetKey,
	UnitMeshPartKind PartKind,
	UnitMeshPartLayer Layer);

// Purpose: Chooses whether a cross-armor Unit keeps source material references directly or only uses fully resolved source material closures.
public enum CrossArmorMaterialBindingMode
{
	PreserveSourceReferences,
	RequireCompleteSourceClosure
}

// Purpose: Captures the approved source Patch, read-only mapping plan, and output destination for one isolated cross-armor candidate.
public sealed record CrossArmorTransferCandidateRequest(
	string SourcePatchTocPath,
	string GameDataDirectory,
	string OutputDirectory,
	CrossArmorTransferPlan Plan,
	CrossArmorMaterialBindingMode MaterialBindingMode = CrossArmorMaterialBindingMode.PreserveSourceReferences,
	IReadOnlyList<HD2ModAdaptation.PatchReconstruction.PatchTocEntry>? PreparedSourceEntries = null);

// Purpose: Reports the independent cross-armor candidate output without changing the source Mod or any profile.
public sealed record CrossArmorTransferCandidateResult(
	bool IsSuccessful,
	string? OutputDirectory,
	string? ReportPath,
	int OutputUnitCount,
	int ReplacementMeshCount,
	int MinifiedMeshCount,
	IReadOnlyList<CoreIssue> Issues);
