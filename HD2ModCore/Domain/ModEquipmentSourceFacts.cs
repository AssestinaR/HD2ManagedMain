using AdaptationPatchTocEntry = HD2ModAdaptation.PatchReconstruction.PatchTocEntry;

namespace HD2ModCore.Domain;

// 作用：描述一个 Mod 可作为装备替换来源的部件映射、真实几何和已准备 TOC 条目。
// Purpose: Describes a Mod's equipment source mappings, real geometry, and prepared TOC entries.
public sealed record ModEquipmentSourceFacts(
	ModNodeId NodeId,
	string? ContentGeneration,
	IReadOnlyList<string> SourcePatchTocPaths,
	IReadOnlyList<EquipmentUnitCatalogEntry> SourceCandidates,
	IReadOnlyList<EquipmentUnitCatalogEntry> TargetCandidates,
	IReadOnlyDictionary<string, IReadOnlyList<AdaptationPatchTocEntry>> PreparedEntries,
	IReadOnlyList<ModSourceUnitFacts> Units,
	IReadOnlyList<CoreIssue> Issues)
{
	public bool HasUsableSource => SourcePatchTocPaths.Count != 0 && SourceCandidates.Count != 0;

	public IReadOnlyList<AdaptationPatchTocEntry> GetPreparedEntries(string patchTocPath)
	{
		if (string.IsNullOrWhiteSpace(patchTocPath)) return Array.Empty<AdaptationPatchTocEntry>();
		return PreparedEntries.TryGetValue(Path.GetFullPath(patchTocPath), out var entries)
			? entries
			: Array.Empty<AdaptationPatchTocEntry>();
	}
}
